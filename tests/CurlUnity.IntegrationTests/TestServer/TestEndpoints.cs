using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace CurlUnity.IntegrationTests.TestServer
{
    internal static class TestEndpoints
    {
        public static void Map(WebApplication app)
        {
            app.MapMethods("/hello", new[] { "GET", "HEAD" }, () => "Hello, World!");

            app.MapGet("/status/{code:int}", (int code) => Results.StatusCode(code));

            app.MapPost("/echo", async (HttpRequest req) =>
            {
                using var reader = new StreamReader(req.Body);
                var body = await reader.ReadToEndAsync();
                return Results.Text(body, req.ContentType ?? "text/plain");
            });

            // Binary echo: reads raw bytes and returns them unchanged
            app.MapPost("/echo-bytes", async (HttpRequest req) =>
            {
                using var ms = new System.IO.MemoryStream();
                await req.Body.CopyToAsync(ms);
                return Results.Bytes(ms.ToArray(), "application/octet-stream");
            });

            // Echo that accepts all methods and returns method + body info
            app.MapMethods("/method-echo", new[] { "GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS" },
                async (HttpRequest req) =>
                {
                    string body = null;
                    if (req.ContentLength > 0 || req.Headers.ContainsKey("Transfer-Encoding"))
                    {
                        using var reader = new StreamReader(req.Body);
                        body = await reader.ReadToEndAsync();
                    }
                    return Results.Json(new { method = req.Method, bodyLength = body?.Length ?? 0, body });
                });

            // Cookie: set a cookie
            app.MapGet("/set-cookie", (HttpContext ctx) =>
            {
                ctx.Response.Cookies.Append("test_cookie", "cookie_value", new CookieOptions
                {
                    Path = "/",
                    HttpOnly = false,
                });
                return Results.Text("cookie set");
            });

            // Cookie: check if cookie was sent back
            app.MapGet("/check-cookie", (HttpRequest req) =>
            {
                var hasCookie = req.Cookies.TryGetValue("test_cookie", out var value);
                return Results.Json(new { hasCookie, value });
            });

            // Cookie: set cookie then redirect to /check-cookie (same easy handle)
            app.MapGet("/set-cookie-and-redirect", (HttpContext ctx) =>
            {
                ctx.Response.Cookies.Append("test_cookie", "cookie_value", new CookieOptions
                {
                    Path = "/",
                    HttpOnly = false,
                });
                var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
                return Results.Redirect($"{baseUrl}/check-cookie");
            });

            app.MapGet("/echo-headers", (HttpRequest req) =>
            {
                var dict = req.Headers.ToDictionary(
                    h => h.Key,
                    h => h.Value.ToArray());
                return Results.Json(dict);
            });

            app.MapGet("/delay/{ms:int}", async (int ms) =>
            {
                await Task.Delay(ms);
                return Results.Text("delayed");
            });

            app.MapGet("/bytes/{count:int}", (int count) =>
            {
                var data = new byte[count];
                new Random(42).NextBytes(data);
                return Results.Bytes(data, "application/octet-stream");
            });

            app.MapGet("/redirect/{n:int}", (int n, HttpRequest req) =>
            {
                if (n <= 0)
                    return Results.Text("final");
                var baseUrl = $"{req.Scheme}://{req.Host}";
                return Results.Redirect($"{baseUrl}/redirect/{n - 1}");
            });

            app.MapGet("/custom-headers", (HttpContext ctx) =>
            {
                ctx.Response.Headers["X-Custom-One"] = "value1";
                ctx.Response.Headers["X-Custom-Two"] = "value2";
                return Results.Text("with-headers");
            });

            app.MapGet("/json", () => Results.Json(new { message = "ok", number = 42 }));

            app.MapGet("/protocol", (HttpRequest req) => Results.Text(req.Protocol));
        }
    }
}
