using IdentityModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Fhi.Slash.Public.SlashMessenger.Extensions;
using Fhi.Slash.Public.SlashMessenger.HelseId.Interfaces;
using Fhi.Slash.Public.SlashMessenger.Slash.Exceptions;
using Fhi.Slash.Public.SlashMessenger.Slash.Interfaces;
using Fhi.Slash.Public.SlashMessenger.Slash.Models;
using Fhi.Slash.Public.SlashMessenger.Tools;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Fhi.Slash.Public.SlashMessenger.Slash;

/// <summary>
/// The default implementation of <see cref="ISlashService"/>.
/// 
/// This class handles the core logic for interacting with the Slash API.
/// You can provide your own implementation of <see cref="ISlashService"/> to customize or override the default behavior.
/// </summary>
public class DefaultSlashService : ISlashService
{
    private readonly ILogger<DefaultSlashService> _logger;
    private readonly ISlashClient _slashClient;
    private readonly IHelseIdService _helseIdService;
    private readonly JsonWebKey _dPoPProofJwk;
    private readonly Uri _slashMessageUrl;

    /// <summary>
    /// Constructor for <see cref="DefaultSlashService"/>.
    /// </summary>
    /// <param name="slashConfig">The Slash configuration object.</param>
    /// <param name="logger">The logger instance used for logging messages.</param>
    /// <param name="slashClient">The client used to interact with the Slash API.</param>
    /// <param name="IHelseIdService">The HelseId client used to obtain access tokens from HelseId.</param>
    /// <param name="dPoPProofJwk">The <see cref="JsonWebKey"/> used when generating the DPoP proof.</param>
    public DefaultSlashService(
        SlashConfig slashConfig,
        ILogger<DefaultSlashService> logger,
        ISlashClient slashClient,
        IHelseIdService IHelseIdService,
        [FromKeyedServices(ServiceCollectionExtensions.dPoPProofJwkKey)] JsonWebKey dPoPProofJwk)
    {
        _logger = logger;
        _slashClient = slashClient;
        _helseIdService = IHelseIdService;
        _dPoPProofJwk = dPoPProofJwk;
        _slashMessageUrl = new Uri(new Uri(slashConfig.BaseUrl), slashConfig.MessageEndpoint);
    }

    /// <summary>
    /// Prepares the message and uses the <see cref="ISlashClient"/> to send it to the Slash API.
    /// 
    /// This method retrieves a public key from the Slash API, encrypts the message with a symmetric key, and encrypts the symmetric key with the retrieved public key.
    /// It then acquires an access token from HelseId, creates a DPoP proof, and sends the encrypted message to the Slash API.
    /// </summary>
    /// <param name="rawJsonMessage">The serialized JSON string representing the message to be sent.</param>
    /// <param name="messageType">The type of the message.</param>
    /// <param name="messageVersion">The version of the message.</param>
    /// <param name="parentOrganizationNumber">Optional parent organization number used for multi-tenant token requests to HelseID.</param>
    /// <returns>An <see cref="SendMessageResponse"/> containing the response from the Slash API.</returns>
    /// <exception cref="SlashServiceException">Thrown when any step in the process fails.</exception>
    public virtual async Task<SendMessageResponse> PrepareAndSendMessage(string rawJsonMessage, string messageType, string messageVersion, string? parentOrganizationNumber = null)
    {
        _logger.LogDebug("Sending message to Slash API");

        // Validate input
        try
        {
            _logger.LogTrace("Validating input");
            ValidateInput(rawJsonMessage, messageType, messageVersion);
            _logger.LogTrace("Input validated");
        }
        catch(Exception ex)
        {
            throw new SlashServiceException("Input validation failed", ex);
        }

        // Get public key
        PublicKeyInfo slashPublicKeyInfo;
        try
        {
            _logger.LogTrace("Getting public key from Slash API");
            slashPublicKeyInfo = await GetPublicKey();
            _logger.LogTrace("Got public key from Slash API");
        }
        catch(Exception ex)
        {
            throw new SlashServiceException("Could not get public key from Slash", ex);
        }

        // Encrypt message
        EncryptedMessage encryptedMessage;
        try
        {
            _logger.LogTrace("Encrypting message");
            encryptedMessage = EncryptMessage(rawJsonMessage, slashPublicKeyInfo);
            _logger.LogTrace("Message encrypted");
        }
        catch (Exception ex)
        {
            throw new SlashServiceException("Could not encrypt message", ex);
        }

        // Get access token from HelseId
        string dPoPAccessToken;
        try
        {
            _logger.LogTrace("Getting access token from HelseId");
            dPoPAccessToken = await GetHelseDPoPAccessToken(parentOrganizationNumber);
            _logger.LogTrace("Got access token from HelseId");
        }
        catch (Exception ex)
        {
            throw new SlashServiceException("Could not get access token from HelseId", ex);
        }

        // Create DPoP proof
        string dPoPProof;
        try
        {
            _logger.LogTrace("Creating DPoP Proof");
            dPoPProof = CreateDPoPProof(
                dPoPAccessToken, 
                messageType, 
                messageVersion, 
                encryptedMessage.MessageHash,
                encryptedMessage.EncryptedSymmetricKey,
                slashPublicKeyInfo.Id.ToString());
            _logger.LogTrace("DPoP Proof created");
        }
        catch (Exception ex)
        {
            throw new SlashServiceException("Could not create DPoP Proof", ex);
        }

        // Send message
        SendMessageResponse response;
        try
        {
            _logger.LogTrace("Sending message to Slash API");
            var slashMessage = new SlashMessage
            {
                AccessToken = dPoPAccessToken,
                DPoPProof = dPoPProof,
                Payload = encryptedMessage.Message
            };

            response = await _slashClient.SendMessage(slashMessage);
            _logger.LogTrace("Message sent to Slash API");
        }
        catch (Exception ex)
        {
            throw new SlashServiceException("Could not send message to Slash API", ex);
        }

        _logger.LogDebug("Message sent to Slash API");
        return response;
    }

