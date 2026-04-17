using System;
using System.Text;
using System.Threading.Tasks;
using CurlUnity.Http;
using CurlUnity.IntegrationTests.Fixtures;
using Xunit;

namespace CurlUnity.IntegrationTests.Tests
{
    [Collection("Integration")]
    public class RedirectTests : IDisposable
    {
        private readonly TestServerFixture _server;
        private readonly CurlHttpClient _client;

        public RedirectTests(TestServerFixture server, CurlGlobalFixture _)
        {
            _server = server;
            _client = new CurlHttpClient();
            _client.PreferredVersion = HttpVersion.Default;
        }

        public void Dispose() => _client.Dispose();

        [Fact]
        public async Task FollowRedirects_ReachesFinalDestination()
        {
            using var resp = await _client.GetAsync($"{_server.HttpUrl}/redirect/3");

            Assert.True(resp.HasResponse);
            Assert.Equal(200, resp.StatusCode);
            Assert.Equal("final", Encoding.UTF8.GetString(resp.Body));
        }

        [Fact]
        public async Task RedirectCount_IsCorrect()
        {
            var req = new HttpRequest
            {
                Url = $"{_server.HttpUrl}/redirect/3",
                EnableResponseHeaders = true,
            };

            using var resp = await _client.SendAsync(req);

            Assert.True(resp.HasResponse);
            Assert.Equal(3, resp.RedirectCount);
        }

        [Fact]
        public async Task EffectiveUrl_PointsToFinalUrl()
        {
            using var resp = await _client.GetAsync($"{_server.HttpUrl}/redirect/2");

            Assert.True(resp.HasResponse);
            Assert.EndsWith("/redirect/0", resp.EffectiveUrl);
        }

        [Fact]
        public async Task ZeroRedirects_NoRedirectCount()
        {
            using var resp = await _client.GetAsync($"{_server.HttpUrl}/redirect/0");

            Assert.True(resp.HasResponse);
            Assert.Equal(200, resp.StatusCode);
            Assert.Equal(0, resp.RedirectCount);
            Assert.Equal("final", Encoding.UTF8.GetString(resp.Body));
        }

        [Fact]
        public async Task ManyRedirects_CurlDefaultLimitApplied()
        {
            // curl default CURLOPT_MAXREDIRS is -1 (unlimited when FOLLOWLOCATION is on),
            // but some builds default to 30 or 50. Test with a moderate chain.
            using var resp = await _client.GetAsync($"{_server.HttpUrl}/redirect/20");

            Assert.True(resp.HasResponse);
            Assert.Equal(200, resp.StatusCode);
            Assert.Equal(20, resp.RedirectCount);
        }
    }
}
