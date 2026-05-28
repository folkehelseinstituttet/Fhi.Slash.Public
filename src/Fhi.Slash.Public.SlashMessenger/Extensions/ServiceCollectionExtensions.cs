using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Fhi.Slash.Public.SlashMessenger.HelseId;
using Fhi.Slash.Public.SlashMessenger.HelseId.Interfaces;
using Fhi.Slash.Public.SlashMessenger.HelseId.Models;
using Fhi.Slash.Public.SlashMessenger.Slash;
using Fhi.Slash.Public.SlashMessenger.Slash.Interfaces;
using Fhi.Slash.Public.SlashMessenger.Slash.Models;
using System.Security.Cryptography.X509Certificates;

namespace Fhi.Slash.Public.SlashMessenger.Extensions;

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// The key for the HelseId JWK in the service collection.
    /// </summary>
    public const string helseIdJwkKey = "slash_helseIdJwk";

    /// <summary>
    /// The key for the DPoP Proof JWK in the service collection.
    /// </summary>
    public const string dPoPProofJwkKey = "slash_dPoPProofJwk";

    /// <summary>
    /// Registers Slash and HelseID services in the service collection.
    ///
    /// If <paramref name="dataExtractionDate"/> is not provided, the current date will be used as "Data Extraction Date".
    ///
    /// To customize the default behavior, add your own implementations of Clients and Services before calling this method.
    /// </summary>
    /// <param name="services">The service collection to register the services.</param>
    /// <param name="configureSlashConfig">A configuration delegate for Slash settings.</param>
    /// <param name="configureHelseIdConfig">A configuration delegate for HelseID settings.</param>
    /// <param name="dataExtractionDate">The date for data extraction (optional, defaults to the current date).</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddSlash(this IServiceCollection services, Action<SlashConfig> configureSlashConfig, Action<HelseIdConfig> configureHelseIdConfig, DateTime? dataExtractionDate = null)
    {
        // Setup config
        var helseIdConfig = new HelseIdConfig();
        var slashConfig = new SlashConfig();

        configureHelseIdConfig(helseIdConfig);
        configureSlashConfig(slashConfig);

        helseIdConfig.Validate();
        slashConfig.Validate();

        // Add MemoryCache
        services.AddMemoryCache();

        // Add JWKs for HelseId and DPoP
        JsonWebKey helseIdJwk;
        if (helseIdConfig.Certificate != null)
        {
            ValidateCertificate(helseIdConfig.Certificate);

            helseIdJwk = JsonWebKeyConverter.ConvertFromX509SecurityKey(new X509SecurityKey(helseIdConfig.Certificate), true);
            helseIdJwk.Alg = SecurityAlgorithms.RsaSha256;
        }
        else if (helseIdConfig.ClientDefinition != null)
        {
            helseIdJwk = new JsonWebKey(helseIdConfig.ClientDefinition.PrivateJwk);
        }
        else
        {
            throw new InvalidOperationException("Could not create JWK for DPoP. Missing Certificate or HelseId Client Definition");
        }
        services.TryAddKeyedSingleton(helseIdJwkKey, helseIdJwk);
        services.TryAddKeyedSingleton(dPoPProofJwkKey, helseIdJwk); // Default use the same JWK as HelseId for DPoP

        // Add HelseId
        services.AddSingleton(helseIdConfig);
        services.TryAddTransient<IHelseIdClient, DefaultHelseIdClient>();
        services.TryAddTransient<IHelseIdService, DefaultHelseIdService>();
        services.TryAddHttpClient(helseIdConfig.BasicClientName);

        // Add Slash
        services.AddSingleton(slashConfig);
        services.TryAddTransient<ISlashClient, DefaultSlashClient>();
        services.TryAddTransient<ISlashService, DefaultSlashService>();

        var slashBaseUri = new Uri(slashConfig.BaseUrl.TrimEnd('/') + "/");
        services.TryAddHttpClient(slashConfig.BasicClientName, config => config.BaseAddress = slashBaseUri);
        services.TryAddHttpClient(slashConfig.DPoPClientName, config =>
        {
            config.BaseAddress = slashBaseUri;
            config.DefaultRequestHeaders.Add("x-vendor-name", slashConfig.VendorName);
            config.DefaultRequestHeaders.Add("x-software-name", slashConfig.SoftwareName);
            config.DefaultRequestHeaders.Add("x-software-version", slashConfig.SoftwareVersion);
            config.DefaultRequestHeaders.Add("x-export-software-version", slashConfig.ExportSoftwareVersion);
            config.DefaultRequestHeaders.Add("x-data-extraction-date", (dataExtractionDate ?? DateTime.Now).ToString("dd.MM.yyyy"));
        });

        return services;
    }

    /// <summary>
    /// Validates the provided certificate.
    /// </summary>
    /// <param name="certificate">The certificate to validate.</param>
    /// <exception cref="InvalidOperationException">Thrown if the certificate is invalid.</exception>
    private static void ValidateCertificate(X509Certificate2 certificate)
    {
        if (certificate.GetRSAPublicKey() == null)
        {
            throw new InvalidOperationException("The certificate does not contain an RSA key and cannot support RS256.");
        }

        var keyUsageExtension = certificate.Extensions
            .OfType<X509KeyUsageExtension>()
            .FirstOrDefault();

        if (keyUsageExtension == null || !keyUsageExtension.KeyUsages.HasFlag(X509KeyUsageFlags.DigitalSignature))
        {
            throw new InvalidOperationException("Certificate does not support digital signatures and cannot be used for RS256.");
        }

        if (certificate.SignatureAlgorithm.FriendlyName != "sha256RSA")
        {
            throw new InvalidOperationException("Certificate does not use sha256RSA as the signature algorithm.");
        }
    }

    private static IHttpClientBuilder? TryAddHttpClient(this IServiceCollection services, string name, Action<HttpClient>? configureClient = null)
    {
        // Check if the client already exists
        if (services.Any(s =>
            s.ServiceType == typeof(IConfigureOptions<HttpClientFactoryOptions>) &&
            s.ImplementationInstance is ConfigureNamedOptions<HttpClientFactoryOptions> options &&
            name.Equals(options.Name)))
        {
            return null;
        }

        return configureClient == null ? 
            services.AddHttpClient(name) :
            services.AddHttpClient(name, configureClient);
    }
}
