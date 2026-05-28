using Fhi.Slash.Public.SlashMessenger.Slash.Models;

namespace Fhi.Slash.Public.SlashMessenger.Slash.Interfaces;

/// <summary>
/// Interface for Slash Service.
/// Implement this interface to customize the behavior of the Slash service, or inject your own implementation.
/// </summary>
public interface ISlashService
{
    /// <summary>
    /// Prepares and sends the message to the Slash API.
    /// </summary>
    /// <param name="rawJsonMessage">The serialized JSON string representing the message to be sent.</param>
    /// <param name="messageType">The type of the message.</param>
    /// <param name="messageVersion">The version of the message.</param>
    /// <param name="parentOrganizationNumber">Optional parent organization number used for multi-tenant token requests to HelseID.</param>
    /// <returns>An <see cref="SendMessageResponse"/> containing the response from the Slash API.</returns>
    public Task<SendMessageResponse> PrepareAndSendMessage(string rawJsonMessage, string messageType, string messageVersion, string? parentOrganizationNumber = null);
}