    /// <summary>
    /// Validates the input before processing the message.
    /// </summary>
    /// <param name="rawJsonMessage">The serialized JSON string representing the message to be processed.</param>
    /// <param name="messageType">The type of the message.</param>
    /// <param name="messageVersion">The version of the message.</param>
    protected virtual void ValidateInput(string rawJsonMessage, string messageType, string messageVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawJsonMessage);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageVersion);

        var msgObj = JsonSerializer.Deserialize<object>(rawJsonMessage);
        if (msgObj is not JsonElement jsonElement || jsonElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Payload must be an array of objects");
        }
    }

    /// <summary>
    /// Retrieves the latest public key from the Slash API.
    /// </summary>
    /// <returns>A <see cref="PublicKeyInfo"/> containing details about the latest public key.</returns>
    protected virtual async Task<PublicKeyInfo> GetPublicKey() =>
        (await _slashClient.GetPublicKeys()).First();

    /// <summary>
    /// Encrypts the message using a symmetric key and encrypts the symmetric key using the provided public key.
    /// </summary>
    /// <param name="rawJsonMessage">The serialized JSON string representing the message to be encrypted.</param>
    /// <param name="publicKeyInfo">Information about the public key used to encrypt the symmetric key.</param>
    /// <returns>An <see cref="EncryptedMessage"/> containing the message hash, encrypted symmetric key and encrypted message</returns>
    protected virtual EncryptedMessage EncryptMessage(string rawJsonMessage, PublicKeyInfo publicKeyInfo)
    {
        var msgInBytes = Encoding.UTF8.GetBytes(rawJsonMessage);
        var msgHash = Base64Url.Encode(SHA256.HashData(msgInBytes));
        var symmetricKey = CryptoTools.GenerateRandomKey(32);
        byte[] encSymKeyBytes = CryptoTools.EncryptWithPublicKey(symmetricKey, publicKeyInfo.PublicKey);
        byte[] encryptedMessageBytes = CryptoTools.EncryptWithSymmetricKey(msgInBytes, symmetricKey);
        var encSymKey = Base64Url.Encode(encSymKeyBytes);
        var encryptedMessage = Convert.ToBase64String(encryptedMessageBytes);

        return new EncryptedMessage
        {
            MessageHash = msgHash,
            EncryptedSymmetricKey = encSymKey,
            Message = encryptedMessage
        };
    }

    /// <summary>
    /// Retrieves an access token with DPoP support from HelseId.
    /// </summary>
    /// <param name="parentOrganizationNumber">Optional parent organization number used for multi-tenant token requests.</param>
    /// <returns>A string containing the access token issued by HelseId.</returns>
    protected virtual async Task<string> GetHelseDPoPAccessToken(string? parentOrganizationNumber = null) =>
        await _helseIdService.GetAccessToken(_dPoPProofJwk, parentOrganizationNumber);

    /// <summary>
    /// Creates a DPoP proof for the message.
    /// </summary>
    /// <param name="accessToken">The access token with DPoP support.</param>
    /// <param name="messageType">The type of the message.</param>
    /// <param name="messageVersion">The version of the message.</param>
    /// <param name="msgHash">The hash of the message before encryption.</param>
    /// <param name="encSymKey">The encrypted symmetric key.</param>
    /// <param name="encKeyId">The identifier of the public key used to encrypt the symmetric key.</param>
    /// <returns>A JWT string representing the DPoP proof.</returns>
    protected virtual string CreateDPoPProof(string accessToken, string messageType, string messageVersion, string msgHash, string encSymKey, string encKeyId) =>
        _dPoPProofJwk.CreateDPoPProof(
            _slashMessageUrl.AbsoluteUri,
            HttpMethod.Post.Method,
            accessToken: accessToken,
            customPayloadClaims: new Dictionary<string, string>(){
                { "msg_type", messageType },
                { "msg_version", messageVersion },
                { "msg_hash", msgHash },
                { "enc_sym_key", encSymKey },
                { "enc_key_id", encKeyId }
            });
}
