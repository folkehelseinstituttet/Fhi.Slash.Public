using IdentityModel.Client;
using Microsoft.IdentityModel.Tokens;

namespace Fhi.Slash.Public.SlashMessenger.HelseId.Interfaces;

/// <summary>
/// Defines the interface for the HelseId client.
/// Implement this interface to customize the behavior of the HelseId client, or inject your own implementation.
/// </summary>
public interface IHelseIdClient
{
    /// <summary>
    /// Retrieves an access token from HelseID.
    /// </summary>
    /// <param name="dPoPProofJwk">A <see cref="JsonWebKey"/> used to sign DPoP proofs associated with the access token.</param>
    /// <param name="parentOrganizationNumber">Optional parent organization number used for multi-tenant token requests.</param>
    /// <returns>A <see cref="TokenResponse"/> containing the access token from HelseID.</returns>
    public Task<TokenResponse> GetAccessToken(JsonWebKey dPoPProofJwk, string? parentOrganizationNumber = null);
}
