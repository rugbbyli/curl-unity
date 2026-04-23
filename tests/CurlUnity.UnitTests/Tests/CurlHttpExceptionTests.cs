using CurlUnity.Http;
using Xunit;

namespace CurlUnity.UnitTests.Tests
{
    public class CurlHttpExceptionTests
    {
        /// <summary>
        /// 每个 HttpErrorKind 分类至少一个代表 CURLcode, 再加一个 default→Unknown 兜底。
        /// 不需要把所有 40+ 个 case 都测, 重复 pattern 只会增加维护成本而不增加信心。
        /// 这套代表性 code 对未来修改 MapEasyCode 时的误归类起到守门作用。
        /// </summary>
        [Theory]
        [InlineData(1, HttpErrorKind.InvalidUrl)]          // CURLE_UNSUPPORTED_PROTOCOL
        [InlineData(3, HttpErrorKind.InvalidUrl)]          // CURLE_URL_MALFORMAT
        [InlineData(5, HttpErrorKind.DnsFailed)]           // CURLE_COULDNT_RESOLVE_PROXY
        [InlineData(6, HttpErrorKind.DnsFailed)]           // CURLE_COULDNT_RESOLVE_HOST
        [InlineData(7, HttpErrorKind.ConnectFailed)]       // CURLE_COULDNT_CONNECT
        [InlineData(28, HttpErrorKind.Timeout)]            // CURLE_OPERATION_TIMEDOUT
        [InlineData(35, HttpErrorKind.TlsError)]           // CURLE_SSL_CONNECT_ERROR
        [InlineData(60, HttpErrorKind.TlsError)]           // CURLE_PEER_FAILED_VERIFICATION
        [InlineData(77, HttpErrorKind.TlsError)]           // CURLE_SSL_CACERT_BADFILE
        [InlineData(55, HttpErrorKind.NetworkIo)]          // CURLE_SEND_ERROR
        [InlineData(56, HttpErrorKind.NetworkIo)]          // CURLE_RECV_ERROR
        [InlineData(8, HttpErrorKind.ProtocolError)]       // CURLE_WEIRD_SERVER_REPLY
        [InlineData(95, HttpErrorKind.ProtocolError)]      // CURLE_HTTP3
        [InlineData(97, HttpErrorKind.ProxyError)]         // CURLE_PROXY
        [InlineData(47, HttpErrorKind.TooManyRedirects)]   // CURLE_TOO_MANY_REDIRECTS
        [InlineData(27, HttpErrorKind.OutOfMemory)]        // CURLE_OUT_OF_MEMORY
        [InlineData(2, HttpErrorKind.SetupFailed)]         // CURLE_FAILED_INIT
        [InlineData(48, HttpErrorKind.SetupFailed)]        // CURLE_UNKNOWN_OPTION
        [InlineData(42, HttpErrorKind.Unknown)]            // CURLE_ABORTED_BY_CALLBACK (我们覆盖成 Unknown)
        [InlineData(23, HttpErrorKind.Unknown)]            // CURLE_WRITE_ERROR (同上)
        [InlineData(999, HttpErrorKind.Unknown)]           // 完全未定义的值
        public void MapEasyCode_CategorisesByCurlCode(int curlCode, HttpErrorKind expected)
        {
            Assert.Equal(expected, CurlHttpException.MapEasyCode(curlCode));
        }

        [Fact]
        public void FromEasyCode_PopulatesBothKindAndCodeAndMessage()
        {
            var ex = CurlHttpException.FromEasyCode(28, "Operation timed out after 5000ms");

            Assert.Equal(HttpErrorKind.Timeout, ex.ErrorKind);
            Assert.Equal(28, ex.CurlCode);
            Assert.Contains("Timeout", ex.Message);
            Assert.Contains("28", ex.Message);
            Assert.Contains("Operation timed out", ex.Message);
        }

        [Fact]
        public void SetupFailure_ForcesSetupFailedKindRegardlessOfCode()
        {
            // SetupFailure 是 curl_multi_* 路径的专用构造(code 是 CURLMcode 而非 CURLcode),
            // 不走 MapEasyCode 查表, 直接固定为 SetupFailed。测数值 6 是为了证明
            // 即使 6 在 MapEasyCode 里是 DnsFailed, 这条路径也不会误归到 DnsFailed。
            var ex = CurlHttpException.SetupFailure(6, "curl_multi_add_handle: fake-multi-error-6");

            Assert.Equal(HttpErrorKind.SetupFailed, ex.ErrorKind);
            Assert.Equal(6, ex.CurlCode);
        }
    }
}
