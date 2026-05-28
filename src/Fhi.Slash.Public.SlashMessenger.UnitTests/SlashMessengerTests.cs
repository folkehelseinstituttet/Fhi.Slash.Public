using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json.Linq;
using Fhi.Slash.Public.SlashMessenger.HelseId;
using Fhi.Slash.Public.SlashMessenger.HelseId.Exceptions;
using Fhi.Slash.Public.SlashMessenger.HelseId.Interfaces;
using Fhi.Slash.Public.SlashMessenger.HelseId.Models;
using Fhi.Slash.Public.SlashMessenger.Slash;
using Fhi.Slash.Public.SlashMessenger.Slash.Exceptions;
using Fhi.Slash.Public.SlashMessenger.Slash.Interfaces;
using Fhi.Slash.Public.SlashMessenger.Slash.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using NSubstitute;

namespace Fhi.Slash.Public.SlashMessenger.UnitTests;

[TestClass]
public class SlashMessengerTests
{
    private readonly string _accessToken = File.ReadAllText("TestFiles/access-token.txt");
    private readonly string _testMessage = File.ReadAllText("TestFiles/empty_message.json");
    private readonly JObject _testKeys = JObject.Parse(File.ReadAllText("TestFiles/test_keys.json"))!;
    private readonly X509Certificate2 _certificate = new("TestFiles/Certs/test_cert_without_password.pfx", string.Empty, X509KeyStorageFlags.Exportable);
    private readonly JsonWebKey _dPoPProofJwk = new(File.ReadAllText("TestFiles/private_jwk.json"));

    private readonly Uri _slashBaseUrl = new("https://slash-api.just-a-test.com");
    private readonly Uri _helseIdBaseUrl = new("https://helseid-api.just-a-test.com");

    private readonly Dictionary<object, object> _cacheStore = [];

    private readonly SlashConfig _defaultSlashConfig = new()
    {
        BaseUrl = "https://slash-api.just-a-test.com",
        ExportSoftwareVersion = "1.0.0",
        SoftwareName = "TestSoftware",
        SoftwareVersion = "1.0.0",
        VendorName = "TestVendor",
    };

    [TestMethod]
    public async Task ShouldThrowErrorIfMessageIsNotAValidJson()
    {
        // Arrange
        var slashService = new DefaultSlashService(_defaultSlashConfig, new NullLogger<DefaultSlashService>(), Substitute.For<ISlashClient>(), Substitute.For<IHelseIdService>(), _dPoPProofJwk);

        // Act
        var exception = await Assert.ThrowsExactlyAsync<SlashServiceException>(async () =>
            await slashService.PrepareAndSendMessage("NOT_A_VALID_JSON", "test", "test"));

        // Assert
        Assert.AreEqual("Input validation failed", exception.Message);
    }

    [TestMethod]
    public async Task ShouldThrowErrorIfMessageIsNotAJsonArray()
    {
        // Arrange
        var slashService = new DefaultSlashService(_defaultSlashConfig, new NullLogger<DefaultSlashService>(), Substitute.For<ISlashClient>(), Substitute.For<IHelseIdService>(), _dPoPProofJwk);

        // Act
        var exception = await Assert.ThrowsExactlyAsync<SlashServiceException>(async () =>
            await slashService.PrepareAndSendMessage("{\"error\": \"SHOULD_NOT_BE_JSON_OBJECT\"}", "test", "test"));

        // Assert
        Assert.AreEqual("Input validation failed", exception.Message);
    }

