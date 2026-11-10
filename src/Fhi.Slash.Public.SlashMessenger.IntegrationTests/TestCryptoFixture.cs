using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;

namespace Fhi.Slash.Public.SlashMessenger.IntegrationTests;

public sealed class TestCryptoFixture : IDisposable
{
    public TestCryptoFixture()
    {
        ClientCert1 = CreateCertificate("test-client-1");
        ClientCert2 = CreateCertificate("test-client-2");
    }

    public X509Certificate2 ClientCert1 { get; }
    public X509Certificate2 ClientCert2 { get; }
    public RSA SlashRsa { get; } = RSA.Create(2048);
    public RSA HelseIdSigningRsa { get; } = RSA.Create(2048);
    public string SlashPublicPem => SlashRsa.ExportSubjectPublicKeyInfoPem();
    public string SlashPrivatePem => SlashRsa.ExportPkcs8PrivateKeyPem();
    public string HelseIdJwks => CreateJwks(HelseIdSigningRsa, "helseid-test-key");
    public string HelseIdAccessToken => CreateAccessToken(HelseIdSigningRsa);

    public static string CreatePrivateJwkJson()
    {
        using var rsa = RSA.Create(2048);
        var parameters = rsa.ExportParameters(true);

        var jwk = new JsonWebKey
        {
            Kty = "RSA",
            Use = "sig",
            Alg = SecurityAlgorithms.RsaSsaPssSha512,
            N = Base64UrlEncoder.Encode(parameters.Modulus),
            E = Base64UrlEncoder.Encode(parameters.Exponent),
            D = Base64UrlEncoder.Encode(parameters.D),
            P = Base64UrlEncoder.Encode(parameters.P),
            Q = Base64UrlEncoder.Encode(parameters.Q),
            DP = Base64UrlEncoder.Encode(parameters.DP),
            DQ = Base64UrlEncoder.Encode(parameters.DQ),
            QI = Base64UrlEncoder.Encode(parameters.InverseQ)
        };

        return JsonSerializer.Serialize(jwk);
    }

    private static X509Certificate2 CreateCertificate(string subject)
    {
        using var rsa = RSA.Create(2048);

        var request = new CertificateRequest($"CN={subject}", rsa, HashAlgorithmName.SHA512, RSASignaturePadding.Pss);

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

    private static string CreateJwks(RSA rsa, string keyId)
    {
        var parameters = rsa.ExportParameters(false);

        static string Encode(byte[] value) => Base64UrlEncoder.Encode(value);

        return JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new JsonWebKey
                {
                    Kty = "RSA",
                    Use = "sig",
                    Alg = SecurityAlgorithms.RsaSsaPssSha512,
                    Kid = keyId,
                    N = Encode(parameters.Modulus!),
                    E = Encode(parameters.Exponent!)
                }
            }
        });
    }

    private static string CreateAccessToken(RSA rsa)
    {
        var key = new RsaSecurityKey(rsa)
        {
            KeyId = "helseid-test-key"
        };

        var credentials = new SigningCredentials(key, SecurityAlgorithms.RsaSsaPssSha512);

        var token = new JwtSecurityToken(
            issuer: "test-issuer",
            audience: "test-audience",
            claims:
            [
                new System.Security.Claims.Claim("scope", "fhi:slash.mottak/all")
            ],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public void Dispose()
    {
        ClientCert1.Dispose();
        ClientCert2.Dispose();
        SlashRsa.Dispose();
        HelseIdSigningRsa.Dispose();
    }
}
