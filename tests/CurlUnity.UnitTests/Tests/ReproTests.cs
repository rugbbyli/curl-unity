using System;
using System.Threading;
using System.Threading.Tasks;
using CurlUnity.Core;
using CurlUnity.Http;
using CurlUnity.UnitTests.TestSupport;
using Xunit;

namespace CurlUnity.UnitTests.Tests
{
    public class ReproTests
    {
        [SkippableFact]
        [Trait("Category", "Repro")]
        public async Task Constructor_ShouldFail_WhenGlobalInitFails()
        {
            ReproGate.RequireEnabled();

            var api = new FakeCurlApi
            {
                CurlGlobalInitResult = 7
            };

            CurlHttpClient client = null;
            try
            {
                var ex = Record.Exception(() => client = new CurlHttpClient(api));
                Assert.IsType<InvalidOperationException>(ex);
            }
            finally
            {
                client?.Dispose();
                await Task.CompletedTask;
            }
        }

        [SkippableFact]
        [Trait("Category", "Repro")]
        public async Task SendAsync_ShouldFailFast_WhenMultiAddHandleFails()
        {
            ReproGate.RequireEnabled();

            var api = new FakeCurlApi
            {
                MultiAddHandleResult = 9
            };

            using var client = new CurlHttpClient(api);

            var task = client.GetAsync("http://example.invalid/");
            try
            {
                var completed = await Task.WhenAny(task, Task.Delay(300));
                Assert.Same(task, completed);
                await Assert.ThrowsAsync<InvalidOperationException>(() => task);
            }
            finally
            {
                client.Dispose();
                try { await task; } catch { }
            }
        }

        [SkippableFact]
        [Trait("Category", "Repro")]
        public async Task BackgroundWorker_Dispose_ShouldWaitForBlockedPollThread()
        {
            ReproGate.RequireEnabled();

            var api = new FakeCurlApi();
            using var pollEntered = new ManualResetEventSlim(false);
            using var releasePoll = new ManualResetEventSlim(false);
            api.OnMultiPoll = _ =>
            {
                pollEntered.Set();
                releasePoll.Wait();
            };

            var worker = new CurlBackgroundWorker(api)
            {
                PollTimeoutMs = 10
            };

            Task disposeTask = null;
            try
            {
                worker.Start();
                Assert.True(pollEntered.Wait(TimeSpan.FromSeconds(1)), "Worker did not enter fake poll.");

                disposeTask = Task.Run(() => worker.Dispose());
                await Task.Delay(TimeSpan.FromMilliseconds(3200));

                Assert.False(api.MultiCleanupCalledWhilePollInProgress);
            }
            finally
            {
                releasePoll.Set();
                if (disposeTask != null)
                    await disposeTask.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }

        [SkippableFact]
        [Trait("Category", "Repro")]
        public async Task BackgroundWorker_Dispose_ShouldNotCleanupMulti_WhileDataCallbackIsStillRunning()
        {
            ReproGate.RequireEnabled();

            var api = new FakeCurlApi();
            using var callbackEntered = new ManualResetEventSlim(false);
            using var releaseCallback = new ManualResetEventSlim(false);
            var callbackInvoked = 0;

            api.OnMultiPerform = multi =>
            {
                var handle = api.GetFirstActiveHandle(multi);
                if (handle == IntPtr.Zero)
                    return;

                if (Interlocked.Exchange(ref callbackInvoked, 1) != 0)
                    return;

                api.InvokeWriteCallback(handle, new byte[] { 1, 2, 3 });
            };

            var worker = new CurlBackgroundWorker(api)
            {
                PollTimeoutMs = 10
            };
            var request = new CurlRequest(api)
            {
                DataCallback = (_, _, _) =>
                {
                    callbackEntered.Set();
                    releaseCallback.Wait();
                }
            };

            worker.Start();
            worker.Send(request);

            Task disposeTask = null;
            try
            {
                Assert.True(callbackEntered.Wait(TimeSpan.FromSeconds(1)), "Streaming callback was not invoked.");

                disposeTask = Task.Run(() => worker.Dispose());
                await Task.Delay(TimeSpan.FromMilliseconds(3200));

                Assert.False(api.MultiCleanupCalledWhileCallbackInProgress);
            }
            finally
            {
                releaseCallback.Set();
                if (disposeTask != null)
                    await disposeTask.WaitAsync(TimeSpan.FromSeconds(10));
                request.Dispose();
            }
        }
    }
}
