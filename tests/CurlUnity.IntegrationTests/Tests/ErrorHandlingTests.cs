using System;
using System.Threading.Tasks;
using CurlUnity.Http;
using CurlUnity.IntegrationTests.Fixtures;
using Xunit;

namespace CurlUnity.IntegrationTests.Tests
{
    [Collection("Integration")]
    public class ErrorHandlingTests : IDisposable
    {
        private readonly TestServerFixture _server;
        private readonly CurlHttpClient _client;

        public ErrorHandlingTests(TestServerFixture server, CurlGlobalFixture _)
        {
            _server = server;
            _client = new CurlHttpClient();
            _client.PreferredVersion = HttpVersion.Default;
        }

        public void Dispose() => _client.Dispose();

        [Fact]
        public async Task ConnectionRefused_HasResponseIsFalse()
        {
            // Port 1 should not be listening
            using var resp = await _client.GetAsync("http://localhost:1/nope");

            Assert.False(resp.HasResponse);
            Assert.NotEqual(0, resp.ErrorCode);
            Assert.NotNull(resp.ErrorMessage);
        }

        [Fact]
        public async Task Timeout_ReturnsCurlError()
        {
            var req = new HttpRequest
            {
                Url = $"{_server.HttpUrl}/delay/5000",
                TimeoutMs = 500,
            };

            using var resp = await _client.SendAsync(req);

            Assert.False(resp.HasResponse);
            Assert.Equal(28, resp.ErrorCode); // CURLE_OPERATION_TIMEDOUT
        }

        [Fact]
        public async Task DnsFailure_ReturnsCurlError()
        {
            using var resp = await _client.GetAsync("http://this.host.does.not.exist.invalid/test");

            Assert.False(resp.HasResponse);
            Assert.NotEqual(0, resp.ErrorCode);
            Assert.NotNull(resp.ErrorMessage);
        }

        [Fact]
        public async Task ErrorCode_HasMatchingMessage()
        {
            using var resp = await _client.GetAsync("http://localhost:1/nope");

            Assert.False(resp.HasResponse);
            var msg = resp.ErrorMessage;
            Assert.NotNull(msg);
            Assert.True(msg.Length > 0);
        }

        [Fact]
        public async Task ConnectTimeout_FiresBeforeTotalTimeout()
        {
            // 10.255.255.1 is a non-routable IP — TCP SYN will hang
            var req = new HttpRequest
            {
                Url = "http://10.255.255.1/",
                ConnectTimeoutMs = 2000,
                TimeoutMs = 60000, // total timeout is much larger
            };

            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var resp = await _client.SendAsync(req);
            sw.Stop();

            Assert.False(resp.HasResponse);
            Assert.NotEqual(0, resp.ErrorCode);
            // Must complete well before total timeout (60s).
            // macOS TCP stack may add OS-level retries, so allow up to 15s.
            Assert.True(sw.ElapsedMilliseconds < 15000,
                $"Expected connect timeout well before 60s total, took {sw.ElapsedMilliseconds}ms");
        }

        [Fact]
        public async Task VerifySSL_True_RejectsSelfSignedCert()
        {
            using var client = new CurlHttpClient();
            client.VerifySSL = true; // default, but explicit for clarity

            using var resp = await client.GetAsync($"{_server.HttpsUrl}/hello");

            // Self-signed cert should be rejected by Apple SecTrust
            Assert.False(resp.HasResponse);
            Assert.NotEqual(0, resp.ErrorCode);
        }
    }
}
