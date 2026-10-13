using Duende.IdentityModel.Client;
using Fhi.Slash.Public.SlashMessenger.Slash.Exceptions;
using Fhi.Slash.Public.SlashMessenger.Slash.Interfaces;
using Fhi.Slash.Public.SlashMessenger.Slash.Models;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using static Duende.IdentityModel.OidcConstants;

namespace Fhi.Slash.Public.SlashMessenger.Slash;

/// <summary>
/// The default implementation of <see cref="ISlashClient"/>.
/// 
/// This class is responsible for handling communication with the Slash API.
/// You can provide your own implementation of <see cref="ISlashClient"/> to customize or override the default behavior.
/// </summary>
public class DefaultSlashClient : ISlashClient
{
    private readonly SlashConfig _slashConfig;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DefaultSlashClient> _logger;

    /// <summary>
    /// Constructor for <see cref="DefaultSlashClient"/>.
    /// </summary>
    /// <param name="logger">The logger instance used for logging operations.</param>
    /// <param name="httpClientFactory">The factory used to create HTTP clients for communication with the Slash API.</param>
    public DefaultSlashClient(SlashConfig slashConfig, ILogger<DefaultSlashClient> logger, IHttpClientFactory httpClientFactory)
    {
        _slashConfig = slashConfig;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Retrieves the public keys used for message encryption.
    /// These keys are utilized to encrypt the symmetric key that secures the message.
    /// The public key reference is included in the DPoP proof, allowing the receiver to decrypt the symmetric key.
    /// </summary>
    /// <returns>List of <see cref="PublicKeyInfo"/></returns>
    /// <exception cref="SlashClientException">Thrown if the retrival of public keys fails.</exception>
    public virtual async Task<List<PublicKeyInfo>> GetPublicKeys()
    {
        _logger.LogDebug("Retrieving public keys from Slash API");

        // Get public keys from Slash
        string rawPublicKeys;
        try
        {
            _logger.LogTrace("Retrieving public keys from Slash API");
            using var httpClient = _httpClientFactory.CreateClient(_slashConfig.BasicClientName);
            var response = await httpClient.GetAsync(_slashConfig.KeysEndpoint);
            rawPublicKeys = await response.Content.ReadAsStringAsync();
            _logger.LogTrace("Public keys retrieved from Slash API");
        }
        catch (Exception ex)
        {
            throw new SlashClientException("Could not retrive public keys from Slash", ex);
        }

        // Parse public keys
        List<PublicKeyInfo>? keys;
        try
        {
            _logger.LogTrace("Parsing public keys from Slash API");

            keys = (JsonSerializer.Deserialize<List<PublicKeyInfo>>(rawPublicKeys)?
                .OrderByDescending(k => k.ExpirationDate)
                .ToList()) ?? throw new InvalidOperationException("Parsing of response payload resulted in null");

            _logger.LogTrace("Public keys parsed successfully");
        }
        catch (Exception ex)
        {
            throw new SlashClientException("Could not parse public keys from Slash API", ex);
        }

        // Validate public keys
        if (keys.Count == 0)
        {
            throw new SlashClientException("No public keys were returned in the response from Slash API. Parsed result successfully");
        }

        _logger.LogDebug("Public keys retrieved from Slash API");
        return keys;
    }

    /// <summary>
    /// Sends a message to the Slash API. 
    /// The required header values are automatically included in the HttpClient used for the request (Configured in the ServiceCollectionExtensions).
    /// </summary>
    /// <param name="message">The message to be sent.</param>
    /// <returns>A <see cref="SendMessageResponse"/> containing the result of the message send operation.</returns>
    /// <exception cref="SlashClientException">Thrown if the message sending fails.</exception>
    public virtual async Task<SendMessageResponse> SendMessage(SlashMessage message)
    {
        _logger.LogDebug("Sending message to Slash API");

        // Validate input
        try
        {
            _logger.LogTrace("Validating input message");
            message.Validate();
            _logger.LogTrace("Input message validated");
        }
        catch (Exception ex)
        {
            throw new SlashClientException("Validation of input failed", ex);
        }

        var httpClient = _httpClientFactory.CreateClient(_slashConfig.DPoPClientName);

        // Configure HttpClient
        try
        {
            _logger.LogTrace("Configuring HttpClient before sending message");
            httpClient.SetToken(AuthenticationSchemes.AuthorizationHeaderDPoP, message.AccessToken!);
            httpClient.DefaultRequestHeaders.Add(HttpHeaders.DPoP, message.DPoPProof!);
            _logger.LogTrace("HttpClient configured successfully");
        }
        catch (Exception ex)
        {
            throw new SlashClientException("Failed to configure the HttpClient before sending the message", ex);
        }

        // Post message
        HttpResponseMessage response;
        try
        {
            _logger.LogTrace("Sending message to Slash API");
            using var content = new StringContent(message.Payload!, Encoding.UTF8, "text/plain");
            response = await httpClient.PostAsync(_slashConfig.MessageEndpoint, content);
            _logger.LogTrace("Message sent to Slash API");
        }
        catch (Exception ex)
        {
            throw new SlashClientException("Failed to send message to Slash API", ex);
        }

        // Parse response
        Guid correlationId;
        ProcessMessageResponse parsedResponseContent;
        try
        {
            _logger.LogTrace("Parsing response from Slash API");
            var rawResponseContent = await response.Content.ReadAsStringAsync();
            var rawCorrelationId = response.Headers.GetValues("X-Correlation-ID").FirstOrDefault();
            parsedResponseContent = JsonSerializer.Deserialize<ProcessMessageResponse>(rawResponseContent) ?? new ProcessMessageResponse();
            correlationId = string.IsNullOrEmpty(rawCorrelationId) ? Guid.Empty : Guid.Parse(rawCorrelationId);
            _logger.LogTrace("Response from Slash API parsed successfully");
        }
        catch (Exception ex)
        {
            throw new SlashClientException("Failed to parse response from Slash API", ex);
        }

        _logger.LogDebug("Message sent to Slash API");
        return new SendMessageResponse
        {
            CorrelationId = correlationId,
            ProcessMessageResponse = parsedResponseContent,
            ResposeMessage = response
        };
    }
}