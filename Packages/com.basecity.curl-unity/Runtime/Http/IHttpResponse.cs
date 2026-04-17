using System;
using System.Collections.Generic;

namespace CurlUnity.Http
{
    public interface IHttpResponse : IDisposable
    {
        /// <summary>底层资源是否已释放。</summary>
        bool IsDisposed { get; }

        /// <summary>是否收到了 HTTP 响应（无网络错误）。</summary>
        bool HasResponse { get; }

        /// <summary>HTTP 状态码。HasResponse=false 时为 0。</summary>
        int StatusCode { get; }

        /// <summary>实际使用的 HTTP 协议版本。</summary>
        HttpVersion Version { get; }

        /// <summary>CURLcode。0 = 成功。</summary>
        int ErrorCode { get; }

        /// <summary>错误描述。成功时为 null。</summary>
        string ErrorMessage { get; }

        /// <summary>响应体。流式模式下为 null。</summary>
        byte[] Body { get; }

        // --- curl 内部缓存的常用 header 信息（始终可用，无需开启 EnableResponseHeaders）---

        /// <summary>Content-Type（如 "application/json; charset=utf-8"），无则 null。</summary>
        string ContentType { get; }

        /// <summary>Content-Length，未知时为 -1。</summary>
        long ContentLength { get; }

        /// <summary>重定向后的最终 URL。无重定向时等于请求 URL。</summary>
        string EffectiveUrl { get; }

        /// <summary>重定向次数。</summary>
        int RedirectCount { get; }

        // --- 完整响应头（需要 EnableResponseHeaders=true，否则为 null）---

        /// <summary>
        /// 所有响应头。key 为小写 header name，value 为该 header 的所有值。
        /// </summary>
        IReadOnlyDictionary<string, string[]> Headers { get; }
    }
}
