using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace Fhi.Slash.Public.SlashMessenger.UnitTests;

public sealed class TestCryptoFixture : IDisposable
{
    private readonly RSA _slashRsa = RSA.Create(2048);
    private readonly RSA _dpopRsa = RSA.Create(2048);

    public TestCryptoFixture()
    {
        TestKeys = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            publicKey = _slashRsa.ExportSubjectPublicKeyInfoPem()
        }));

        Certificate = CreateCertificate();
        DPoPProofJwk = CreatePrivateJwk(_dpopRsa);
    }

    public string AccessToken { get; } = "test-access-token";
    public string TestMessage { get; } = "[]";
    public JsonDocument TestKeys { get; }
    public X509Certificate2 Certificate { get; }
    public JsonWebKey DPoPProofJwk { get; }

    private static X509Certificate2 CreateCertificate()
    {
        using var rsa = RSA.Create(2048);

        var request = new CertificateRequest("CN=Test Client", rsa, HashAlgorithmName.SHA512, RSASignaturePadding.Pss);

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature |
                X509KeyUsageFlags.KeyEncipherment,
                critical: true));

        using var generated = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddDays(1));

        return X509CertificateLoader.LoadPkcs12(
            generated.Export(X509ContentType.Pfx),
            password: null,
            keyStorageFlags: X509KeyStorageFlags.Exportable);
    }

    private static JsonWebKey CreatePrivateJwk(RSA rsa)
    {
        var p = rsa.ExportParameters(true);

        return new JsonWebKey
        {
            Kty = "RSA",
            Use = "sig",
            Alg = SecurityAlgorithms.RsaSsaPssSha512,
            N = Base64UrlEncoder.Encode(p.Modulus!),
            E = Base64UrlEncoder.Encode(p.Exponent!),
            D = Base64UrlEncoder.Encode(p.D!),
            P = Base64UrlEncoder.Encode(p.P!),
            Q = Base64UrlEncoder.Encode(p.Q!),
            DP = Base64UrlEncoder.Encode(p.DP!),
            DQ = Base64UrlEncoder.Encode(p.DQ!),
            QI = Base64UrlEncoder.Encode(p.InverseQ!)
        };
    }

    public void Dispose()
    {
        TestKeys.Dispose();
        Certificate.Dispose();
        _slashRsa.Dispose();
        _dpopRsa.Dispose();
    }
}