    [TestMethod]
    public async Task HelseIdClientShouldCacheAccessToken()
    {
        // Arrange
        var slashHttpFactory = Substitute.For<IHttpClientFactory>();
        slashHttpFactory.CreateClient(_defaultSlashConfig.BasicClientName).Returns(_ => CreateSlashDefaultClient());
        slashHttpFactory.CreateClient(_defaultSlashConfig.DPoPClientName).Returns(_ => CreateSlashDPoPClient());

        var helseIdConfig = new HelseIdConfig
        {
            TokenEndpoint = new Uri(_helseIdBaseUrl, "token").AbsoluteUri,
            ClientId = "just-a-test",
            Certificate = _certificate
        };

        var helseIdHttpFactory = Substitute.For<IHttpClientFactory>();
        helseIdHttpFactory.CreateClient(helseIdConfig.BasicClientName).Returns(_ => CreateHelseIdClient());

        var memoryCache = SetupMemoryCacheMock();

        var helseIdClient = new DefaultHelseIdClient(helseIdConfig, new NullLogger<DefaultHelseIdClient>(), helseIdHttpFactory, _dPoPProofJwk);
        var helseIdService = new DefaultHelseIdService(new NullLogger<DefaultHelseIdService>(), memoryCache, helseIdClient);

        var slashClient = new DefaultSlashClient(_defaultSlashConfig, new NullLogger<DefaultSlashClient>(), slashHttpFactory);
        var slashService = new DefaultSlashService(_defaultSlashConfig, new NullLogger<DefaultSlashService>(), slashClient, helseIdService, _dPoPProofJwk);

        // Act
        await slashService.PrepareAndSendMessage(_testMessage, "HST_Konsultasjon", "1");
        await slashService.PrepareAndSendMessage(_testMessage, "HST_Konsultasjon", "1");

        // Assert
        memoryCache.Received(3).TryGetValue(Arg.Any<object>(), out Arg.Any<object>()!);
        memoryCache.Received(1).CreateEntry(Arg.Any<object>());
    }

    [TestMethod]
    public async Task HelseIdClientShouldCacheAccessTokenPerParentOrganizationNumber()
    {
        // Arrange
        var slashHttpFactory = Substitute.For<IHttpClientFactory>();
        slashHttpFactory.CreateClient(_defaultSlashConfig.BasicClientName).Returns(_ => CreateSlashDefaultClient());
        slashHttpFactory.CreateClient(_defaultSlashConfig.DPoPClientName).Returns(_ => CreateSlashDPoPClient());

        var helseIdConfig = new HelseIdConfig
        {
            TokenEndpoint = new Uri(_helseIdBaseUrl, "token").AbsoluteUri,
            ClientId = "just-a-test",
            Certificate = _certificate
        };

        var helseIdHttpFactory = Substitute.For<IHttpClientFactory>();
        helseIdHttpFactory.CreateClient(helseIdConfig.BasicClientName).Returns(_ => CreateHelseIdClient());

        var memoryCache = SetupMemoryCacheMock();

        var helseIdClient = new DefaultHelseIdClient(helseIdConfig, new NullLogger<DefaultHelseIdClient>(), helseIdHttpFactory, _dPoPProofJwk);
        var helseIdService = new DefaultHelseIdService(new NullLogger<DefaultHelseIdService>(), memoryCache, helseIdClient);

        var slashClient = new DefaultSlashClient(_defaultSlashConfig, new NullLogger<DefaultSlashClient>(), slashHttpFactory);
        var slashService = new DefaultSlashService(_defaultSlashConfig, new NullLogger<DefaultSlashService>(), slashClient, helseIdService, _dPoPProofJwk);

        // Act
        await slashService.PrepareAndSendMessage(_testMessage, "HST_Konsultasjon", "1", parentOrganizationNumber: "972418013");
        await slashService.PrepareAndSendMessage(_testMessage, "HST_Konsultasjon", "1", parentOrganizationNumber: "983744516");
        await slashService.PrepareAndSendMessage(_testMessage, "HST_Konsultasjon", "1", parentOrganizationNumber: "972418013");

        // Assert
        memoryCache.Received(5).TryGetValue(Arg.Any<object>(), out Arg.Any<object>()!);
        memoryCache.Received(2).CreateEntry(Arg.Any<object>());

        Assert.IsTrue(_cacheStore.ContainsKey($"{DefaultHelseIdService.AccessTokenCacheKey}:972418013"));
        Assert.IsTrue(_cacheStore.ContainsKey($"{DefaultHelseIdService.AccessTokenCacheKey}:983744516"));
    }

