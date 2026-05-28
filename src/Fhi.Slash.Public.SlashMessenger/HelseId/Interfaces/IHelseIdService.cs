using Microsoft.IdentityModel.Tokens;

namespace Fhi.Slash.Public.SlashMessenger.HelseId.Interfaces;

/// <summary>
/// Defines the interface for the HelseId service.
/// Implement this interface to customize the behavior of the HelseId service, or inject your own implementation.
/// </summary>
public interface IHelseIdService
{
    /// <summary>
    /// Retrieves an access token from HelseID.
    /// </summary>
    /// <param name="dPoPProofJwk">A <see cref="JsonWebKey"/> used to sign DPoP proofs associated with the access token.</param>
    /// <param name="parentOrganizationNumber">Optional parent organization number used for multi-tenant token requests.</param>
    /// <returns>The access token from HelseID as a string.</returns>
    public Task<string> GetAccessToken(JsonWebKey dPoPProofJwk, string? parentOrganizationNumber = null);
}
