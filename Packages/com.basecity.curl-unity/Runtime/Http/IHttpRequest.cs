using System;
using System.Collections.Generic;

namespace CurlUnity.Http
{
    public interface IHttpRequest
    {
        HttpMethod Method { get; set; }
        string Url { get; set; }
        IEnumerable<KeyValuePair<string, string>> Headers { get; set; }
        byte[] Body { get; set; }

        /// <summary>TCP 建连超时（毫秒），0 = 不限</summary>
        int ConnectTimeoutMs { get; set; }

        /// <summary>整个请求响应超时（毫秒），0 = 不限</summary>
        int TimeoutMs { get; set; }

        /// <summary>是否捕获响应头。默认 false。</summary>
        bool EnableResponseHeaders { get; set; }

        /// <summary>是否启用 curl cookie 引擎（跨请求自动管理 cookie）。默认 false。</summary>
        bool EnableCookies { get; set; }

        /// <summary>
        /// 流式数据回调（文件下载等场景）。
        /// 设置后响应体不缓冲，数据逐块交付，Response.Body 为 null。
        /// 在后台线程调用。参数: (buffer, offset, length)
        /// </summary>
        Action<byte[], int, int> OnDataReceived { get; set; }
    }
}
