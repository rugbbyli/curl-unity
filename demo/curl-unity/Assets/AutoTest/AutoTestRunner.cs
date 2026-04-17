#if CURL_UNITY_AUTOTEST
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using CurlUnity.Http;

/// <summary>
/// Automated device test runner for curl-unity.
/// Activated only when CURL_UNITY_AUTOTEST scripting define is set.
/// Auto-injects via [RuntimeInitializeOnLoadMethod] — no scene modification needed.
///
/// Log protocol:
///   [CURL_TEST] BEGIN version=1 platform=<platform> count=<N>
///   [CURL_TEST] RUN <name>
///   [CURL_TEST] PASS <name> <ms>ms
///   [CURL_TEST] FAIL <name> <ms>ms <error>
///   [CURL_TEST] SKIP <name> <reason>
///   [CURL_TEST] END passed=<P> failed=<F> skipped=<S> total_ms=<T>
/// </summary>
public class AutoTestRunner : MonoBehaviour
{
    const string TAG = "[CURL_TEST]";
    const string ResultFileName = "curl_test_results.txt";
    static string _resultPath;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        // Only activate when explicitly requested — normal app launch won't trigger tests.
        // Detection methods (in order):
        //   1. Command-line arg "-autotest" (macOS, Windows, iOS via idevicedebug)
        //   2. Marker file ".autotest" in persistentDataPath (Android via adb)
        if (!DetectAutoTestRequest()) return;

