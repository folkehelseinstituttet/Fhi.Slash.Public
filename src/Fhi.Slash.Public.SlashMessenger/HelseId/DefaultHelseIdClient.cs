using IdentityModel;
using IdentityModel.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Fhi.Slash.Public.SlashMessenger.Extensions;
using Fhi.Slash.Public.SlashMessenger.HelseId.Exceptions;
using Fhi.Slash.Public.SlashMessenger.HelseId.Interfaces;
using Fhi.Slash.Public.SlashMessenger.HelseId.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using static IdentityModel.OidcConstants;

using TokenResponse = IdentityModel.Client.TokenResponse;

namespace Fhi.Slash.Public.SlashMessenger.HelseId;

/// <summary>
/// The default implementation of <see cref="IHelseIdClient"/>.
///
/// This class's main responsibility is to communicate with HelseId to get an access token.
/// You can inject your own implementation of <see cref="IHelseIdClient"/> if you want to override the default behavior.
/// </summary>
public class DefaultHelseIdClient : IHelseIdClient
{
    private readonly HelseIdConfig _helseIdConfig;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DefaultHelseIdClient> _logger;
    private readonly JsonWebKey _helseIdJwk;
    private readonly string _audience;

    /// <summary>
    /// Constructor for <see cref="DefaultHelseIdClient"/>.
    /// </summary>
    /// <param name="helseIdConfig">Configuration settings for HelseID.</param>
    /// <param name="logger">The logger used for logging operations.</param>
    /// <param name="httpClientFactory">Factory for creating <see cref="HttpClient"/> instances.</param>
    /// <param name="helseIdJwk">The <see cref="JsonWebKey"/> used for HelseID authentication.</param>
    public DefaultHelseIdClient(
        HelseIdConfig helseIdConfig, 
        ILogger<DefaultHelseIdClient> logger,
        IHttpClientFactory httpClientFactory,
        [FromKeyedServices(ServiceCollectionExtensions.helseIdJwkKey)] JsonWebKey helseIdJwk)
    {
        helseIdConfig.Validate();

        _helseIdConfig = helseIdConfig;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _helseIdJwk = helseIdJwk;
        _audience = new Uri(_helseIdConfig.TokenEndpoint).GetLeftPart(UriPartial.Authority);
    }

    /// <summary>
    /// Gets an access token from HelseId.
    /// </summary>
    /// <param name="dPoPProofJwk">The <see cref="JsonWebKey"/> used when generating the DPoP proof.</param>
    /// <param name="parentOrganizationNumber">Optional parent organization number used for multi-tenant token requests.</param>
    /// <returns>A <see cref="TokenResponse"/> with the Access Token</returns>
    /// <exception cref="HelseIdClientException">Thrown if the access token retrieval fails.</exception>
    public virtual async Task<TokenResponse> GetAccessToken(JsonWebKey dPoPProofJwk, string? parentOrganizationNumber = null)
    {
        _logger.LogDebug("Getting Access Token from HelseId");

        // Creating Client Credentials Token Request
        ClientCredentialsTokenRequest clientCredentialsTokenRequest;
        try
        {
            clientCredentialsTokenRequest = CreateClientCredentialsTokenRequestAsync(dPoPProofJwk, dPoPNonce: null, parentOrganizationNumber);
        }
        catch (Exception ex)
        {
            throw new HelseIdClientException("Could not create Client Credentials Token Request", ex);
        }

        // Requesting Access Token from HelseId
        TokenResponse tokenResponse;
        try
        {
            using var httpClient = _httpClientFactory.CreateClient(_helseIdConfig.BasicClientName);
            tokenResponse = await httpClient.RequestClientCredentialsTokenAsync(clientCredentialsTokenRequest);

            // If the token response requires a DPoP nonce, create a new request with the nonce from the previous response
            if (tokenResponse.IsError && tokenResponse.Error == "use_dpop_nonce" && !string.IsNullOrEmpty(tokenResponse.DPoPNonce))
            {
                clientCredentialsTokenRequest = CreateClientCredentialsTokenRequestAsync(dPoPProofJwk, tokenResponse.DPoPNonce, parentOrganizationNumber);
                tokenResponse = await httpClient.RequestClientCredentialsTokenAsync(clientCredentialsTokenRequest);
            }

            if (tokenResponse.IsError || tokenResponse.AccessToken == null)
            {
                var errorMessage = tokenResponse.Error ?? "No access token in the token response returned from HelseId";
                throw new InvalidOperationException(errorMessage);
            }
        }
        catch (Exception ex)
        {
            throw new HelseIdClientException("Could not get Access Token from HelseId", ex);
        }

        return tokenResponse;
    }

