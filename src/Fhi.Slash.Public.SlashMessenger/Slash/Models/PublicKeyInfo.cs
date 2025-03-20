using System.Text.Json.Serialization;

namespace Fhi.Slash.Public.SlashMessenger.Slash.Models;

/// <summary>
/// Public Key information from the Slash API.
/// The public keys are used to encrypt the symmetric key used to encrypt the message.
/// </summary>
public class PublicKeyInfo
{
    /// <summary>
    /// The ID of the public key.
    /// This value is used to reference the public key in the DPoP proof.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = null!;

    /// <summary>
    /// The expiration date of the public key.
    /// </summary>
    [JsonPropertyName("expirationDate")]
    public DateTime ExpirationDate { get; set; }

    /// <summary>
    /// The public key used to encrypt the symmetric key.
    /// </summary>
    [JsonPropertyName("publicKey")]
    public string PublicKey { get; set; } = null!;
}