        var go = new GameObject("__AutoTestRunner__");
        DontDestroyOnLoad(go);
        go.AddComponent<AutoTestRunner>();
    }

    static bool DetectAutoTestRequest()
    {
        // 1. Command-line arg "-autotest" (macOS, Windows, Android)
        var args = System.Environment.GetCommandLineArgs();
        foreach (var arg in args)
            if (arg == "-autotest") return true;

        // 2. Environment variable CURL_UNITY_AUTOTEST=1 (iOS via idevicedebug -e)
        var env = System.Environment.GetEnvironmentVariable("CURL_UNITY_AUTOTEST");
        if (env == "1") return true;

        return false;
    }

    async void Start()
    {
        // Let engine stabilize
        await Task.Delay(1500);

        // File-based result output (for iOS/Android where log capture is unreliable)
        _resultPath = Path.Combine(Application.persistentDataPath, ResultFileName);
        try { File.Delete(_resultPath); } catch { }
        Log($"INFO results_file={_resultPath}");

        var tests = new List<(string name, Func<CurlHttpClient, Task> run)>
        {
            ("GET_Basic",        TestGetBasic),
            ("POST_Json",        TestPostJson),
            ("HTTPS_Verify",     TestHttpsVerify),
            ("HTTP2",            TestHttp2),
            ("ResponseHeaders",  TestResponseHeaders),
            ("Redirect",         TestRedirect),
            ("Timeout",          TestTimeout),
            ("Cancel",           TestCancel),
            ("LargeResponse",    TestLargeResponse),
            ("Concurrent",       TestConcurrent),
            ("ConnectionReuse",  TestConnectionReuse),
            ("DnsFailure",       TestDnsFailure),
        };

        var platform = Application.platform.ToString();
        Log($"BEGIN version=1 platform={platform} count={tests.Count}");

        int passed = 0, failed = 0, skipped = 0;
        var totalSw = Stopwatch.StartNew();

        using var client = new CurlHttpClient(enableDiagnostics: true);

        const int perTestTimeoutMs = 30000; // 单个测试用例最多 30 秒

        foreach (var (name, run) in tests)
        {
            Log($"RUN {name}");
            var sw = Stopwatch.StartNew();
            try
            {
                using var cts = new CancellationTokenSource(perTestTimeoutMs);
                var testTask = run(client);
                var completed = await Task.WhenAny(testTask, Task.Delay(perTestTimeoutMs, cts.Token));
                if (completed != testTask)
                    throw new TimeoutException($"Test timed out after {perTestTimeoutMs}ms");
                await testTask; // propagate exceptions
                cts.Cancel();   // cancel the delay task
                sw.Stop();
                Log($"PASS {name} {sw.ElapsedMilliseconds}ms");
                passed++;
            }
            catch (SkipException ex)
            {
                sw.Stop();
                Log($"SKIP {name} {Sanitize(ex.Message)}");
                skipped++;
            }
            catch (Exception ex)
            {
                sw.Stop();
                Log($"FAIL {name} {sw.ElapsedMilliseconds}ms {Sanitize(ex.Message)}");
                failed++;
            }
        }

        totalSw.Stop();

        // Emit diagnostics summary
        if (client.Diagnostics != null)
        {
            var snap = client.Diagnostics.GetSnapshot();
            Log($"DIAG total_requests={snap.TotalRequests} success={snap.SuccessRequests} " +
                $"failed={snap.FailedRequests} reuse_rate={snap.ConnectionReuseRate:F2} " +
                $"avg_ttfb_ms={snap.AvgFirstByteTimeUs / 1000.0:F1} " +
                $"avg_total_ms={snap.AvgTotalTimeUs / 1000.0:F1}");
        }

        Log($"END passed={passed} failed={failed} skipped={skipped} total_ms={totalSw.ElapsedMilliseconds}");

        // Let logs flush before quitting
        await Task.Delay(500);
        Application.Quit(failed > 0 ? 1 : 0);
    }

    // ================================================================
    // Test cases
    // ================================================================

    static async Task TestGetBasic(CurlHttpClient client)
    {
        using var resp = await client.GetAsync("https://httpbin.org/get");
        Assert(resp.HasResponse, $"No response: err={resp.ErrorCode} {resp.ErrorMessage}");
        Assert(resp.StatusCode == 200, $"Expected 200, got {resp.StatusCode}");
        Assert(resp.Body != null && resp.Body.Length > 0, "Empty body");
    }

    static async Task TestPostJson(CurlHttpClient client)
    {
        var json = "{\"test\":\"hello\",\"num\":42}";
        using var resp = await client.PostJsonAsync("https://httpbin.org/post", json);
        Assert(resp.HasResponse, $"No response: err={resp.ErrorCode} {resp.ErrorMessage}");
        Assert(resp.StatusCode == 200, $"Expected 200, got {resp.StatusCode}");
        var body = Encoding.UTF8.GetString(resp.Body);
        Assert(body.Contains("\"test\""), "Response missing posted JSON data");
        Assert(body.Contains("hello"), "Response missing posted value");
    }

    static async Task TestHttpsVerify(CurlHttpClient client)
    {
        // This is the most important platform-specific test:
        // - macOS/iOS: uses Apple SecTrust with system cert store
        // - Android: uses JNI-extracted system certs via CurlCerts
        var saved = client.VerifySSL;
        client.VerifySSL = true;
        try
        {
            using var resp = await client.GetAsync("https://www.example.com/");
            Assert(resp.HasResponse,
                $"HTTPS verification failed: err={resp.ErrorCode} {resp.ErrorMessage}");
            Assert(resp.StatusCode == 200, $"Expected 200, got {resp.StatusCode}");
        }
        finally
        {
            client.VerifySSL = saved;
        }
    }

    static async Task TestHttp2(CurlHttpClient client)
    {
        var saved = client.PreferredVersion;
        client.PreferredVersion = HttpVersion.Http2;
        try
        {
            using var resp = await client.GetAsync("https://httpbin.org/get");
            Assert(resp.HasResponse, $"No response: err={resp.ErrorCode} {resp.ErrorMessage}");
            // HTTP/2 = enum value 3
            Assert((int)resp.Version >= 3,
                $"Expected HTTP/2+, got {resp.Version} ({(int)resp.Version})");
        }
        finally
        {
            client.PreferredVersion = saved;
        }
    }

    static async Task TestResponseHeaders(CurlHttpClient client)
    {
        var request = new HttpRequest
        {
            Url = "https://httpbin.org/response-headers?X-Curl-Test=hello123",
            EnableResponseHeaders = true
        };
        using var resp = await client.SendAsync(request);
        Assert(resp.HasResponse, $"No response: err={resp.ErrorCode} {resp.ErrorMessage}");
        Assert(resp.StatusCode == 200, $"Expected 200, got {resp.StatusCode}");
        Assert(resp.Headers != null, "Headers dict is null");
        Assert(resp.Headers.ContainsKey("x-curl-test"), "Missing X-Curl-Test header");
        var values = resp.Headers["x-curl-test"];
        Assert(values.Length > 0 && values[0] == "hello123",
            $"X-Curl-Test value mismatch: {string.Join(",", values)}");
    }

    static async Task TestRedirect(CurlHttpClient client)
    {
        using var resp = await client.GetAsync("https://httpbin.org/redirect/3");
        Assert(resp.HasResponse, $"No response: err={resp.ErrorCode} {resp.ErrorMessage}");
        Assert(resp.StatusCode == 200, $"Expected 200 after redirects, got {resp.StatusCode}");
        Assert(resp.RedirectCount >= 3,
            $"Expected >= 3 redirects, got {resp.RedirectCount}");
        Assert(resp.EffectiveUrl != null && resp.EffectiveUrl.Contains("/get"),
            $"Unexpected final URL: {resp.EffectiveUrl}");
    }

    static async Task TestTimeout(CurlHttpClient client)
    {
        var request = new HttpRequest
        {
            Url = "https://httpbin.org/delay/30",
            TimeoutMs = 2000
        };
        using var resp = await client.SendAsync(request);
        Assert(!resp.HasResponse, "Expected timeout but got response");
        // CURLE_OPERATION_TIMEDOUT = 28
        Assert(resp.ErrorCode == 28,
            $"Expected CURLE_OPERATION_TIMEDOUT (28), got {resp.ErrorCode}: {resp.ErrorMessage}");
    }

    static async Task TestCancel(CurlHttpClient client)
    {
        using var cts = new CancellationTokenSource(500);
        bool cancelled = false;
        try
        {
            var request = new HttpRequest { Url = "https://httpbin.org/delay/30" };
            using var resp = await client.SendAsync(request, cts.Token);
            // Should not reach here
            Assert(false, $"Request completed instead of being cancelled, status={resp.StatusCode}");
        }
        catch (OperationCanceledException)
        {
            // TaskCanceledException inherits from OperationCanceledException,
            // so this catches both
            cancelled = true;
        }
        Assert(cancelled, "Expected cancellation exception");
    }

    static async Task TestLargeResponse(CurlHttpClient client)
    {
        const int expectedSize = 102400; // 100KB
        using var resp = await client.GetAsync($"https://httpbin.org/bytes/{expectedSize}");
        Assert(resp.HasResponse, $"No response: err={resp.ErrorCode} {resp.ErrorMessage}");
        Assert(resp.StatusCode == 200, $"Expected 200, got {resp.StatusCode}");
        Assert(resp.Body != null, "Body is null");
        // Allow some tolerance (httpbin may return slightly different sizes)
        Assert(resp.Body.Length >= expectedSize * 0.9 && resp.Body.Length <= expectedSize * 1.1,
            $"Body size mismatch: expected ~{expectedSize}, got {resp.Body.Length}");
    }

    static async Task TestConcurrent(CurlHttpClient client)
    {
        const int count = 5;
        var tasks = new Task<IHttpResponse>[count];
        for (int i = 0; i < count; i++)
            tasks[i] = client.GetAsync($"https://httpbin.org/get?idx={i}");

        var responses = await Task.WhenAll(tasks);
        int successCount = 0;
        try
        {
            foreach (var resp in responses)
            {
                if (resp.HasResponse && resp.StatusCode == 200)
                    successCount++;
            }
            Assert(successCount == count,
                $"Only {successCount}/{count} concurrent requests succeeded");
        }
        finally
        {
            foreach (var resp in responses)
                resp?.Dispose();
        }
    }

    static async Task TestConnectionReuse(CurlHttpClient client)
    {
        // Reset diagnostics to get clean stats for this test
        client.Diagnostics?.Reset();

        // 5 sequential requests to the same host should reuse connections
        for (int i = 0; i < 5; i++)
        {
            using var resp = await client.GetAsync("https://httpbin.org/get");
            Assert(resp.HasResponse, $"Request {i} failed: err={resp.ErrorCode}");
        }

        if (client.Diagnostics != null)
        {
            var snap = client.Diagnostics.GetSnapshot();
            Assert(snap.TotalRequests == 5,
                $"Expected 5 requests, got {snap.TotalRequests}");
            Assert(snap.ConnectionReuseRate > 0,
                $"No connection reuse detected (rate={snap.ConnectionReuseRate:F2})");
        }
    }

    static async Task TestDnsFailure(CurlHttpClient client)
    {
        // Use a short timeout — some networks DNS-hijack invalid domains,
        // causing long connection attempts instead of immediate DNS failure
        var request = new HttpRequest
        {
            Url = "http://this.host.does.not.exist.invalid/",
            TimeoutMs = 5000
        };
        using var resp = await client.SendAsync(request);
        Assert(!resp.HasResponse,
            $"Expected failure but got HTTP {resp.StatusCode}");
        Assert(resp.ErrorCode != 0,
            $"Expected non-zero error code, got {resp.ErrorCode}");
        // CURLE_COULDNT_RESOLVE_HOST (6) is ideal, but some networks
        // DNS-hijack and return CURLE_GOT_NOTHING (52), CURLE_OPERATION_TIMEDOUT (28), etc.
        // Any error is acceptable — the point is that the request failed gracefully.
    }

    // ================================================================
    // Helpers
    // ================================================================

    static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    static void Log(string msg)
    {
        var line = $"{TAG} {msg}";
        UnityEngine.Debug.Log(line);
        // Also append to file (survives even if log capture fails)
        try
        {
            if (_resultPath != null)
                File.AppendAllText(_resultPath, line + "\n");
        }
        catch { /* best effort */ }
    }

    static string Sanitize(string msg)
    {
        if (string.IsNullOrEmpty(msg)) return "(no message)";
        // Keep single-line for grep parsing
        return msg.Replace('\n', ' ').Replace('\r', ' ');
    }

    class SkipException : Exception
    {
        public SkipException(string reason) : base(reason) { }
    }
}
#endif
