using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Fhi.Slash.Public.SlashMessenger.HelseId.Exceptions;
using Fhi.Slash.Public.SlashMessenger.HelseId.Interfaces;

namespace Fhi.Slash.Public.SlashMessenger.HelseId;

/// <summary>
/// Default implementation of <see cref="IHelseIdService"/>.
/// 
/// This class handles the core logic for interacting with the HelseId.
/// This service is responsible for caching access tokens and requesting new access tokens from HelseId when needed.
/// 
/// You can inject your own implementation of <see cref="IHelseIdService"/> if you want to override the default behavior.
/// </summary>
public class DefaultHelseIdService : IHelseIdService
{
    private static readonly SemaphoreSlim _requestAccessTokenLock = new(1, 1);

    public const string AccessTokenCacheKey = "HelseIdAccessToken";

    private readonly ILogger _logger;
    private readonly IMemoryCache _memoryCache;
    private readonly IHelseIdClient _helseIdClient;


    /// <summary>
    /// Constructor for <see cref="DefaultHelseIdService"/>.
    /// </summary>
    /// <param name="logger">The logger instance used for logging operations.</param>
    /// <param name="memoryCache">The memory cache for storing access tokens.</param>
    /// <param name="helseIdClient">The client used to interact with the HelseId API.</param>
    public DefaultHelseIdService(ILogger<DefaultHelseIdService> logger, IMemoryCache memoryCache, IHelseIdClient helseIdClient)
    {
        _logger = logger;
        _memoryCache = memoryCache;
        _helseIdClient = helseIdClient;
    }

    /// <summary>
    /// Retrieves an access token from the HelseID client.
    /// </summary>
    /// <param name="dPoPProofJwk">The <see cref="JsonWebKey"/> used when generating DPoP proofs.</param>
    /// <param name="parentOrganizationNumber">Optional parent organization number used for multi-tenant token requests.</param>
    /// <returns>The access token as a string.</returns>
    /// <exception cref="HelseIdServiceException">Thrown if the access token retrieval fails.</exception>
    public virtual async Task<string> GetAccessToken(JsonWebKey dPoPProofJwk, string? parentOrganizationNumber = null)
    {
        _logger.LogDebug("Getting Access Token from HelseId");

        var normalizedParentOrganizationNumber = OrganizationNumberTools.NormalizeParentOrganizationNumber(parentOrganizationNumber);
        var accessTokenCacheKey = GetAccessTokenCacheKey(normalizedParentOrganizationNumber);

        // Get access token from cache
        string? accessToken;
        try
        {
            _logger.LogTrace("Getting Access Token from cache");
            accessToken = GetAccessTokenFromCache(accessTokenCacheKey);
            _logger.LogTrace("Finished getting Access Token from cache. Found: {foundCachedAccessToken}", !string.IsNullOrEmpty(accessToken));
        }
        catch (Exception ex)
        {
            throw new HelseIdServiceException("Requesting a new access token from HelseId failed", ex);
        }

        if (accessToken != null)
        {
            _logger.LogDebug("Got cached Access Token from HelseId");
            return accessToken;
        }

        // Request new access token
        try
        {
            _logger.LogTrace("Requesting new Access Token from HelseId");
            accessToken = await RequestNewAccessToken(dPoPProofJwk, normalizedParentOrganizationNumber, accessTokenCacheKey);
            _logger.LogTrace("Got Access Token from HelseId");
        }
        catch (Exception ex)
        {
            throw new HelseIdServiceException("Requesting a new access token from HelseId failed", ex);
        }

        _logger.LogDebug("Got Access Token from new response from HelseId");
        return accessToken;
    }

    /// <summary>
    /// Request a new access token from HelseId.
    /// This method will persist the access token in the cache for later use.
    /// 
    /// This method uses a semaphore to ensure that only one request to HelseId for an access token is made at a time. 
    /// If the access token is already in the cache, the semaphore ensures that concurrent requests do not redundantly fetch a new token.
    /// </summary>
    /// <param name="dPoPProofJwk">The <see cref="JsonWebKey"/> used when generating DPoP proofs.</param>
    /// <param name="normalizedParentOrganizationNumber">Normalized parent organization number used for multi-tenant token requests.</param>
    /// <param name="accessTokenCacheKey">The cache key used for retrieving and storing the access token.</param>
    /// <returns>An access token as a string.</returns>
    protected virtual async Task<string> RequestNewAccessToken(JsonWebKey dPoPProofJwk, string? normalizedParentOrganizationNumber, string accessTokenCacheKey)
    {
        _logger.LogDebug("Requesting new Access Token from HelseId");

        await _requestAccessTokenLock.WaitAsync();
        _logger.LogTrace("Lock acquired for requesting Access Token from HelseId");
        try
        {
            // Return cached access token if available
            // This is to prevent multiple requests for access token
            _logger.LogTrace("Getting Access Token from cache");
            var cachedAccessToken = GetAccessTokenFromCache(accessTokenCacheKey);
            _logger.LogTrace("Finished getting Access Token from cache. Found: {foundCachedAccessToken}", !string.IsNullOrEmpty(cachedAccessToken));

            if (cachedAccessToken != null)
            {
                _logger.LogDebug("Got Access Token from cache when requesting access token from HelseId");
                return cachedAccessToken;
            }

            // Get new access token
            var tokenResponse = await _helseIdClient.GetAccessToken(dPoPProofJwk, normalizedParentOrganizationNumber);
            if (tokenResponse.IsError || tokenResponse.AccessToken == null)
            {
                var errorMessage = tokenResponse.Error ?? "No access token in the token response returned from HelseId";
                throw new InvalidOperationException(errorMessage);
            }

            // Cache the access token
            _logger.LogTrace("Caching Access Token from HelseId");
            _memoryCache.Set(accessTokenCacheKey, tokenResponse.AccessToken, TimeSpan.FromSeconds(tokenResponse.ExpiresIn - 30)); // Skew expiry time -30 sec. To ensure that token is valid upon request
            _logger.LogTrace("Cached Access Token from HelseId");

            return tokenResponse.AccessToken;
        }
        finally
        {
            _logger.LogTrace("Releasing lock for requesting Access Token from HelseId");
            _requestAccessTokenLock.Release();
        }
    }

    /// <summary>
    /// Attempts to retrieve the access token from the cache.
    /// </summary>
    /// <param name="accessTokenCacheKey">The cache key used for retrieving the access token.</param>
    /// <returns>A string containing the access token, or <c>null</c> if not found.</returns>
    private string? GetAccessTokenFromCache(string accessTokenCacheKey) =>
        _memoryCache.TryGetValue<string>(accessTokenCacheKey, out var accessToken) ? accessToken : null;

    private static string GetAccessTokenCacheKey(string? normalizedParentOrganizationNumber) =>
        string.IsNullOrEmpty(normalizedParentOrganizationNumber) ?
            AccessTokenCacheKey :
            $"{AccessTokenCacheKey}:{normalizedParentOrganizationNumber}";
}
