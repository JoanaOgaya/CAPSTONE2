using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using FOMS.Infrastructure.Configuration;
using Xunit;

namespace FOMS.Tests;

public class DatabaseSecretsValidationTests
{
    [Theory]
    [InlineData("replace_with_your_db_password")]
    [InlineData("change_me_12345")]
    [InlineData("your_secret_password")]
    [InlineData("dev-secret-key-change-in-production")]
    public void ValidateDatabaseSecrets_WhenMssqlPasswordIsPlaceholder_ThrowsInvalidOperationException(string placeholder)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "MSSQL_PASSWORD", placeholder }
            })
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            DatabaseConfiguration.ValidateDatabaseSecrets(config));

        Assert.Contains("placeholder", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Server=localhost;Database=FOMS_DB;User Id=sa;Password=replace_with_prod_password;")]
    [InlineData("Server=sql.prod;Database=FOMS;User Id=foms_user;Pwd=change_me_secure_pass;")]
    public void ValidateDatabaseSecrets_WhenConnectionStringHasPlaceholderPassword_ThrowsInvalidOperationException(string connStr)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ConnectionStrings:FomsSqlServer", connStr }
            })
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            DatabaseConfiguration.ValidateDatabaseSecrets(config));

        Assert.Contains("placeholder", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateDatabaseSecrets_WhenSecurePasswordConfigured_DoesNotThrow()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ConnectionStrings:FomsSqlServer", "Server=localhost;Database=FOMS_DB;User Id=sa;Password=K9#vL2$mP8!xQ4@zR7;" }
            })
            .Build();

        // Should not throw
        DatabaseConfiguration.ValidateDatabaseSecrets(config);
    }

    [Fact]
    public void ValidateDatabaseSecrets_WhenTrustedConnection_DoesNotThrow()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ASPNETCORE_ENVIRONMENT", "Development" },
                { "ConnectionStrings:FomsSqlServer", "Server=localhost\\SQLEXPRESS;Database=FOMS_DB;Trusted_Connection=True;TrustServerCertificate=True;" }
            })
            .Build();

        // Should not throw in development
        DatabaseConfiguration.ValidateDatabaseSecrets(config);
    }

    [Fact]
    public void ValidateDatabaseSecrets_InProduction_WhenTrustServerCertificateTrue_ThrowsInvalidOperationException()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ASPNETCORE_ENVIRONMENT", "Production" },
                { "ConnectionStrings:FomsSqlServer", "Server=prod.sql.azure.com;Database=FOMS_DB;User Id=foms;Password=P@ssw0rd123456789;TrustServerCertificate=True;" }
            })
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            DatabaseConfiguration.ValidateDatabaseSecrets(config));

        Assert.Contains("TrustServerCertificate=True is prohibited in production", ex.Message);
    }

    [Fact]
    public void ValidateDatabaseSecrets_InProduction_WhenTrustServerCertificateFalse_DoesNotThrow()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ASPNETCORE_ENVIRONMENT", "Production" },
                { "ConnectionStrings:FomsSqlServer", "Server=prod.sql.azure.com;Database=FOMS_DB;User Id=foms;Password=P@ssw0rd123456789;TrustServerCertificate=False;Encrypt=True;" }
            })
            .Build();

        // Should not throw in production with validated certificate
        DatabaseConfiguration.ValidateDatabaseSecrets(config);
    }
}
