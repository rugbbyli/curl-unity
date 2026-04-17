using System.Threading;
using CurlUnity.Native;

namespace CurlUnity.Core
{
    /// <summary>
    /// 引用计数式管理 curl_global_init / curl_global_cleanup。
    /// 第一个 CurlHttpClient 构造时 Acquire，最后一个 Dispose 时 Release。
    /// </summary>
    internal static class CurlGlobal
    {
        private static int _refCount;
        private static readonly object _lock = new();

        public static void Acquire()
        {
            Acquire(CurlNativeApi.Instance);
        }

        internal static void Acquire(ICurlApi api)
        {
            lock (_lock)
            {
                if (_refCount++ == 0)
                    api.CurlGlobalInit(CurlNative.CURL_GLOBAL_DEFAULT);
            }
        }

        public static void Release()
        {
            Release(CurlNativeApi.Instance);
        }

        internal static void Release(ICurlApi api)
        {
            lock (_lock)
            {
                if (--_refCount == 0)
                    api.CurlGlobalCleanup();
            }
        }
    }
}
