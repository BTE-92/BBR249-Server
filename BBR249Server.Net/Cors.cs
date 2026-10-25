using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace BBR249Server.Net;

// CORS for browser clients (a WebGL build of the game runs in a web page, so the browser checks every request).
//  - answers the "preflight" OPTIONS request the browser sends before a request with custom headers (PLAYER_ID, ...);
//  - allows any origin (the game sends no cookies, so "*" is allowed);
//  - exposes the response headers the game reads (PLAY_STATUS, PLAY_HASH, FILE_SIZES, GHOST_*...), which the browser
//    would otherwise hide from the page;
//  - answers the "Private Network Access" preflight Chrome sends when a web page calls a server on a local address.
public static class Cors
{
	private const string ExposedHeaders = "PLAY_STATUS, PLAY_HASH, PLAY_ERROR, FILE_SIZES, GHOST_TIME, GHOST_NAME, Content-Length, Content-Type";

	public static async Task Handle(HttpContext http, Func<Task> next)
	{
		HttpRequest request = http.Request;
		HttpResponse response = http.Response;
		if (!request.Headers.ContainsKey("Origin"))
		{
			await next();
			return;
		}
		response.Headers["Access-Control-Allow-Origin"] = "*";
		response.Headers["Access-Control-Expose-Headers"] = ExposedHeaders;
		response.Headers["Vary"] = "Origin";
		if (HttpMethods.IsOptions(request.Method))
		{
			string requested = request.Headers["Access-Control-Request-Headers"].ToString();
			response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE, OPTIONS";
			response.Headers["Access-Control-Allow-Headers"] = string.IsNullOrEmpty(requested) ? "*" : requested;
			response.Headers["Access-Control-Max-Age"] = "86400";
			if (request.Headers.ContainsKey("Access-Control-Request-Private-Network"))
			{
				response.Headers["Access-Control-Allow-Private-Network"] = "true";
			}
			response.StatusCode = 204;
			return;
		}
		await next();
	}
}
