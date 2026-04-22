// Unity 专有 attribute 的编译 stub, 只给 DocFX Roslyn 分析用。
// 真实编译时 Unity 自己的 AOT.MonoPInvokeCallbackAttribute 会覆盖掉。
// 实际发布的 Package 不含此文件, filterConfig.yml 保证这些 stub 类型不会
// 出现在生成的 API 文档里。

namespace AOT
{
    [System.AttributeUsage(System.AttributeTargets.Method)]
    internal sealed class MonoPInvokeCallbackAttribute : System.Attribute
    {
        public MonoPInvokeCallbackAttribute(System.Type type) { }
    }
}
