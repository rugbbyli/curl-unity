using System;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using CurlUnity.IntegrationTests.TestServer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CurlUnity.IntegrationTests.Fixtures
{
    public class TestServerFixture : IAsyncLifetime
    {
        private WebApplication _app;
        private X509Certificate2 _certificate;

        /// <summary>HTTP/1.1 base URL, e.g. "http://127.0.0.1:12345"</summary>
        public string HttpUrl { get; private set; }

        /// <summary>HTTPS (HTTP/1.1 + HTTP/2 via ALPN) base URL, e.g. "https://127.0.0.1:12346"</summary>
        public string HttpsUrl { get; private set; }

        public async Task InitializeAsync()
        {
            _certificate = CertificateHelper.CreateSelfSigned();

            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();

            builder.WebHost.ConfigureKestrel(options =>
            {
                // HTTP/1.1
                options.Listen(IPAddress.Loopback, 0, lo =>
                {
                    lo.Protocols = HttpProtocols.Http1;
                });

                // HTTPS: HTTP/1.1 + HTTP/2 (ALPN negotiation)
                options.Listen(IPAddress.Loopback, 0, lo =>
                {
                    lo.Protocols = HttpProtocols.Http1AndHttp2;
                    lo.UseHttps(_certificate);
                });
            });

            _app = builder.Build();
            TestEndpoints.Map(_app);
            await _app.StartAsync();

            // Extract assigned ports from server features
            var addresses = _app.Services
                .GetRequiredService<IServer>()
                .Features
                .Get<IServerAddressesFeature>()
                .Addresses
                .ToArray();

            foreach (var addr in addresses)
            {
                if (addr.StartsWith("https://"))
                    HttpsUrl = addr;
                else if (addr.StartsWith("http://"))
                    HttpUrl = addr;
            }
        }

        public async Task DisposeAsync()
        {
            if (_app != null)
                await _app.DisposeAsync();
            _certificate?.Dispose();
        }
    }
}