    [TestMethod]
    public async Task HelseIdClientShouldIncludeParentOrganizationNumberInAssertionDetails()
    {
        // Arrange
        const string parentOrganizationNumber = "972418013";
        string? requestBody = null;

        var helseIdConfig = new HelseIdConfig
        {
            TokenEndpoint = new Uri(_helseIdBaseUrl, "token").AbsoluteUri,
            ClientId = "just-a-test",
            Certificate = _certificate
        };

        var helseIdHttpFactory = Substitute.For<IHttpClientFactory>();
        helseIdHttpFactory.CreateClient(helseIdConfig.BasicClientName).Returns(_ =>
            CreateHelseIdClient(request =>
            {
                requestBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            }));

        var helseIdClient = new DefaultHelseIdClient(helseIdConfig, new NullLogger<DefaultHelseIdClient>(), helseIdHttpFactory, _dPoPProofJwk);

        // Act
        await helseIdClient.GetAccessToken(_dPoPProofJwk, parentOrganizationNumber);

        // Assert
        Assert.IsNotNull(requestBody);

        var requestParameters = System.Web.HttpUtility.ParseQueryString(requestBody!);
        var rawClientAssertion = requestParameters["client_assertion"];

        Assert.IsNotNull(rawClientAssertion);

        var assertionJwt = new JwtSecurityTokenHandler().ReadJwtToken(rawClientAssertion);
        Assert.IsTrue(assertionJwt.Payload.TryGetValue("assertion_details", out var assertionDetailsClaim));

        var assertionDetails = JsonDocument.Parse(JsonSerializer.Serialize(assertionDetailsClaim)).RootElement;
        Assert.AreEqual(1, assertionDetails.GetArrayLength());

        var firstAssertionDetailsElement = assertionDetails[0];
        Assert.AreEqual("helseid_authorization", firstAssertionDetailsElement.GetProperty("type").GetString());

        var identifierElement = firstAssertionDetailsElement
            .GetProperty("practitioner_role")
            .GetProperty("organization")
            .GetProperty("identifier");

        Assert.AreEqual("urn:oid:1.0.6523", identifierElement.GetProperty("system").GetString());
        Assert.AreEqual("ENH", identifierElement.GetProperty("type").GetString());
        Assert.AreEqual($"NO:ORGNR:{parentOrganizationNumber}", identifierElement.GetProperty("value").GetString());
    }

