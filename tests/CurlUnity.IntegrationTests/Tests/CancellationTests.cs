using System;
using System.Threading;
using System.Threading.Tasks;
using CurlUnity.Http;
using CurlUnity.IntegrationTests.Fixtures;
using Xunit;

namespace CurlUnity.IntegrationTests.Tests
{
    [Collection("Integration")]
    public class CancellationTests : IDisposable
    {
        private readonly TestServerFixture _server;
        private readonly CurlHttpClient _client;

        public CancellationTests(TestServerFixture server, CurlGlobalFixture _)
        {
            _server = server;
            _client = new CurlHttpClient();
            _client.PreferredVersion = HttpVersion.Default;
        }

        public void Dispose() => _client.Dispose();

        [Fact]
        public async Task CancellationToken_CancelsDelayedRequest()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            var req = new HttpRequest { Url = $"{_server.HttpUrl}/delay/10000" };

            await Assert.ThrowsAsync<TaskCanceledException>(
                () => _client.SendAsync(req, cts.Token));
        }

        [Fact]
        public async Task AlreadyCancelledToken_ThrowsImmediately()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var req = new HttpRequest { Url = $"{_server.HttpUrl}/hello" };

            await Assert.ThrowsAsync<TaskCanceledException>(
                () => _client.SendAsync(req, cts.Token));
        }
    }
}
