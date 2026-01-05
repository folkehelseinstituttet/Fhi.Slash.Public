using Microsoft.Extensions.Configuration;
using Fhi.Slash.Public.SlashMessenger.Extensions;
using Fhi.Slash.Public.SlashMessenger.HelseId.Models;
using Fhi.Slash.Public.SlashMessengerCLI.Config;
using IdentityModel.Client;
using Fhi.Slash.Public.SlashMessenger.Slash.Interfaces;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Fhi.Slash.Public.SlashMessengerCLI.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Fhi.Slash.Public.SlashMessengerCLI;

public static class Program
{
    private static IHost? _host;

    // Input Arguments: "*PATH TO MESSAGE FILE*" "*MESSAGE TYPE*" "*MESSAGE VERSION*" ("*DATA EXTRACTION DATE*": optional)
    // Example: "C:\my_message_file.json" "HST_Konsultasjon" "1" "01.01.2024"
    public static async Task Main(string[] args)
    {
        if(args.Length < 3)
        {
            throw new ArgumentException($"Missing arguments. Please provide *Path to message file*, *Message type*, *Message version*, (*Data extraction date*: Optional)\n Example: \"C:\\my_message_file.json\" \"HST_Konsultasjon\" \"1\" \"01.01.2024\"");
        }

        // Handle Arguments
        var messageFilePath = args[0];
        var messageType = args[1];
        var messageVersion = args[2];
        DateTime? dataExtractionDate = args.Length >= 4 ? DateTime.Parse(args[3]) : DateTime.Now;

        ArgumentException.ThrowIfNullOrWhiteSpace(messageFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageVersion);

        // Setup Host
        _host = await SetupHost(dataExtractionDate);

        // Execute
        await Execute(_host, messageFilePath, messageType, messageVersion, dataExtractionDate);
    }

    public static async Task Execute(IHost host, string messageFilePath, string messageType, string messageVersion, DateTime? dataExtractionDate = null)
    {
        // Print Info
        Console.WriteLine($"MessageFilePath: {messageFilePath}");
        Console.WriteLine($"MessageType: {messageType}");
        Console.WriteLine($"MessageVersion: {messageVersion}");
        Console.WriteLine($"DataExtractionDate: {dataExtractionDate?.ToString("dd.MM.yyyy") ?? "Today"}");

        // Load Message File
        var messageFileContent = File.ReadAllText(messageFilePath) ??
            throw new InvalidOperationException($"Could not load message file. File Path: {messageFilePath}");

        // Send Message
        var slashService = host.Services.GetRequiredService<ISlashService>();
        var response = await slashService.PrepareAndSendMessage(messageFileContent, messageType, messageVersion);

        // Print Response
        Console.WriteLine("\nResponse:");
        Console.WriteLine($" CorrelationId: {response.CorrelationId}");
        Console.WriteLine($" Delivered: {response.ProcessMessageResponse.Delivered}");
        Console.WriteLine($" Number of errors: {response.ProcessMessageResponse.Errors?.Count ?? 0}");
        if (response.ProcessMessageResponse.Errors?.Count > 0)
        {
            Console.WriteLine(" Errors:");
            foreach (var error in response.ProcessMessageResponse.Errors)
            {
                Console.WriteLine($"  ErrorCode: {error.ErrorCode}");
                Console.WriteLine($"  PropertyName: {error.PropertyName}");
                Console.WriteLine($"  ErrorMessage: {error.ErrorMessage}");
                Console.WriteLine($"  ErrorDetails: {error.ErrorDetails}");
                Console.WriteLine();
            }
        }
    }

    private static async Task<IHost> SetupHost(DateTime? dataExtractionDate = null)
    {
        var appsettingsConfig = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
            .Build();

        // Get HelseId Discovery Document
        var helseIdOpenIdConfigUrl = appsettingsConfig.GetValue<string>(nameof(AppsettingsConfig.HelseIdOpenIdConfigurationUrl));
        using var httpClient = new HttpClient();
        var disco = await httpClient.GetDiscoveryDocumentAsync(helseIdOpenIdConfigUrl);
        if (string.IsNullOrWhiteSpace(disco?.TokenEndpoint))
        {
            throw new InvalidOperationException("Could not get TokenEndpoint from HelseId Discovery Document");
        }

        return new HostBuilder()
            .ConfigureAppConfiguration((ctx, config) =>
            {
                config.AddConfiguration(appsettingsConfig);
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders(); // Clears default providers if you want to start fresh
                logging.AddConsole();     // Adds console logging
            })
            .ConfigureServices((ctx, services) =>
            {
                var config = ctx.Configuration.Get<AppsettingsConfig>() ??
                    throw new InvalidOperationException("Could not get Appsettings configuration. Ensure that appsettings.json exists and is configured");

                services.AddSlash(slashConfig =>
                {
                    slashConfig.BaseUrl = config.SlashBaseUrl;
                    slashConfig.VendorName = config.SenderVendorName;
                    slashConfig.SoftwareName = config.SenderSoftwareName;
                    slashConfig.SoftwareVersion = config.SenderSoftwareVersion;
                    slashConfig.ExportSoftwareVersion = config.SenderExportSoftwareVersion;
                }, helseIdConfig =>
                {
                    helseIdConfig.TokenEndpoint = disco.TokenEndpoint;
                    helseIdConfig.ClientId = config.HelseIdClientId;
                    helseIdConfig.Certificate = GetCertificateByHelseIdConfig(config);
                    helseIdConfig.ClientDefinition = !string.IsNullOrWhiteSpace(config.HelseIdClientJsonFilePath) ?
                        GetHelseIdClientDefinition(config.HelseIdClientJsonFilePath) : null;
                }, dataExtractionDate);
            })
            .Build();
    }

    public static X509Certificate2? GetCertificateByHelseIdConfig(AppsettingsConfig config) =>
        !string.IsNullOrEmpty(config?.HelseIdCertificateThumbprint) ?
            CertificateTools.GetFromStore(false, (X509FindType.FindByThumbprint, config.HelseIdCertificateThumbprint)) :
        !string.IsNullOrEmpty(config?.HelseIdCertificatePath) ?
            new X509Certificate2(config.HelseIdCertificatePath, config.HelseIdCertificatePassword, X509KeyStorageFlags.Exportable) :
        null;

    public static HelseIdClientDefinition GetHelseIdClientDefinition(string helseIdClientJsonFilePath) =>
         JsonSerializer.Deserialize<HelseIdClientDefinition>(File.ReadAllText(helseIdClientJsonFilePath)) ??
            throw new InvalidOperationException("Could not deserialize HelseId Client file to HelseId Client Definition class");
}
