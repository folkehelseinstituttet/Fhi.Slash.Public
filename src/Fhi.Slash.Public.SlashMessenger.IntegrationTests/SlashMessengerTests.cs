using IdentityModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json.Linq;
using Fhi.Slash.Public.SlashMessenger.Extensions;
using Fhi.Slash.Public.SlashMessenger.HelseId.Models;
using Fhi.Slash.Public.SlashMessengerCLI;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Aes = System.Security.Cryptography.Aes;
using Fhi.Slash.Public.SlashMessenger.Slash.Models;

namespace Fhi.Slash.Public.SlashMessenger.IntegrationTests;

[TestClass]
public sealed class SlashMessengerTests
{
    // Client files
    private const string ClientCert1FilePath = "TestFiles/Client/test_cert_without_password.pfx";
    private const string ClientCert2FilePath = "TestFiles/Client/test_cert_without_password_2.pfx";
    private const string ClientHelseIdClientDefinitionFilePath = "TestFiles/Client/helseid-client-definition.json";
    private const string ClientTestMessage1FilePath = "TestFiles/Client/hst_avtale_test_message.json";
    private const string ClientTestMessage1Type = "hst_avtale";
    private const string ClientTestMessage1Version = "1";

    // Slash files & Endpoints
    private const string SlashPublicPemFilePath = "TestFiles/Slash/pub-key.pem";
    private const string SlashPrivatePemFilePath = "TestFiles/Slash/priv-key.pem";
    private const string SlashKeysEndpoint = "/slash/keys";
    private const string SlashMessageEndpoint = "/slash/message";

    // HelseId files & Endpoints
    private const string HelseIdAccessTokenFilePath = "TestFiles/HelseId/access-token.txt";
    private const string HelseIdPublicKeySetFilePath = "TestFiles/HelseId/pub-key-set.json";
    private const string HelseIdJWKsEndpoint = "/helseId/.well-known/openid-configuration/jwks";
    private const string HelseIdTokenEndpoint = "/helseId/connect/token";

    private readonly WireMockServer _mockServer = WireMockServer.Start();
    private readonly Guid SlashKeyPairId = Guid.NewGuid();

    [TestMethod]
    public async Task ClientDefinitionShouldWork()
    {
        // Arrange
        SetupMockServer();
        var helseIdClientConfig = JsonSerializer.Deserialize<HelseIdClientDefinition>(File.ReadAllText(ClientHelseIdClientDefinitionFilePath));
        var host = SetupHost(_mockServer.Url!, helseIdClientDefinition: helseIdClientConfig);

        // Act
        await Program.Execute(host, ClientTestMessage1FilePath, ClientTestMessage1Type, ClientTestMessage1Version);

        // Assert
        var slashResponse = GetResponseLog(_mockServer, SlashMessageEndpoint);
        Assert.AreEqual(200, slashResponse!.StatusCode);
    }

    [TestMethod]
    public async Task SlashRequestIsValid_AccessToken()
    {
        // Arrange
        SetupMockServer();
        var host = SetupHost(_mockServer.Url!);

        // Act
        await Program.Execute(host, ClientTestMessage1FilePath, ClientTestMessage1Type, ClientTestMessage1Version);

        // Assert
        var slashRequest = GetRequestLog(_mockServer, SlashMessageEndpoint);
        var accessToken = slashRequest!.Headers!["Authorization"].First();
        Assert.IsTrue(accessToken.StartsWith("DPoP"));
    }

    [TestMethod]
    public async Task SlashRequestIsValid_DPoPProof()
    {
        // Arrange
        SetupMockServer();
        var host = SetupHost(_mockServer.Url!);

        // Act
        await Program.Execute(host, ClientTestMessage1FilePath, ClientTestMessage1Type, ClientTestMessage1Version);

        // Assert
        var slashRequest = GetRequestLog(_mockServer, SlashMessageEndpoint);
        var dPoPProof = slashRequest!.Headers!["DPoP"].First();

        var (_, rawDPoPProofPayload, _) = SplitJwt(dPoPProof);
        var dPoPPayload = JsonDocument.Parse(Base64UrlDecode(rawDPoPProofPayload)).RootElement;

        Assert.AreEqual(ClientTestMessage1Type, dPoPPayload.GetProperty("msg_type").GetString());
        Assert.AreEqual(ClientTestMessage1Version, dPoPPayload.GetProperty("msg_version").GetString());
        Assert.AreEqual(SlashKeyPairId.ToString(), dPoPPayload.GetProperty("enc_key_id").GetString());
        Assert.IsNotNull(dPoPPayload.GetProperty("msg_hash"));
        Assert.IsNotNull(dPoPPayload.GetProperty("enc_sym_key"));
    }

