using System;
using System.Threading;
using System.Threading.Tasks;

namespace CurlUnity.Http
{
    public interface IHttpClient : IDisposable
    {
        Task<IHttpResponse> SendAsync(IHttpRequest request, CancellationToken ct = default);
    }
}
