using System;
using CurlUnity.Core;
using CurlUnity.Native;
using CurlUnity.UnitTests.TestSupport;
using Xunit;

namespace CurlUnity.UnitTests.Tests
{
    /// <summary>
    /// 验证 CurlGlobal 的引用计数 invariant: init 失败 **不能** 增加 refcount,
    /// 否则后续 Release 会对"还没 init 成功的库"调 curl_global_cleanup, 未定义行为。
    ///
    /// 注: CurlGlobal 是 static, refcount 全进程共享。和 ReproTests (也会 Acquire/Release)
    /// 共用 [Collection("CurlGlobal")] 串行执行, 避免并发污染 refcount 状态。
    /// 每个 test 结束前必须对称 Release, 留给后续 test 一个干净的 refcount=0。
    /// </summary>
    [Collection("CurlGlobal")]
    public class CurlGlobalTests
    {
        [Fact]
        public void Acquire_WhenInitFails_DoesNotIncrementRefcount()
        {
            // 策略: 先失败一次, 再成功一次。如果失败路径错误地 +refcount, 第二次
            // Acquire 时发现 _refCount > 0 就跳过 init; 失败前和失败后各要求 init
            // 被调一次, 总共 2 次 → 证明失败确实没污染 refcount。
            var api = new FakeCurlApi { CurlGlobalInitResult = 1 };

            Assert.Throws<InvalidOperationException>(() => CurlGlobal.Acquire(api));
            Assert.Equal(1, api.CurlGlobalInitCalls);

            api.CurlGlobalInitResult = CurlNative.CURLE_OK;
            CurlGlobal.Acquire(api);
            try
            {
                Assert.Equal(2, api.CurlGlobalInitCalls);
            }
            finally
            {
                CurlGlobal.Release(api);
            }

            // 对称 release 之后, refcount 应该归 0, 且本次成功的那一次 init 对应了一次 cleanup
            Assert.Equal(1, api.CurlGlobalCleanupCalls);
        }

        [Fact]
        public void Release_OverRelease_IsNoopAndDoesNotTriggerCleanup()
        {
            // 一次过度 release (refcount 已经是 0 时再调), 必须静默忽略,
            // 不能让 refcount 跑到负数、也不能错误地触发 cleanup。
            var api = new FakeCurlApi();

            // 确保起点 refcount=0 (Release on 0 是 noop), 且 cleanupCalls 基线固定。
            CurlGlobal.Release(api);
            int cleanupBaseline = api.CurlGlobalCleanupCalls;

            CurlGlobal.Release(api);  // over-release #1
            CurlGlobal.Release(api);  // over-release #2

            Assert.Equal(cleanupBaseline, api.CurlGlobalCleanupCalls);

            // 后续一次正常 Acquire/Release 仍能正确工作, 证明 refcount 没被跑成负数
            int cleanupBefore = api.CurlGlobalCleanupCalls;
            CurlGlobal.Acquire(api);
            CurlGlobal.Release(api);
            Assert.Equal(cleanupBefore + 1, api.CurlGlobalCleanupCalls);
        }

        [Fact]
        public void Acquire_MultipleHolders_InitOnceAndCleanupOnlyAfterLastRelease()
        {
            // Refcount 语义: init 在 refcount 0→1 时调一次, cleanup 在 refcount 1→0 时调一次,
            // 中间多次 Acquire/Release 不应重复 init/cleanup。
            var api = new FakeCurlApi();
            int initBaseline = api.CurlGlobalInitCalls;
            int cleanupBaseline = api.CurlGlobalCleanupCalls;

            CurlGlobal.Acquire(api);
            CurlGlobal.Acquire(api);
            CurlGlobal.Acquire(api);

            Assert.Equal(initBaseline + 1, api.CurlGlobalInitCalls);
            Assert.Equal(cleanupBaseline, api.CurlGlobalCleanupCalls);

            CurlGlobal.Release(api);
            CurlGlobal.Release(api);
            Assert.Equal(cleanupBaseline, api.CurlGlobalCleanupCalls);  // 还没到最后一个

            CurlGlobal.Release(api);
            Assert.Equal(cleanupBaseline + 1, api.CurlGlobalCleanupCalls);  // 最后一个 → cleanup
        }
    }
}
