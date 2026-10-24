using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using BBR249Server.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace BBR249Server.Net;

public sealed class Ctx
{
	public HttpContext Http { get; }

	public byte[] Body { get; }

	public string RawTarget { get; }

	public string Path => Http.Request.Path.Value ?? "";

	public string PlayerId => Header("PLAYER_ID");

	public Ctx(HttpContext http, byte[] body)
	{
		Http = http;
		Body = body;
		RawTarget = http.Features.Get<IHttpRequestFeature>()?.RawTarget ?? (http.Request.Path + http.Request.QueryString);
	}

	public string Q(string key, string def = "")
	{
		if (!Http.Request.Query.TryGetValue(key, out var value) || value.Count <= 0)
		{
			return def;
		}
		return value[0] ?? def;
	}

	public int QInt(string key, int def = 0)
	{
		if (!int.TryParse(Q(key), out var result))
		{
			return def;
		}
		return result;
	}

	public bool QBool(string key)
	{
		return string.Equals(Q(key), "true", StringComparison.OrdinalIgnoreCase);
	}

	public string Header(string name)
	{
		if (!Http.Request.Headers.TryGetValue(name, out var value) || value.Count <= 0)
		{
			return "";
		}
		return value[0] ?? "";
	}

	public JsonNode? BodyJson()
	{
		if (Body.Length == 0)
		{
			return null;
		}
		try
		{
			return JsonNode.Parse(Body);
		}
		catch
		{
			return null;
		}
	}

	public Task Json(JsonNode node)
	{
		return JsonText(node.ToJsonString());
	}

	public async Task JsonText(string text)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(text);
		HttpResponse response = Http.Response;
		response.StatusCode = 200;
		response.ContentType = "application/json";
		response.Headers["PLAY_STATUS"] = "OK";
		response.Headers["PLAY_HASH"] = Hasher.HashString(RawTarget + text);
		response.ContentLength = bytes.Length;
		await response.Body.WriteAsync(bytes);
	}

	public async Task Text(string text)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(text);
		HttpResponse response = Http.Response;
		response.StatusCode = 200;
		response.ContentType = "application/json";
		response.ContentLength = bytes.Length;
		await response.Body.WriteAsync(bytes);
	}

	public async Task Bytes(byte[] data, string? fileSizes = null, IDictionary<string, string>? extraHeaders = null)
	{
		HttpResponse response = Http.Response;
		response.StatusCode = 200;
		response.ContentType = "application/octet-stream";
		response.Headers["PLAY_STATUS"] = "OK";
		response.Headers["PLAY_HASH"] = Hasher.HashBytes(RawTarget, data);
		if (fileSizes != null)
		{
			response.Headers["FILE_SIZES"] = fileSizes;
		}
		if (extraHeaders != null)
		{
			foreach (KeyValuePair<string, string> extraHeader in extraHeaders)
			{
				response.Headers[extraHeader.Key] = extraHeader.Value;
			}
		}
		response.ContentLength = data.Length;
		await response.Body.WriteAsync(data);
	}

	public async Task Error(string code = "NOT_FOUND")
	{
		HttpResponse response = Http.Response;
		response.StatusCode = 200;
		response.ContentType = "application/json";
		response.Headers["PLAY_STATUS"] = "ERROR";
		response.Headers["PLAY_ERROR"] = code;
		byte[] bytes = Encoding.UTF8.GetBytes("{}");
		response.ContentLength = bytes.Length;
		await response.Body.WriteAsync(bytes);
	}
}