    [TestMethod]
    public async Task HelseIdClientShouldNotIncludeAssertionDetailsWhenNoParentOrganizationNumber()
    {
        // Arrange
        string? requestBody = null;

        var helseIdConfig = new HelseIdConfig
        {
            TokenEndpoint = new Uri(_helseIdBaseUrl, "token").AbsoluteUri,
            ClientId = "just-a-test",
            Certificate = _certificate
        };

        var helseIdHttpFactory = Substitute.For<IHttpClientFactory>();
        helseIdHttpFactory.CreateClient(helseIdConfig.BasicClientName).Returns(_ =>
            CreateHelseIdClient(request =>
            {
                requestBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            }));

        var helseIdClient = new DefaultHelseIdClient(helseIdConfig, new NullLogger<DefaultHelseIdClient>(), helseIdHttpFactory, _dPoPProofJwk);

        // Act
        await helseIdClient.GetAccessToken(_dPoPProofJwk);

        // Assert
        Assert.IsNotNull(requestBody);

        var requestParameters = System.Web.HttpUtility.ParseQueryString(requestBody!);
        var rawClientAssertion = requestParameters["client_assertion"];

        Assert.IsNotNull(rawClientAssertion);

        var assertionJwt = new JwtSecurityTokenHandler().ReadJwtToken(rawClientAssertion);
        Assert.IsFalse(assertionJwt.Payload.TryGetValue("assertion_details", out _));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public async Task HelseIdClientShouldNotIncludeAssertionDetailsWhenParentOrganizationNumberIsEmpty(string parentOrganizationNumber)
    {
        // Arrange
        string? requestBody = null;

        var helseIdConfig = new HelseIdConfig
        {
            TokenEndpoint = new Uri(_helseIdBaseUrl, "token").AbsoluteUri,
            ClientId = "just-a-test",
            Certificate = _certificate
        };

        var helseIdHttpFactory = Substitute.For<IHttpClientFactory>();
        helseIdHttpFactory.CreateClient(helseIdConfig.BasicClientName).Returns(_ =>
            CreateHelseIdClient(request =>
            {
                requestBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            }));

        var helseIdClient = new DefaultHelseIdClient(helseIdConfig, new NullLogger<DefaultHelseIdClient>(), helseIdHttpFactory, _dPoPProofJwk);

        // Act
        await helseIdClient.GetAccessToken(_dPoPProofJwk, parentOrganizationNumber);

        // Assert
        Assert.IsNotNull(requestBody);

        var requestParameters = System.Web.HttpUtility.ParseQueryString(requestBody!);
        var rawClientAssertion = requestParameters["client_assertion"];

        Assert.IsNotNull(rawClientAssertion);

        var assertionJwt = new JwtSecurityTokenHandler().ReadJwtToken(rawClientAssertion);
        Assert.IsFalse(assertionJwt.Payload.TryGetValue("assertion_details", out _));
    }

    [TestMethod]
    public async Task ShouldSendMessageToSlashWithMultiTenant()
    {
        // Arrange
        var slashHttpFactory = Substitute.For<IHttpClientFactory>();
        slashHttpFactory.CreateClient(_defaultSlashConfig.BasicClientName).Returns(_ => CreateSlashDefaultClient());
        slashHttpFactory.CreateClient(_defaultSlashConfig.DPoPClientName).Returns(_ => CreateSlashDPoPClient());

        var helseIdConfig = new HelseIdConfig
        {
            TokenEndpoint = new Uri(_helseIdBaseUrl, "token").AbsoluteUri,
            ClientId = "just-a-test",
            Certificate = _certificate
        };

        var helseIdHttpFactory = Substitute.For<IHttpClientFactory>();
        helseIdHttpFactory.CreateClient(helseIdConfig.BasicClientName).Returns(_ => CreateHelseIdClient());

        var memoryCache = SetupMemoryCacheMock();

        var helseIdClient = new DefaultHelseIdClient(helseIdConfig, new NullLogger<DefaultHelseIdClient>(), helseIdHttpFactory, _dPoPProofJwk);
        var helseIdService = new DefaultHelseIdService(new NullLogger<DefaultHelseIdService>(), memoryCache, helseIdClient);

        var slashClient = new DefaultSlashClient(_defaultSlashConfig, new NullLogger<DefaultSlashClient>(), slashHttpFactory);
        var slashService = new DefaultSlashService(_defaultSlashConfig, new NullLogger<DefaultSlashService>(), slashClient, helseIdService, _dPoPProofJwk);

        // Act
        var response = await slashService.PrepareAndSendMessage(_testMessage, "HST_Konsultasjon", "1", parentOrganizationNumber: "972418013");

        // Assert
        Assert.IsTrue(response.ProcessMessageResponse.Delivered);
    }

    [TestMethod]
    public async Task HelseIdClientShouldThrowOnInvalidParentOrganizationNumber()
    {
        // Arrange
        var helseIdConfig = new HelseIdConfig
        {
            TokenEndpoint = new Uri(_helseIdBaseUrl, "token").AbsoluteUri,
            ClientId = "just-a-test",
            Certificate = _certificate
        };

        var helseIdHttpFactory = Substitute.For<IHttpClientFactory>();
        helseIdHttpFactory.CreateClient(helseIdConfig.BasicClientName).Returns(_ => CreateHelseIdClient());

        var helseIdClient = new DefaultHelseIdClient(helseIdConfig, new NullLogger<DefaultHelseIdClient>(), helseIdHttpFactory, _dPoPProofJwk);

        // Act
        var exception = await Assert.ThrowsExactlyAsync<HelseIdClientException>(() =>
            helseIdClient.GetAccessToken(_dPoPProofJwk, "NOT_VALID_ORG_NUMBER"));

        // Assert
        Assert.IsInstanceOfType<ArgumentException>(exception.InnerException);
    }

    [TestMethod]
    public async Task ShouldSendMessageToSlash()
    {
        // Arrange
        var slashHttpFactory = Substitute.For<IHttpClientFactory>();
        slashHttpFactory.CreateClient(_defaultSlashConfig.BasicClientName).Returns(_ => CreateSlashDefaultClient());
        slashHttpFactory.CreateClient(_defaultSlashConfig.DPoPClientName).Returns(_ => CreateSlashDPoPClient());

        var helseIdConfig = new HelseIdConfig
        {
            TokenEndpoint = new Uri(_helseIdBaseUrl, "token").AbsoluteUri,
            ClientId = "just-a-test",
            Certificate = _certificate
        };

        var helseIdHttpFactory = Substitute.For<IHttpClientFactory>();
        helseIdHttpFactory.CreateClient(helseIdConfig.BasicClientName).Returns(_ => CreateHelseIdClient());

        var memoryCache = SetupMemoryCacheMock();

        var helseIdClient = new DefaultHelseIdClient(helseIdConfig, new NullLogger<DefaultHelseIdClient>(), helseIdHttpFactory, _dPoPProofJwk);
        var helseIdService = new DefaultHelseIdService(new NullLogger<DefaultHelseIdService>(), memoryCache, helseIdClient);

        var slashClient = new DefaultSlashClient(_defaultSlashConfig, new NullLogger<DefaultSlashClient>(), slashHttpFactory);
        var slashService = new DefaultSlashService(_defaultSlashConfig, new NullLogger<DefaultSlashService>(), slashClient, helseIdService, _dPoPProofJwk);

        // Act
        var response = await slashService.PrepareAndSendMessage(_testMessage, "HST_Konsultasjon", "1");

        // Assert
        Assert.IsTrue(response.ProcessMessageResponse.Delivered);
    }

    private IMemoryCache SetupMemoryCacheMock()
    {
        var memoryCache = Substitute.For<IMemoryCache>();
        memoryCache.TryGetValue(Arg.Any<object>(), out Arg.Any<object>()!).Returns(call =>
        {
            var key = call[0];
            var found = _cacheStore.TryGetValue(key, out var value);
            call[1] = value;
            return found;
        });

        memoryCache.CreateEntry(Arg.Any<object>()).Returns(call =>
        {
            var key = call[0];
            var cacheEntry = Substitute.For<ICacheEntry>();
            cacheEntry.When(entry => entry.Dispose()).Do(_ =>
            {
                if (cacheEntry.Value != null)
                {
                    _cacheStore[key] = cacheEntry.Value;
                }
            });

            return cacheEntry;
        });

        return memoryCache;
    }

    private HttpClient CreateSlashDefaultClient()
    {
        var defaultHttpMessageHandler = new MockHttpMessageHandler(request =>
        {
            if ("/keys".Equals(request.RequestUri?.AbsolutePath))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new List<PublicKeyInfo>
                    {
                        new()
                        {
                            Id = "TESTKEY",
                            PublicKey = _testKeys.Value<string>("publicKey")!,
                            ExpirationDate = DateTime.Now.AddDays(1)
                        }
                    }))
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        return new HttpClient(defaultHttpMessageHandler)
        {
            BaseAddress = _slashBaseUrl,
        };
    }

    private HttpClient CreateSlashDPoPClient()
    {
        var dpopHttpMessageHandler = new MockHttpMessageHandler(request =>
        {
            if ("/message".Equals(request.RequestUri?.AbsolutePath))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Headers =
                    {
                        { "X-Correlation-ID", Guid.NewGuid().ToString() }
                    },
                    Content = new StringContent(
                        JsonSerializer.Serialize(new { delivered = true }),
                        Encoding.UTF8,
                        "application/json"
                    )
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        return new HttpClient(dpopHttpMessageHandler)
        {
            BaseAddress = _slashBaseUrl,
        };
    }

    private HttpClient CreateHelseIdClient(Action<HttpRequestMessage>? onRequest = null)
    {
        var helseIdHttpMessageHandler = new MockHttpMessageHandler(request =>
        {
            onRequest?.Invoke(request);

            if ("/token".Equals(request.RequestUri?.AbsolutePath))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        access_token = _accessToken,
                        expires_in = 60,
                        token_type = "DPoP",
                        scope = "fhi:slash.mottak/all"
                    }), Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        return new HttpClient(helseIdHttpMessageHandler)
        {
            BaseAddress = _helseIdBaseUrl,
        };
    }
}

public class MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handlerFunc) : DelegatingHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handlerFunc = handlerFunc ??
        throw new ArgumentNullException(nameof(handlerFunc));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(_handlerFunc(request));
    }
}
