using System.Text;
using Microsoft.Extensions.Configuration;

namespace FOMS.Infrastructure.Configuration;

/// <summary>
/// Dynamic database configuration resolver for Microsoft SQL Server / FOMS database connections.
/// Loads connection strings from environment variables, configuration providers, or discrete parameters
/// with standard fallback options to prevent hardcoded credentials.
/// </summary>
public static class DatabaseConfiguration
{
    public const string DefaultServer = @"localhost\SQLEXPRESS";
    public const string DefaultDatabase = "FOMS_DB";

    /// <summary>
    /// Resolves the database connection string dynamically with precedence:
    /// 1. IConfiguration ConnectionStrings (FomsSqlServer, DefaultConnection)
    /// 2. Direct Environment Variables (ConnectionStrings__FomsSqlServer, MSSQL_CONNECTION_STRING, DATABASE_URL)
    /// 3. Discrete Environment Variables (MSSQL_SERVER, MSSQL_DATABASE, MSSQL_TRUSTED_CONNECTION, etc.)
    /// 4. Standard secure local fallback configuration
    /// </summary>
    public static string GetConnectionString(IConfiguration configuration)
    {
        // 1. Check configuration providers
        var connStr = configuration.GetConnectionString("FomsSqlServer");
        if (string.IsNullOrWhiteSpace(connStr))
            connStr = configuration.GetConnectionString("DefaultConnection");

        // 2. Check full connection string environment variables
        if (string.IsNullOrWhiteSpace(connStr))
            connStr = Environment.GetEnvironmentVariable("ConnectionStrings__FomsSqlServer");
        if (string.IsNullOrWhiteSpace(connStr))
            connStr = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (string.IsNullOrWhiteSpace(connStr))
            connStr = Environment.GetEnvironmentVariable("MSSQL_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connStr))
            connStr = Environment.GetEnvironmentVariable("DATABASE_URL");

        // 3. If no full string found, build dynamically from discrete parameters
        if (string.IsNullOrWhiteSpace(connStr))
        {
            connStr = BuildFromDiscreteParameters(configuration);
        }

        return connStr;
    }

    /// <summary>
    /// Dynamically constructs a SQL Server connection string using individual environment variables or configuration values.
    /// Supports Windows Authentication (Trusted_Connection=yes / True) and SQL Server Authentication.
    /// </summary>
    public static string BuildFromDiscreteParameters(IConfiguration? configuration = null)
    {
        var server = Environment.GetEnvironmentVariable("MSSQL_SERVER")
            ?? configuration?["MSSQL_SERVER"]
            ?? Environment.GetEnvironmentVariable("DB_SERVER")
            ?? DefaultServer;

        var database = Environment.GetEnvironmentVariable("MSSQL_DATABASE")
            ?? Environment.GetEnvironmentVariable("MSSQL_DB")
            ?? configuration?["MSSQL_DATABASE"]
            ?? Environment.GetEnvironmentVariable("DB_NAME")
            ?? DefaultDatabase;

        var trustedConnectionRaw = Environment.GetEnvironmentVariable("MSSQL_TRUSTED_CONNECTION")
            ?? configuration?["MSSQL_TRUSTED_CONNECTION"]
            ?? "yes";

        var isTrusted = string.Equals(trustedConnectionRaw, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trustedConnectionRaw, "true", StringComparison.OrdinalIgnoreCase)
            || trustedConnectionRaw == "1";

        var envName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? configuration?["ASPNETCORE_ENVIRONMENT"]
            ?? "Production";

        var isDevelopment = string.Equals(envName, "Development", StringComparison.OrdinalIgnoreCase);

        var trustServerCertRaw = Environment.GetEnvironmentVariable("MSSQL_TRUST_SERVER_CERTIFICATE")
            ?? configuration?["MSSQL_TRUST_SERVER_CERTIFICATE"]
            ?? (isDevelopment ? "True" : "False");

        var user = Environment.GetEnvironmentVariable("MSSQL_USER") ?? configuration?["MSSQL_USER"];
        var password = Environment.GetEnvironmentVariable("MSSQL_PASSWORD") ?? configuration?["MSSQL_PASSWORD"];
        var port = Environment.GetEnvironmentVariable("MSSQL_PORT") ?? configuration?["MSSQL_PORT"];

        var sb = new StringBuilder();

        // Server and instance / port
        if (!string.IsNullOrWhiteSpace(port) && !server.Contains(','))
        {
            sb.Append($"Server={server},{port};");
        }
        else
        {
            sb.Append($"Server={server};");
        }

        sb.Append($"Database={database};");

        if (isTrusted || string.IsNullOrWhiteSpace(user))
        {
            // Windows Authentication (Trusted_Connection)
            sb.Append("Trusted_Connection=True;");
        }
        else
        {
            // SQL Server Authentication
            sb.Append($"User Id={user};Password={password};");
        }

        sb.Append($"TrustServerCertificate={trustServerCertRaw};");

        return sb.ToString();
    }

    public static bool IsPlaceholderSecret(string? val)
    {
        if (string.IsNullOrWhiteSpace(val)) return false;
        var trimmed = val.Trim();
        string[] forbidden = new[]
        {
            "replace_with",
            "change_me",
            "change-me",
            "your_",
            "example",
            "placeholder",
            "FOMS_Capstone_JWT_SecureKey",
            "dev-secret-key-change-in-production",
            "1234567890"
        };
        return forbidden.Any(f => trimmed.Contains(f, StringComparison.OrdinalIgnoreCase));
    }

    public static void ValidateDatabaseSecrets(IConfiguration configuration)
    {
        var mssqlPass = configuration["MSSQL_PASSWORD"] ?? Environment.GetEnvironmentVariable("MSSQL_PASSWORD");
        if (!string.IsNullOrWhiteSpace(mssqlPass) && IsPlaceholderSecret(mssqlPass))
        {
            throw new InvalidOperationException("Fatal Startup Error: MSSQL_PASSWORD contains a placeholder value.");
        }

        var envName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? configuration?["ASPNETCORE_ENVIRONMENT"]
            ?? "Production";

        var isDevelopment = string.Equals(envName, "Development", StringComparison.OrdinalIgnoreCase);

        var connStr = GetConnectionString(configuration);
        if (!string.IsNullOrWhiteSpace(connStr))
        {
            if (!isDevelopment &&
                (connStr.Contains("TrustServerCertificate=True", StringComparison.OrdinalIgnoreCase) ||
                 connStr.Contains("TrustServerCertificate=yes", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    "Fatal Configuration Error: TrustServerCertificate=True is prohibited in production. " +
                    "Configure a valid CA-signed TLS certificate and set TrustServerCertificate=False.");
            }
            var parts = connStr.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                var kv = part.Split('=', 2);
                if (kv.Length == 2)
                {
                    var k = kv[0].Trim();
                    var v = kv[1].Trim();
                    if ((k.Equals("Password", StringComparison.OrdinalIgnoreCase) || k.Equals("Pwd", StringComparison.OrdinalIgnoreCase))
                        && IsPlaceholderSecret(v))
                    {
                        throw new InvalidOperationException("Fatal Startup Error: Database connection string contains a placeholder password.");
                    }
                }
            }
        }
    }
}
