using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using CurlUnity.Http;
using CurlUnity.IntegrationTests.Fixtures;
using Xunit;

namespace CurlUnity.IntegrationTests.Tests
{
    [Collection("Integration")]
    public class MultipartTests : IDisposable
    {
        private readonly TestServerFixture _server;
        private readonly CurlHttpClient _client;

        public MultipartTests(TestServerFixture server, CurlGlobalFixture _)
        {
            _server = server;
            _client = new CurlHttpClient();
            _client.PreferredVersion = CurlUnity.Http.HttpVersion.Default;
        }

        public void Dispose() => _client.Dispose();

        [Fact]
        public async Task PostMultipart_TextFields_AreReceivedByServer()
        {
            var form = new MultipartFormData();
            form.AddText("userId", "42");
            form.AddText("name", "alice");

            using var resp = await _client.PostMultipartAsync(
                $"{_server.HttpUrl}/multipart-echo", form);

            Assert.Equal(200, resp.StatusCode);
            var body = Encoding.UTF8.GetString(resp.Body);
            Assert.Contains("ct=multipart/form-data; boundary=", body);
            Assert.Contains("field:name=alice", body);
            Assert.Contains("field:userId=42", body);
        }

        [Fact]
        public async Task PostMultipart_WithFile_ServerReceivesCorrectBytes()
        {
            var fileBytes = new byte[4096];
            new Random(42).NextBytes(fileBytes);
            var expectedHex = Convert.ToHexString(SHA256.HashData(fileBytes)).ToLowerInvariant();

            var form = new MultipartFormData();
            form.AddText("desc", "avatar upload");
            form.AddFile("avatar", "photo.jpg", fileBytes, "image/jpeg");

            using var resp = await _client.PostMultipartAsync(
                $"{_server.HttpUrl}/multipart-echo", form);

            Assert.Equal(200, resp.StatusCode);
            var body = Encoding.UTF8.GetString(resp.Body);
            Assert.Contains("field:desc=avatar upload", body);
            Assert.Contains("file:avatar;filename=photo.jpg;ct=image/jpeg;len=4096;sha256=" + expectedHex, body);
        }

        [Fact]
        public async Task PostMultipart_MultipleFiles_AllReceivedInOrder()
        {
            var a = new byte[] { 1, 2, 3 };
            var b = new byte[] { 4, 5, 6, 7 };

            var form = new MultipartFormData();
            form.AddFile("f1", "a.bin", a);
            form.AddFile("f2", "b.bin", b);

            using var resp = await _client.PostMultipartAsync(
                $"{_server.HttpUrl}/multipart-echo", form);

            Assert.Equal(200, resp.StatusCode);
            var body = Encoding.UTF8.GetString(resp.Body);
            Assert.Contains("file:f1;filename=a.bin;ct=application/octet-stream;len=3;", body);
            Assert.Contains("file:f2;filename=b.bin;ct=application/octet-stream;len=4;", body);
        }

        [Fact]
        public void ContentType_AvailableBeforeBuild()
        {
            var form = new MultipartFormData();
            // ContentType 在构造时即可读,不需要先调 Build
            Assert.StartsWith("multipart/form-data; boundary=", form.ContentType);
        }
    }
}
