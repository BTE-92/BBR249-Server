using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BBR249Server.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace BBR249Server.Net;

public static class Router
{
	private static readonly Dictionary<string, Func<Ctx, Task>> Routes = new Dictionary<string, Func<Ctx, Task>>(StringComparer.OrdinalIgnoreCase);

	private static readonly Regex TsParam = new Regex("([?&])ts=[^&]*&?", RegexOptions.Compiled);

	private static readonly HashSet<string> SeenUnhandled = new HashSet<string>();

	public static int Count => Routes.Count;

	public static void Map(string path, Func<Ctx, Task> handler)
	{
		Routes[path] = handler;
	}

	private static string Pretty(string rawTarget)
	{
		string text = TsParam.Replace(rawTarget, (Match m) => (!m.Value.EndsWith("&")) ? "" : m.Groups[1].Value);
		return text.TrimEnd('?', '&');
	}

	public static async Task Handle(HttpContext http)
	{
		byte[] body;
		using (MemoryStream ms = new MemoryStream())
		{
			await http.Request.Body.CopyToAsync(ms);
			body = ms.ToArray();
		}
		Ctx ctx = new Ctx(http, body);
		string path = ctx.Path;
		string line = http.Request.Method + " " + Pretty(ctx.RawTarget);
		try
		{
			if (Routes.TryGetValue(path, out Func<Ctx, Task> value))
			{
				await value(ctx);
				Log(line, http);
				return;
			}
			if (await TryServeStatic(ctx))
			{
				Log(line, http);
				return;
			}
			string host = http.Request.Host.Host;
			if (host.Equals("graph.facebook.com", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/v2."))
			{
				if (Settings.Verbose)
				{
					Console.WriteLine("[Facebook] " + line);
				}
				await ctx.Text("{}");
			}
			else
			{
				if (SeenUnhandled.Add(path))
				{
					Console.WriteLine("[HTTP] not implemented (answered with {}): " + path);
				}
				await ctx.Json(new JsonObject());
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine("[HTTP] ERROR " + line + ": " + (Settings.Verbose ? ex.ToString() : ex.Message));
			if (!http.Response.HasStarted)
			{
				http.Response.StatusCode = 500;
				http.Response.Headers["PLAY_STATUS"] = "ERROR";
				http.Response.Headers["PLAY_ERROR"] = "FATAL_ERROR";
			}
		}
	}

	private static void Log(string line, HttpContext http)
	{
		StringValues value2;
		string value = (http.Response.Headers.TryGetValue("PLAY_STATUS", out value2) ? value2.ToString() : http.Response.StatusCode.ToString());
		StringValues value4;
		string value3 = (http.Response.Headers.TryGetValue("PLAY_ERROR", out value4) ? (" " + value4) : "");
		Console.WriteLine($"[HTTP] {line} -> {value}{value3}");
	}

	private static async Task<bool> TryServeStatic(Ctx ctx)
	{
		string path = ctx.Path;
		if (!path.StartsWith("/ServerData/", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		string path2 = Uri.UnescapeDataString(path.Substring("/ServerData/".Length)).Replace('/', Path.DirectorySeparatorChar);
		string fullPath = Path.GetFullPath(Path.Combine(Settings.ServerDataDir, path2));
		string value = Path.GetFullPath(Settings.ServerDataDir) + Path.DirectorySeparatorChar;
		if (!fullPath.StartsWith(value, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
		{
			ctx.Http.Response.StatusCode = 404;
			return true;
		}
		HttpResponse response = ctx.Http.Response;
		response.StatusCode = 200;
		response.ContentType = "application/octet-stream";
		response.ContentLength = new FileInfo(fullPath).Length;
		await response.SendFileAsync(fullPath);
		return true;
	}
}