    [TestMethod]
    public async Task SlashRequestIsValid_Payload()
    {
        // Arrange
        SetupMockServer();
        var host = SetupHost(_mockServer.Url!);

        // Act
        await Program.Execute(host, ClientTestMessage1FilePath, ClientTestMessage1Type, ClientTestMessage1Version);

        // Assert
        var slashRequest = GetRequestLog(_mockServer, SlashMessageEndpoint);
        var dPoPProof = slashRequest!.Headers!["DPoP"].First();
        var rawBody = slashRequest!.Body!;
        var (_, rawDPoPProofPayload, _) = SplitJwt(dPoPProof);
        var dPoPPayload = JsonDocument.Parse(Base64UrlDecode(rawDPoPProofPayload)).RootElement;
        var dPoPMsgHash = dPoPPayload.GetProperty("msg_hash").GetString();
        var encSymKey = dPoPPayload.GetProperty("enc_sym_key").GetString();
        var privateKey = ConvertPemToPrivateJwk(File.ReadAllText(SlashPrivatePemFilePath));

        var encSymKeyBytes = Base64Url.Decode(encSymKey!);
        var symKey = CreateRsaFromJwk(privateKey).Decrypt(encSymKeyBytes, RSAEncryptionPadding.OaepSHA256);

        var msgBytes = DecryptWithSymmetricKey(Base64Url.Decode(rawBody), symKey);
        var msg = Encoding.UTF8.GetString(msgBytes);
        var msgHash = Base64Url.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(msg)));

        Assert.AreEqual(dPoPMsgHash, msgHash);
        Assert.AreEqual(File.ReadAllText(ClientTestMessage1FilePath), msg);
    }

    [TestMethod]
    public async Task SlashRequestIsValid_CustomHeaderValues()
    {
        // Arrange
        SetupMockServer();
        var host = SetupHost(_mockServer.Url!);

        // Act
        await Program.Execute(host, ClientTestMessage1FilePath, ClientTestMessage1Type, ClientTestMessage1Version);

        // Assert
        var slashRequest = GetRequestLog(_mockServer, SlashMessageEndpoint)!;
        Assert.IsNotNull(slashRequest.Headers);
        Assert.IsTrue(slashRequest.Headers.Any(h => h.Key.Equals("x-vendor-name")));
        Assert.IsTrue(slashRequest.Headers.Any(h => h.Key.Equals("x-software-name")));
        Assert.IsTrue(slashRequest.Headers.Any(h => h.Key.Equals("x-software-version")));
        Assert.IsTrue(slashRequest.Headers.Any(h => h.Key.Equals("x-export-software-version")));
        Assert.IsTrue(slashRequest.Headers.Any(h => h.Key.Equals("x-data-extraction-date")));
    }

    [TestMethod]
    public async Task ShouldAllowUsingAnotherJWKForDPoPProof()
    {
        // Arrange
        SetupMockServer();
        var dpopCert = LoadCertificate(ClientCert2FilePath);
        var dPoPJwk = JsonWebKeyConverter.ConvertFromX509SecurityKey(new X509SecurityKey(dpopCert), true);
        var host = SetupHost(_mockServer.Url!, services =>
        {
            services.AddKeyedSingleton(ServiceCollectionExtensions.dPoPProofJwkKey, dPoPJwk);
        });

        // Act
        await Program.Execute(host, ClientTestMessage1FilePath, ClientTestMessage1Type, ClientTestMessage1Version);

        // Assert
        var helseIdRequest = GetRequestLog(_mockServer, HelseIdTokenEndpoint);
        var rawDPoPProof = helseIdRequest!.Headers!["DPoP"].First();
        var (header, payload, signature) = SplitJwt(rawDPoPProof);

        AssertJwtSignature(dpopCert, header, payload, signature, shouldBeValid: true);
        AssertJwtSignature(LoadCertificate(ClientCert1FilePath), header, payload, signature, shouldBeValid: false);

        var parameters = System.Web.HttpUtility.ParseQueryString(helseIdRequest!.Body!);
        var (accessTokenHeader, accessTokenPayload, accessTokenSignature) = SplitJwt(parameters["client_assertion"]!);
        AssertJwtSignature(LoadCertificate(ClientCert1FilePath), accessTokenHeader, accessTokenPayload, accessTokenSignature, shouldBeValid: true);
    }

    [TestMethod]
    public async Task CheckIfTokenRequestToHelseIdIsCorrect_Body()
    {
        // Arrange
        SetupMockServer();
        var host = SetupHost(_mockServer.Url!);

        // Act
        await Program.Execute(host, ClientTestMessage1FilePath, ClientTestMessage1Type, ClientTestMessage1Version);

        // Assert
        var helseIdRequest = GetRequestLog(_mockServer, HelseIdTokenEndpoint);
        var parameters = ParseQueryParameters(helseIdRequest!.Body!);

        Assert.AreEqual("client_credentials", parameters["grant_type"]);
        Assert.AreEqual("just-a-test-client", parameters["client_id"]);
        Assert.AreEqual("urn:ietf:params:oauth:client-assertion-type:jwt-bearer", parameters["client_assertion_type"]);

        var (accessTokenHeader, accessTokenPayload, accessTokenSignature) = SplitJwt(parameters["client_assertion"]!);
        AssertJwtSignature(LoadCertificate(ClientCert1FilePath), accessTokenHeader, accessTokenPayload, accessTokenSignature, shouldBeValid: true);
    }

    [TestMethod]
    public async Task CheckIfTokenRequestToHelseIdIsCorrect_DPoPProof()
    {
        // Arrange
        SetupMockServer();
        var host = SetupHost(_mockServer.Url!);

        // Act
        await Program.Execute(host, ClientTestMessage1FilePath, ClientTestMessage1Type, ClientTestMessage1Version);

        // Assert
        var helseIdRequest = GetRequestLog(_mockServer, HelseIdTokenEndpoint);
        var rawDPoPProof = helseIdRequest!.Headers!["DPoP"].First();
        var (header, payload, signature) = SplitJwt(rawDPoPProof);

        AssertJwtHeader(header, "dpop+jwt", "RS256");
        AssertJwtPayload(payload, "POST", $"{_mockServer.Url!}{HelseIdTokenEndpoint}");

        var dpopCert = LoadCertificate(ClientCert1FilePath);
        AssertJwtSignature(dpopCert, header, payload, signature, shouldBeValid: true);
    }

    [TestMethod]
    public async Task ShouldBeAllowedToReplaceDefaultHttpClients()
    {
        // Arrange
        const string testHeader = "test-header";
        var defaultSlashConfig = new SlashConfig();

        SetupMockServer();

        var host = SetupHost(_mockServer.Url!, preCustomConfig: services =>
        {
            services.AddHttpClient(defaultSlashConfig.BasicClientName, config => {
                config.BaseAddress = new Uri(new Uri(_mockServer.Url!), "slash/");
                config.DefaultRequestHeaders.Add(testHeader, "test");
             });
        });

        // Act
        await Program.Execute(host, ClientTestMessage1FilePath, ClientTestMessage1Type, ClientTestMessage1Version);

        // Assert
        var getKeysRequest = GetRequestLog(_mockServer, SlashKeysEndpoint);
        Assert.IsNotNull(getKeysRequest);
        Assert.IsNotNull(getKeysRequest.Headers);
        Assert.IsTrue(getKeysRequest.Headers.Any(h => h.Key.Equals(testHeader)));
    }

    private void SetupMockServer()
    {
        // HelseId JWK
        _mockServer
          .Given(Request.Create().WithPath(HelseIdJWKsEndpoint).UsingGet())
          .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.OK).WithBodyAsJson(JObject.Parse(File.ReadAllText(HelseIdPublicKeySetFilePath))));

        // HelseId Token
        const string nonce = "just-a-nonce";
        _mockServer
          .Given(Request.Create().WithPath(HelseIdTokenEndpoint).UsingPost()
            .WithHeader((x) =>
            {
                x.TryGetValue("DPoP", out string[]? dPoPValues);
                var rawDPoP = dPoPValues?.FirstOrDefault();
                if (string.IsNullOrEmpty(rawDPoP))
                {
                    return true;
                }

                var dPoPToken = new JwtSecurityTokenHandler().ReadJwtToken(rawDPoP);

                return !dPoPToken.Payload.ContainsKey("nonce");
            }))
          .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.BadRequest)
          .WithHeader("DPoP-Nonce", nonce)
          .WithBodyAsJson(new
          {
              error = "use_dpop_nonce",
              error_description = "Missing \u0027nonce\u0027 value."
          }));

        _mockServer
          .Given(Request.Create().WithPath(HelseIdTokenEndpoint).UsingPost().WithHeader((x) =>
          {
              x.TryGetValue("DPoP", out string[]? dPoPValues);
              var rawDPoP = dPoPValues?.FirstOrDefault();
              if (string.IsNullOrEmpty(rawDPoP))
              {
                  return true;
              }

              var dPoPToken = new JwtSecurityTokenHandler().ReadJwtToken(rawDPoP);

              return dPoPToken.Payload.ContainsKey("nonce") && dPoPToken.Payload["nonce"].Equals(nonce);
          }))
          .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.OK).WithBodyAsJson(new
          {
              access_token = File.ReadAllText(HelseIdAccessTokenFilePath),
              expires_in = 60,
              token_type = "DPoP",
              scope = "fhi:slash.mottak/all"
          }));

        // Slash Public Keys
        _mockServer
           .Given(Request.Create().WithPath(SlashKeysEndpoint).UsingGet())
           .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.OK).WithBodyAsJson(JArray.FromObject(new[]{new
           {
               id = SlashKeyPairId,
               publicKey = File.ReadAllText(SlashPublicPemFilePath),
               expirationDate = DateTime.Now.AddDays(1),
               isExpired = false
           }})));

        // Slash Message
        _mockServer
            .Given(Request.Create().WithPath(SlashMessageEndpoint).UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("X-Correlation-ID", Guid.NewGuid().ToString())
                .WithBodyAsJson(new
                   {
                       delivered = true,
                       errors = Array.Empty<object>()
                   }));
    }

    private static IHost SetupHost(string baseUrl, Action<IServiceCollection>? preCustomConfig = null, HelseIdClientDefinition? helseIdClientDefinition = null) =>
        Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                preCustomConfig?.Invoke(services);
                services.AddSlash(slashConfig =>
                {
                    slashConfig.BaseUrl = $"{baseUrl}/slash";
                    slashConfig.ExportSoftwareVersion = "exportSoftware-integrationTest";
                    slashConfig.SoftwareName = "softwareName-integrationTest";
                    slashConfig.SoftwareVersion = "softwareVersion-integrationTest";
                    slashConfig.VendorName = "vendoreName-integrationTest";
                }, helseIdConfig =>
                {
                    helseIdConfig.TokenEndpoint = $"{baseUrl}/helseId/connect/token";
                    helseIdConfig.ClientId = "just-a-test-client";
                    helseIdConfig.Certificate = helseIdClientDefinition == null ? new X509Certificate2(ClientCert1FilePath, string.Empty, X509KeyStorageFlags.Exportable) : null;
                    helseIdConfig.ClientDefinition = helseIdClientDefinition;
                });
            })
            .Build();

    private static X509Certificate2 LoadCertificate(string path) =>
        new(path, string.Empty, X509KeyStorageFlags.Exportable);

    private static (string Header, string Payload, string Signature) SplitJwt(string jwt)
    {
        var parts = jwt.Split('.');
        Assert.AreEqual(3, parts.Length);
        return (parts[0], parts[1], parts[2]);
    }

    private static IRequestMessage? GetRequestLog(WireMockServer mockServer, string requestPath, string? method = null) => mockServer.LogEntries
            .FirstOrDefault(log =>
                log.RequestMessage.AbsolutePath.Equals(requestPath, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrEmpty(method) || log.RequestMessage.Method.Equals(method, StringComparison.OrdinalIgnoreCase)))?.RequestMessage;

    private static IResponseMessage? GetResponseLog(WireMockServer wireMockServer, string requestPath, string? method = null) => wireMockServer.LogEntries
            .FirstOrDefault(log =>
                log.RequestMessage.AbsolutePath.Equals(requestPath, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrEmpty(method) || log.RequestMessage.Method.Equals(method, StringComparison.OrdinalIgnoreCase)))?.ResponseMessage;

    private static void AssertJwtSignature(X509Certificate2 cert, string header, string payload, string signature, bool shouldBeValid)
    {
        using var rsa = cert.GetRSAPublicKey();
        var isValid = rsa!.VerifyData(
            Encoding.UTF8.GetBytes($"{header}.{payload}"),
            Base64UrlDecodeBytes(signature),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        Assert.AreEqual(shouldBeValid, isValid);
    }

    private static void AssertJwtHeader(string encodedHeader, string expectedType, string expectedAlgorithm)
    {
        var header = JsonDocument.Parse(Base64UrlDecode(encodedHeader)).RootElement;
        Assert.AreEqual(expectedType, header.GetProperty("typ").GetString());
        Assert.AreEqual(expectedAlgorithm, header.GetProperty("alg").GetString());
    }

    private static void AssertJwtPayload(string encodedPayload, string expectedMethod, string expectedUri)
    {
        var payload = JsonDocument.Parse(Base64UrlDecode(encodedPayload)).RootElement;
        Assert.AreEqual(expectedMethod, payload.GetProperty("htm").GetString());
        Assert.IsTrue(payload.GetProperty("htu").GetString()?.EndsWith(expectedUri));
    }

    public static JsonWebKey ConvertPemToPrivateJwk(string pem)
    {
        // Remove PEM headers and footers
        var base64 = pem
            .Replace("-----BEGIN PRIVATE KEY-----", "")
            .Replace("-----END PRIVATE KEY-----", "")
            .Replace("\n", "")
            .Replace("\r", "");

        // Decode the Base64-encoded key
        var privateKeyBytes = Convert.FromBase64String(base64);

        // Parse the key
        using var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(privateKeyBytes, out _);

        // Extract RSA parameters
        var parameters = rsa.ExportParameters(true);

        // Create JWK from RSA parameters
        var jwk = new JsonWebKey
        {
            Kty = "RSA",
            N = Base64UrlEncoder.Encode(parameters.Modulus),
            E = Base64UrlEncoder.Encode(parameters.Exponent),
            D = Base64UrlEncoder.Encode(parameters.D),
            P = Base64UrlEncoder.Encode(parameters.P),
            Q = Base64UrlEncoder.Encode(parameters.Q),
            DP = Base64UrlEncoder.Encode(parameters.DP),
            DQ = Base64UrlEncoder.Encode(parameters.DQ),
            QI = Base64UrlEncoder.Encode(parameters.InverseQ),
            Use = "sig",
            Alg = "RS256"
        };

        return jwk;
    }

    public static RSA CreateRsaFromJwk(JsonWebKey jwk)
    {
        // Create RSA parameters from JWK
        RSAParameters rsaParams = new()
        {
            Modulus = Base64UrlEncoder.DecodeBytes(jwk.N),
            Exponent = Base64UrlEncoder.DecodeBytes(jwk.E),
            D = Base64UrlEncoder.DecodeBytes(jwk.D),
            P = Base64UrlEncoder.DecodeBytes(jwk.P),
            Q = Base64UrlEncoder.DecodeBytes(jwk.Q),
            DP = Base64UrlEncoder.DecodeBytes(jwk.DP),
            DQ = Base64UrlEncoder.DecodeBytes(jwk.DQ),
            InverseQ = Base64UrlEncoder.DecodeBytes(jwk.QI)
        };

        RSA rsa = RSA.Create();
        rsa.ImportParameters(rsaParams);
        return rsa;
    }

    private static byte[] DecryptWithSymmetricKey(byte[] data, byte[] key)
    {
        if (data.Length == 0)
        {
            return data;
        }

        using var aes = Aes.Create();
        aes.Key = key;

        byte[] iv = new byte[aes.BlockSize / 8];
        Array.Copy(data, 0, iv, 0, iv.Length);
        aes.IV = iv;

        using var memoryStream = new MemoryStream();
        using var cryptoStream = new CryptoStream(memoryStream, aes.CreateDecryptor(), CryptoStreamMode.Write);
        cryptoStream.Write(data, iv.Length, data.Length - iv.Length);
        cryptoStream.FlushFinalBlock();
        return memoryStream.ToArray();
    }

    private static string Base64UrlDecode(string input) =>
        Encoding.UTF8.GetString(Base64UrlDecodeBytes(input));

    private static byte[] Base64UrlDecodeBytes(string input) =>
        Convert.FromBase64String(input.Replace('-', '+').Replace('_', '/').PadRight(input.Length + (4 - input.Length % 4) % 4, '='));

    private static Dictionary<string, string?> ParseQueryParameters(string body) =>
        System.Web.HttpUtility.ParseQueryString(body).Cast<string>()
            .ToDictionary(k => k, v => System.Web.HttpUtility.ParseQueryString(body)[v]);
}
