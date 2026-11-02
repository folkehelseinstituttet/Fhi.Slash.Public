using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Fhi.Slash.Public.SlashMessenger.Slash.Interfaces;
using Fhi.Slash.Public.SlashMessenger.Slash.Models;
using Fhi.Slash.Public.SlashMessengerCLI.Config;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Fhi.Slash.Public.SlashMessengerCLI.UnitTests;

[TestClass]
public class SlashMessengerCLITests
{
    private readonly AppsettingsConfig _defaultConfig;

    public SlashMessengerCLITests()
    {
        _defaultConfig = new AppsettingsConfig
        {
            HelseIdCertificateThumbprint = string.Empty,
            HelseIdCertificatePath = string.Empty,
            HelseIdCertificatePassword = string.Empty,
            HelseIdOpenIdConfigurationUrl = string.Empty,
            HelseIdClientJsonFilePath = string.Empty,
            SenderExportSoftwareVersion = string.Empty,
            SenderSoftwareVersion = string.Empty,
            SenderSoftwareName = string.Empty,
            SenderVendorName = string.Empty,
            HelseIdClientId = string.Empty,
            SlashBaseUrl = string.Empty,
        };
    }

    [TestMethod]
    public async Task ShouldSendMessage()
    {
        // Arrange
        var slashService = Substitute.For<ISlashService>();
        slashService.PrepareAndSendMessage(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(new SendMessageResponse()
            {
                CorrelationId = Guid.Empty,
                ProcessMessageResponse = new ProcessMessageResponse()
                {
                    Delivered = false,
                    Errors =
                    [
                        new()
                    {
                        ErrorCode = 0,
                        ErrorDetails = "TestDetails 1",
                        ErrorMessage = "TestMessage 1",
                        PropertyName = "TestProperty 1"
                    },
                    new()
                    {
                        ErrorCode = 1,
                        ErrorDetails = "TestDetails 2",
                        ErrorMessage = "TestMessage 2",
                        PropertyName = "TestProperty 2"
                    }
                    ]
                },
                ResposeMessage = new HttpResponseMessage()
            });

        using var testHost = new HostBuilder()
            .ConfigureServices((ctx, services) =>
            {
                services.AddTransient(_ => slashService);
            })
            .Build();

        var msgType = "testType";
        var msgVersion = "testVersion";
        var fileContent = "Test message content";

        // Act
        await Program.ExecuteWithMessage(testHost, fileContent, msgType, msgVersion);

        // Assert
        await slashService.Received(1).PrepareAndSendMessage(fileContent, msgType, msgVersion);
    }

    [TestMethod]
    public async Task ShouldSendMessageWithParentOrganizationNumber()
    {
        // Arrange
        var parentOrganizationNumber = "123456789";
        var slashService = Substitute.For<ISlashService>();
        slashService.PrepareAndSendMessage(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>())
            .Returns(new SendMessageResponse()
            {
                CorrelationId = Guid.Empty,
                ProcessMessageResponse = new ProcessMessageResponse()
                {
                    Delivered = true,
                    Errors = []
                },
                ResposeMessage = new HttpResponseMessage()
            });

        using var testHost = new HostBuilder()
            .ConfigureServices((ctx, services) =>
            {
                services.AddTransient(_ => slashService);
            })
            .Build();

        var msgType = "testType";
        var msgVersion = "testVersion";
        var fileContent = "Test message content";

        // Act
        await Program.ExecuteWithMessage(testHost, fileContent, msgType, msgVersion, parentOrganizationNumber: parentOrganizationNumber);

        // Assert
        await slashService.Received(1).PrepareAndSendMessage(fileContent, msgType, msgVersion, parentOrganizationNumber);
    }

    [TestMethod]
    public void ShouldTryToGetCertFromCertStoreIfThumbprintProvided()
    {
        // Arrange
        _defaultConfig.HelseIdCertificateThumbprint = "non-existent-thumbprint";

        // Act & Assert
        var exception = Assert.ThrowsExactly<KeyNotFoundException>(() =>
            Program.GetCertificateByHelseIdConfig(_defaultConfig));

        Assert.AreEqual("No valid certfication was found for: onlyValid=False and FindByThumbprint=non-existent-thumbprint", exception.Message);
    }

    [TestMethod]
    public void ShouldTryToGetCertFromFileIfCertPathProvided_WithoutPassword()
    {
        // Arrange
        var path = CreateTestCertificateFile(null);

        try
        {
            _defaultConfig.HelseIdCertificatePath = path;
            _defaultConfig.HelseIdCertificatePassword = string.Empty;

            // Act
            var cert = Program.GetCertificateByHelseIdConfig(_defaultConfig);

            // Assert
            try
            {
                Assert.IsNotNull(cert);
                Assert.Contains("Test Client", cert.Subject);
            }
            finally
            {
                cert?.Dispose();
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ShouldTryToGetCertFromFileIfCertPathProvided_WithPassword()
    {
        // Arrange
        var path = CreateTestCertificateFile("password");

        try
        {
            _defaultConfig.HelseIdCertificatePath = path;
            _defaultConfig.HelseIdCertificatePassword = "password";

            // Act
            var cert = Program.GetCertificateByHelseIdConfig(_defaultConfig);

            // Assert
            try
            {
                Assert.IsNotNull(cert);
                Assert.Contains("Test Client", cert.Subject);
            }
            finally
            {
                cert?.Dispose();
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ShouldThrowExceptionIfCertNotFoundByPath()
    {
        // Arrange
        _defaultConfig.HelseIdCertificatePath = "non-existent.pfx";

        // Act & Assert
        Assert.ThrowsExactly<CryptographicException>(() =>
            Program.GetCertificateByHelseIdConfig(_defaultConfig));
    }

    [TestMethod]
    public void ShouldThrowExceptionIfCertFoundByPathWithInvalidPassword()
    {
        // Arrange
        var path = CreateTestCertificateFile("password");

        try
        {
            _defaultConfig.HelseIdCertificatePath = path;
            _defaultConfig.HelseIdCertificatePassword = "NOT_VALID_PASSWORD";

            // Act & Assert
            Assert.ThrowsExactly<CryptographicException>(() =>
                Program.GetCertificateByHelseIdConfig(_defaultConfig));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTestCertificateFile(string? password)
    {
        var rsa = RSA.Create(2048);

        try
        {
            var request = new CertificateRequest("CN=Test Client", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));

            var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pfx");

            File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password ?? string.Empty));

            return path;
        }
        finally
        {
            rsa.Dispose();
        }
    }
}