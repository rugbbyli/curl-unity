using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace CurlUnity.IntegrationTests.TestServer
{
    internal static class CertificateHelper
    {
        public static X509Certificate2 CreateSelfSigned()
        {
            using var rsa = RSA.Create(2048);
            var req = new CertificateRequest(
                "CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            var sanBuilder = new SubjectAlternativeNameBuilder();
            sanBuilder.AddDnsName("localhost");
            sanBuilder.AddIpAddress(IPAddress.Loopback);
            req.CertificateExtensions.Add(sanBuilder.Build());

            var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1));

            // Export and re-import to ensure private key is accessible
            return X509CertificateLoader.LoadPkcs12(
                cert.Export(X509ContentType.Pfx, "test"), "test",
                X509KeyStorageFlags.Exportable);
        }
    }
}
