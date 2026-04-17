using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CurlUnity.Http;
using CurlUnity.IntegrationTests.Fixtures;
using Xunit;

namespace CurlUnity.IntegrationTests.Tests
{
    [Collection("Integration")]
    public class StreamingTests : IDisposable
    {
        private readonly TestServerFixture _server;
        private readonly CurlHttpClient _client;

        public StreamingTests(TestServerFixture server, CurlGlobalFixture _)
        {
            _server = server;
            _client = new CurlHttpClient();
            _client.PreferredVersion = HttpVersion.Default;
        }

        public void Dispose() => _client.Dispose();

        [Fact]
        public async Task OnDataReceived_ReceivesAllBytes()
        {
            const int size = 50_000;
            var chunks = new List<byte[]>();

            var req = new HttpRequest
            {
                Url = $"{_server.HttpUrl}/bytes/{size}",
                OnDataReceived = (buf, offset, len) =>
                {
                    var chunk = new byte[len];
                    Buffer.BlockCopy(buf, offset, chunk, 0, len);
                    chunks.Add(chunk);
                }
            };

            using var resp = await _client.SendAsync(req);

            Assert.True(resp.HasResponse);
            Assert.Equal(200, resp.StatusCode);
            Assert.Null(resp.Body); // Body is null in streaming mode
            Assert.True(chunks.Count > 0);
            Assert.Equal(size, chunks.Sum(c => c.Length));
        }

        [Fact]
        public async Task OnDataReceived_CallbackInvokedMultipleTimes()
        {
            const int size = 100_000;
            int callbackCount = 0;

            var req = new HttpRequest
            {
                Url = $"{_server.HttpUrl}/bytes/{size}",
                OnDataReceived = (buf, offset, len) =>
                {
                    callbackCount++;
                }
            };

            using var resp = await _client.SendAsync(req);

            Assert.True(resp.HasResponse);
            Assert.True(callbackCount > 0);
        }
    }
}
