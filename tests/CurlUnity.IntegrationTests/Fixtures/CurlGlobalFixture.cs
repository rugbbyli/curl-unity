using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using CurlUnity.Native;

namespace CurlUnity.IntegrationTests.Fixtures
{
    public class CurlGlobalFixture : IDisposable
    {
        public CurlGlobalFixture()
        {
            // Ensure the native library can be found in the test output directory
            NativeLibrary.SetDllImportResolver(
                typeof(CurlNative).Assembly,
                ResolveDllImport);

            var result = CurlNative.curl_global_init(CurlNative.CURL_GLOBAL_DEFAULT);
            if (result != CurlNative.CURLE_OK)
                throw new InvalidOperationException($"curl_global_init failed: {result}");
        }

        public void Dispose()
        {
            CurlNative.curl_global_cleanup();
        }

        private static IntPtr ResolveDllImport(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (libraryName != "curl_unity")
                return IntPtr.Zero;

            // Try loading from the assembly's directory first
            var assemblyDir = Path.GetDirectoryName(assembly.Location);
            if (assemblyDir != null)
            {
                var libPath = Path.Combine(assemblyDir, "libcurl_unity.dylib");
                if (File.Exists(libPath) && NativeLibrary.TryLoad(libPath, out var handle))
                    return handle;
            }

            // Fallback: let the OS find it
            if (NativeLibrary.TryLoad("libcurl_unity", out var fallback))
                return fallback;

            return IntPtr.Zero;
        }
    }
}
