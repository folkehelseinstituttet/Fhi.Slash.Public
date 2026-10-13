using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Duende.IdentityModel;
using Microsoft.IdentityModel.Tokens;

namespace Fhi.Slash.Public.SlashMessenger.Extensions;

internal static class JsonWebKeyExtensions
{
    /// <summary>
    /// Extension method for <see cref="JsonWebKey"/> to generate a DPoP proof.
    /// </summary>
    /// <param name="jwk">The <see cref="JsonWebKey"/> used to sign the DPoP proof.</param>
    /// <param name="url">The URL of the resource being accessed.</param>
    /// <param name="httpMethod">The HTTP method (e.g., GET, POST) used in the request.</param>
    /// <param name="dPoPNonce">Optional DPoP nonce.</param>
    /// <param name="accessToken">Optional access token associated with the DPoP Proof.</param>
    /// <param name="customPayloadClaims">Optional custom claims to include in the DPoP proof payload.</param>
    /// <returns>A DPoP proof as a JWT string.</returns>
    public static string CreateDPoPProof(this JsonWebKey jwk,
        string url, 
        string httpMethod, 
        string? dPoPNonce = null, 
        string? accessToken = null,
        Dictionary<string, string>? customPayloadClaims = null)
    {
        var signingCredentials = new SigningCredentials(jwk, algorithm: jwk.Alg ?? SecurityAlgorithms.RsaSha256);

        var newJwk = jwk.Kty switch
        {
            JsonWebAlgorithmsKeyTypes.EllipticCurve => new Dictionary<string, string>
            {
                [JsonWebKeyParameterNames.Kty] = jwk.Kty,
                [JsonWebKeyParameterNames.X] = jwk.X,
                [JsonWebKeyParameterNames.Y] = jwk.Y,
                [JsonWebKeyParameterNames.Crv] = jwk.Crv,
            },
            JsonWebAlgorithmsKeyTypes.RSA => new Dictionary<string, string>
            {
                [JsonWebKeyParameterNames.Kty] = jwk.Kty,
                [JsonWebKeyParameterNames.N] = jwk.N,
                [JsonWebKeyParameterNames.E] = jwk.E,
                [JsonWebKeyParameterNames.Alg] = signingCredentials.Algorithm,
            },
            _ => throw new InvalidOperationException("Invalid key type for DPoP proof.")
        };

        // Header Parameters
        var jwtHeader = new JwtHeader(signingCredentials)
        {
            [JwtClaimTypes.TokenType] = "dpop+jwt",
            [JwtClaimTypes.JsonWebKey] = newJwk
        };

        // Payload Claims
        var payload = new JwtPayload
        {
            [JwtClaimTypes.JwtId] = Guid.NewGuid().ToString(),
            [JwtClaimTypes.DPoPHttpMethod] = httpMethod,
            [JwtClaimTypes.DPoPHttpUrl] = url,
            [JwtClaimTypes.IssuedAt] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };

        foreach (var claim in customPayloadClaims ?? [])
        {
            payload[claim.Key] = claim.Value;
        }

        // Used when accessing the authentication server (HelseID):
        if (!string.IsNullOrEmpty(dPoPNonce))
        {
            // nonce: A recent nonce provided via the DPoP-Nonce HTTP header from HelseID.
            payload[JwtClaimTypes.Nonce] = dPoPNonce;
        }

        // Used when accessing an API that requires a DPoP token:
        if (!string.IsNullOrEmpty(accessToken))
        {
            // ath: hash of the access token. The value MUST be the result of a base64url encoding
            // the SHA-256 [SHS] hash of the ASCII encoding of the associated access token's value.
            var hash = SHA256.HashData(Encoding.ASCII.GetBytes(accessToken));
            var ath = Base64UrlEncoder.Encode(hash);

            payload[JwtClaimTypes.DPoPAccessTokenHash] = ath;
        }

        var jwtSecurityToken = new JwtSecurityToken(jwtHeader, payload);
        return new JwtSecurityTokenHandler().WriteToken(jwtSecurityToken);
    }
}