    /// <summary>
    /// Creates a <see cref="ClientCredentialsTokenRequest"/> to be used to get an access token from HelseId.
    /// </summary>
    /// <param name="dPoPProofJwk">The <see cref="JsonWebKey"/> used when generating DPoP proofs.</param>
    /// <param name="dPoPNonce">A nonce issued by HelseId</param>
    /// <param name="parentOrganizationNumber">Optional parent organization number used for multi-tenant token requests.</param>
    /// <returns>A <see cref="ClientCredentialsTokenRequest"/> for requesting a new access token.</returns>
    protected virtual ClientCredentialsTokenRequest CreateClientCredentialsTokenRequestAsync(JsonWebKey dPoPProofJwk, string? dPoPNonce, string? parentOrganizationNumber) => new()
    {
        Address = _helseIdConfig.TokenEndpoint,
        ClientAssertion = BuildClientAssertion(parentOrganizationNumber),
        ClientId = _helseIdConfig.ClientId,
        GrantType = GrantTypes.ClientCredentials,
        ClientCredentialStyle = ClientCredentialStyle.PostBody,
        DPoPProofToken = dPoPProofJwk.CreateDPoPProof(_helseIdConfig.TokenEndpoint, HttpMethod.Post.Method, dPoPNonce)
    };

    /// <summary>
    /// Creates a client assertion for use in client credentials token requests to HelseID. 
    /// The client assertion is a JWT signed with the private key corresponding to the public key registered with HelseID.
    /// </summary>
    /// <param name="parentOrganizationNumber">Optional parent organization number used for multi-tenant token requests.</param>
    /// <returns>A <see cref="ClientAssertion"/> to be used in a <see cref="ClientCredentialsTokenRequest"/>.</returns>
    protected virtual ClientAssertion BuildClientAssertion(string? parentOrganizationNumber = null)
    {
        var normalizedParentOrganizationNumber = OrganizationNumberTools.NormalizeParentOrganizationNumber(parentOrganizationNumber);

        var claims = new List<Claim>
        {
            new(JwtClaimTypes.Subject, _helseIdConfig.ClientId.ToString()),
            new(JwtClaimTypes.IssuedAt, DateTimeOffset.Now.ToUnixTimeSeconds().ToString()),
            new(JwtClaimTypes.JwtId, Guid.NewGuid().ToString("N"))
        };
        
        var signingCredentials = new SigningCredentials(_helseIdJwk, SecurityAlgorithms.RsaSha256);

        var header = new JwtHeader(signingCredentials, null, "client-authentication+jwt");

        var payload = new JwtPayload(
            _helseIdConfig.ClientId.ToString(),
            _audience,
            claims,
            DateTime.UtcNow,
            DateTime.UtcNow.AddSeconds(60));

        if (!string.IsNullOrEmpty(normalizedParentOrganizationNumber))
        {
            payload["assertion_details"] = BuildAssertionDetails(normalizedParentOrganizationNumber);
        }
        
        var token = new JwtSecurityToken(header, payload);

        return new ClientAssertion
        {
            Type = ClientAssertionTypes.JwtBearer,
            Value = new JwtSecurityTokenHandler().WriteToken(token)
        };
    }

    private static List<Dictionary<string, object>> BuildAssertionDetails(string normalizedParentOrganizationNumber) =>
    [
        new()
        {
            ["type"] = "helseid_authorization",
            ["practitioner_role"] = new Dictionary<string, object>
            {
                ["organization"] = new Dictionary<string, object>
                {
                    ["identifier"] = new Dictionary<string, object>
                    {
                        ["system"] = "urn:oid:1.0.6523",
                        ["type"] = "ENH",
                        ["value"] = OrganizationNumberTools.ToOrganizationIdentifier(normalizedParentOrganizationNumber)
                    }
                }
            }
        }
    ];
}
