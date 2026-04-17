using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using CurlUnity.Core;
using CurlUnity.Native;

namespace CurlUnity.Http
{
    internal class HttpResponse : IHttpResponse
    {
        private IntPtr _easyHandle;
        private readonly int _curlCode;
        private readonly long _statusCode;
        private readonly byte[] _body;
        private readonly byte[] _rawHeaders;
        private IReadOnlyDictionary<string, string[]> _parsedHeaders;

        internal HttpResponse(CurlResponse raw)
        {
            _easyHandle = raw.EasyHandle;
            _curlCode = raw.CurlCode;
            _statusCode = raw.StatusCode;
            _body = raw.Body;
            _rawHeaders = raw.RawHeaders;
        }

        internal IntPtr EasyHandle => _easyHandle;

        public bool IsDisposed => _easyHandle == IntPtr.Zero;
        public bool HasResponse => _curlCode == CurlNative.CURLE_OK;
        public int StatusCode => (int)_statusCode;
        public int ErrorCode => _curlCode;

        public string ErrorMessage
        {
            get
            {
                if (_curlCode == CurlNative.CURLE_OK) return null;
                var ptr = CurlNative.curl_easy_strerror(_curlCode);
                return ptr != IntPtr.Zero ? Marshal.PtrToStringAnsi(ptr) : null;
            }
        }

        public HttpVersion Version
        {
            get
            {
                if (_easyHandle == IntPtr.Zero) return HttpVersion.Default;
                CurlNative.curl_unity_getinfo_long(_easyHandle, CurlNative.CURLINFO_HTTP_VERSION, out var v);
                return (HttpVersion)(int)v;
            }
        }

        public byte[] Body => _body;

        // --- 惰性 getinfo 属性，每次直接读 ---

        public string ContentType
        {
            get
            {
                if (_easyHandle == IntPtr.Zero) return null;
                CurlNative.curl_unity_getinfo_string(_easyHandle, CurlNative.CURLINFO_CONTENT_TYPE, out var ptr);
                return ptr != IntPtr.Zero ? Marshal.PtrToStringAnsi(ptr) : null;
            }
        }

        public long ContentLength
        {
            get
            {
                if (_easyHandle == IntPtr.Zero) return -1;
                CurlNative.curl_unity_getinfo_off_t(_easyHandle, CurlNative.CURLINFO_CONTENT_LENGTH_DOWNLOAD_T, out var v);
                return v;
            }
        }

        public string EffectiveUrl
        {
            get
            {
                if (_easyHandle == IntPtr.Zero) return null;
                CurlNative.curl_unity_getinfo_string(_easyHandle, CurlNative.CURLINFO_EFFECTIVE_URL, out var ptr);
                return ptr != IntPtr.Zero ? Marshal.PtrToStringAnsi(ptr) : null;
            }
        }

        public int RedirectCount
        {
            get
            {
                if (_easyHandle == IntPtr.Zero) return 0;
                CurlNative.curl_unity_getinfo_long(_easyHandle, CurlNative.CURLINFO_REDIRECT_COUNT, out var v);
                return (int)v;
            }
        }

        public IReadOnlyDictionary<string, string[]> Headers
        {
            get
            {
                if (_rawHeaders == null) return null;
                return _parsedHeaders ??= ParseHeaders(_rawHeaders);
            }
        }

        public void Dispose()
        {
            if (_easyHandle != IntPtr.Zero)
            {
                CurlNative.curl_easy_cleanup(_easyHandle);
                _easyHandle = IntPtr.Zero;
            }
        }

        private static IReadOnlyDictionary<string, string[]> ParseHeaders(byte[] raw)
        {
            var dict = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var text = Encoding.UTF8.GetString(raw);

            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.TrimEnd('\r');
                if (trimmed.Length == 0 || trimmed.StartsWith("HTTP/"))
                    continue;

                var colonIdx = trimmed.IndexOf(':');
                if (colonIdx <= 0) continue;

                var name = trimmed.Substring(0, colonIdx).Trim().ToLowerInvariant();
                var value = trimmed.Substring(colonIdx + 1).Trim();

                if (!dict.TryGetValue(name, out var list))
                {
                    list = new List<string>();
                    dict[name] = list;
                }
                list.Add(value);
            }

            var result = new Dictionary<string, string[]>(dict.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in dict)
                result[kv.Key] = kv.Value.ToArray();
            return result;
        }
    }
}
