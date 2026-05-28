namespace Fhi.Slash.Public.SlashMessenger.HelseId;

internal static class OrganizationNumberTools
{
    private const string OrganizationIdentifierPrefix = "NO:ORGNR:";

    public static string? NormalizeParentOrganizationNumber(string? parentOrganizationNumber)
    {
        if (string.IsNullOrWhiteSpace(parentOrganizationNumber))
        {
            return null;
        }

        var normalizedParentOrganizationNumber = parentOrganizationNumber.Trim();
        if (normalizedParentOrganizationNumber.Length != 9 || normalizedParentOrganizationNumber.Any(c => !char.IsDigit(c)))
        {
            throw new ArgumentException("Parent organization number must consist of exactly 9 digits.", nameof(parentOrganizationNumber));
        }

        return normalizedParentOrganizationNumber;
    }

    public static string ToOrganizationIdentifier(string parentOrganizationNumber)
    {
        var normalizedParentOrganizationNumber = NormalizeParentOrganizationNumber(parentOrganizationNumber);
        if (normalizedParentOrganizationNumber == null)
        {
            throw new ArgumentException("Parent organization number is required.", nameof(parentOrganizationNumber));
        }

        return $"{OrganizationIdentifierPrefix}{normalizedParentOrganizationNumber}";
    }
}
