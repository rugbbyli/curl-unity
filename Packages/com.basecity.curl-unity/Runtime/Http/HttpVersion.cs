namespace CurlUnity.Http
{
    /// <summary>
    /// HTTP 协议版本。数值与 curl 的 CURL_HTTP_VERSION_* 定义一致。
    /// 既用于设置偏好，也用于响应中报告实际版本。
    /// </summary>
    public enum HttpVersion
    {
        Default = 0,
        Http10 = 1,
        Http11 = 2,
        Http2 = 3,
        Http3 = 30,
        PreferH3 = Http3,  // 请求偏好别名，与 Http3 同值
        Http3Only = 31,
    }
}
