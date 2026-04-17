using System;
using System.Text.Json;
using System.Threading.Tasks;
using CurlUnity.Http;
using CurlUnity.IntegrationTests.Fixtures;
using Xunit;

namespace CurlUnity.IntegrationTests.Tests
{
    [Collection("Integration")]
    public class CookieTests : IDisposable
    {
        private readonly TestServerFixture _server;
        private readonly CurlHttpClient _client;

        public CookieTests(TestServerFixture server, CurlGlobalFixture _)
        {
            _server = server;
            _client = new CurlHttpClient();
            _client.PreferredVersion = HttpVersion.Default;
        }

        public void Dispose() => _client.Dispose();

        [Fact]
        public async Task EnableCookies_PersistsThroughRedirect()
        {
            // /set-cookie-and-redirect sets a cookie, then redirects to /check-cookie.
            // Within the same easy handle (single SendAsync), cookies persist.
            var req = new HttpRequest
            {
                Url = $"{_server.HttpUrl}/set-cookie-and-redirect",
                EnableCookies = true,
            };

            using var resp = await _client.SendAsync(req);

            Assert.True(resp.HasResponse);
            Assert.Equal(200, resp.StatusCode);

            var json = JsonDocument.Parse(resp.Body);
            Assert.True(json.RootElement.GetProperty("hasCookie").GetBoolean());
            Assert.Equal("cookie_value", json.RootElement.GetProperty("value").GetString());
        }

        [Fact]
        public async Task DisabledCookies_NoCookieThroughRedirect()
        {
            var req = new HttpRequest
            {
                Url = $"{_server.HttpUrl}/set-cookie-and-redirect",
                EnableCookies = false,
            };

            using var resp = await _client.SendAsync(req);

            Assert.True(resp.HasResponse);
            Assert.Equal(200, resp.StatusCode);

            var json = JsonDocument.Parse(resp.Body);
            Assert.False(json.RootElement.GetProperty("hasCookie").GetBoolean());
        }

        [Fact]
        public async Task Cookies_NotSharedAcrossSeparateRequests()
        {
            // Each SendAsync creates a new easy handle, so cookies don't persist.
            // This documents a known limitation of the current implementation.
            var req1 = new HttpRequest
            {
                Url = $"{_server.HttpUrl}/set-cookie",
                EnableCookies = true,
            };
            using var resp1 = await _client.SendAsync(req1);
            Assert.True(resp1.HasResponse);

            var req2 = new HttpRequest
            {
                Url = $"{_server.HttpUrl}/check-cookie",
                EnableCookies = true,
            };
            using var resp2 = await _client.SendAsync(req2);
            Assert.True(resp2.HasResponse);

            var json = JsonDocument.Parse(resp2.Body);
            // Cookie is NOT persisted — this is expected with current per-request handle design
            Assert.False(json.RootElement.GetProperty("hasCookie").GetBoolean());
        }
    }
}
