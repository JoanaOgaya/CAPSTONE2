using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FOMS.Application.Interfaces;
using FOMS.Application.Services;
using FOMS.Domain.Entities;
using FomsAiGateway;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Xunit;

namespace FomsAiGateway.Tests;

public class GatewayJwtValidationTests
{
    private const string ValidSecret = "Test_Super_Secret_Key_For_Jwt_Validation_2026_AtLeast32Chars!";
    private const string Issuer = "FOMS.Api";
    private const string Audience = "FOMS.Api";

    private string GenerateToken(
        string secret,
        string issuer = Issuer,
        string audience = Audience,
        string username = "EMP-001",
        string role = "Finance Manager",
        DateTime? expires = null,
        string? client_id = null)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, username),
            new(ClaimTypes.Role, role),
            new("unique_name", username),
            new("role", role)
        };
        if (!string.IsNullOrEmpty(client_id))
        {
            claims.Add(new("client_id", client_id));
        }

        var exp = expires ?? DateTime.UtcNow.AddMinutes(30);
        var notBefore = exp.AddMinutes(-30);

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = issuer,
            Audience = audience,
            NotBefore = notBefore,
            Expires = exp,
            SigningCredentials = creds
        };

        var token = tokenHandler.CreateToken(descriptor);
        return tokenHandler.WriteToken(token);
    }

    [Fact]
    public void RealAuthServiceToken_PassesValidation_AndMatchesIssuerAudienceAndRole()
    {
        // Issue token using the real backend AuthService
        var inMemoryConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "JWT_SECRET", ValidSecret },
                { "JwtSettings:Secret", ValidSecret },
                { "JwtSettings:Issuer", Issuer },
                { "JwtSettings:Audience", Audience },
                { "JwtSettings:AccessTokenExpirationMinutes", "60" }
            })
            .Build();

        var mockContext = new Mock<IApplicationDbContext>();
        var mockLogger = new Mock<ILogger<AuthService>>();

        var authService = new AuthService(mockContext.Object, inMemoryConfig, mockLogger.Object);

        var employee = new Employee
        {
            Id = "EMP-001",
            Username = "EMP-001",
            Name = "Crystalyn Joyce C. Fajardo",
            Role = "Finance Manager",
            Email = "crystalyn@foms.local"
        };

        var realToken = authService.GenerateJwtToken(employee);
        var authHeader = $"Bearer {realToken}";

        // Validate through the real GatewayJwtValidator
        var result = GatewayJwtValidator.ValidateToken(authHeader, ValidSecret, Issuer, Audience);

        Assert.True(result.IsValid, $"Validation failed: {result.FailureReason}");
        Assert.Equal("EMP-001", result.Username);
        Assert.Equal("Finance Manager", result.Role);
        Assert.Contains("ai.dashboard.view", result.Permissions);
        Assert.Contains("ai.collection.validate", result.Permissions);
    }

    [Fact]
    public void TokenWithDifferentAlgorithm_HS512_FailsValidationBecauseHS256IsPinned()
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var longSecret = "This_Is_A_Very_Long_Secret_Key_For_HS512_Testing_Purposes_2026_AtLeast64BytesLong!";
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(longSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha512);

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[] { new Claim("role", "Finance Manager"), new Claim("name", "EMP-001") }),
            Issuer = Issuer,
            Audience = Audience,
            Expires = DateTime.UtcNow.AddMinutes(30),
            SigningCredentials = creds
        };

        var token = tokenHandler.CreateToken(descriptor);
        var tokenString = tokenHandler.WriteToken(token);
        var authHeader = $"Bearer {tokenString}";

        var result = GatewayJwtValidator.ValidateToken(authHeader, longSecret, Issuer, Audience);
        Assert.False(result.IsValid);
        Assert.True(result.FailureReason?.Contains("signature", StringComparison.OrdinalIgnoreCase) == true ||
                    result.FailureReason?.Contains("algorithm", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void ValidToken_PassesValidation_AndExtractsRoleAndPermissions()
    {
        var token = GenerateToken(ValidSecret, username: "EMP-001", role: "Finance Manager");
        var authHeader = $"Bearer {token}";

        var result = GatewayJwtValidator.ValidateToken(authHeader, ValidSecret, Issuer, Audience);

        Assert.True(result.IsValid);
        Assert.Equal("EMP-001", result.Username);
        Assert.Equal("Finance Manager", result.Role);
        Assert.Contains("ai.dashboard.view", result.Permissions);
        Assert.Contains("ai.collection.validate", result.Permissions);
    }

    [Fact]
    public void ExpiredToken_FailsValidation()
    {
        var token = GenerateToken(ValidSecret, expires: DateTime.UtcNow.AddMinutes(-10));
        var authHeader = $"Bearer {token}";

        var result = GatewayJwtValidator.ValidateToken(authHeader, ValidSecret, Issuer, Audience);

        Assert.False(result.IsValid);
        Assert.Equal("Anonymous", result.Role);
        Assert.Contains("expired", result.FailureReason?.ToLowerInvariant() ?? "");
    }

    [Fact]
    public void BadSignature_FailsValidation()
    {
        var token = GenerateToken("Another_Wrong_Secret_Key_That_Is_Also_32_Chars_Long!", username: "EMP-001", role: "Finance Manager");
        var authHeader = $"Bearer {token}";

        var result = GatewayJwtValidator.ValidateToken(authHeader, ValidSecret, Issuer, Audience);

        Assert.False(result.IsValid);
        Assert.Equal("Anonymous", result.Role);
        Assert.Contains("signature", result.FailureReason?.ToLowerInvariant() ?? "");
    }

    [Fact]
    public void WrongIssuer_FailsValidation()
    {
        var token = GenerateToken(ValidSecret, issuer: "Untrusted.Issuer.Org");
        var authHeader = $"Bearer {token}";

        var result = GatewayJwtValidator.ValidateToken(authHeader, ValidSecret, Issuer, Audience);

        Assert.False(result.IsValid);
        Assert.Contains("issuer", result.FailureReason?.ToLowerInvariant() ?? "");
    }

    [Fact]
    public void WrongAudience_FailsValidation()
    {
        var token = GenerateToken(ValidSecret, audience: "WrongAudienceService");
        var authHeader = $"Bearer {token}";

        var result = GatewayJwtValidator.ValidateToken(authHeader, ValidSecret, Issuer, Audience);

        Assert.False(result.IsValid);
        Assert.Contains("audience", result.FailureReason?.ToLowerInvariant() ?? "");
    }

    [Fact]
    public void MissingOrMalformedHeader_FailsValidation()
    {
        var result1 = GatewayJwtValidator.ValidateToken(null, ValidSecret, Issuer, Audience);
        Assert.False(result1.IsValid);
        Assert.Equal("Anonymous", result1.Role);

        var result2 = GatewayJwtValidator.ValidateToken("InvalidFormatWithoutBearer", ValidSecret, Issuer, Audience);
        Assert.False(result2.IsValid);
    }

    [Fact]
    public void TamperedRoleClaim_FailsValidation()
    {
        var validToken = GenerateToken(ValidSecret, username: "client1", role: "Client");

        var parts = validToken.Split('.');
        Assert.Equal(3, parts.Length);

        var payloadBytes = Convert.FromBase64String(parts[1].PadRight(parts[1].Length + (4 - parts[1].Length % 4) % 4, '='));
        var payloadJson = Encoding.UTF8.GetString(payloadBytes);

        var tamperedPayloadJson = payloadJson.Replace("Client", "Finance Manager");
        var tamperedPayloadBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(tamperedPayloadJson))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var tamperedToken = $"{parts[0]}.{tamperedPayloadBase64}.{parts[2]}";
        var authHeader = $"Bearer {tamperedToken}";

        var result = GatewayJwtValidator.ValidateToken(authHeader, ValidSecret, Issuer, Audience);

        Assert.False(result.IsValid);
        Assert.Equal("Anonymous", result.Role);
    }

    [Fact]
    public void DevMockTokens_BlockedInProductionOrWhenDisabled()
    {
        var authHeader = "Bearer fm-token";

        var prodResult = GatewayJwtValidator.ValidateToken(authHeader, ValidSecret, Issuer, Audience, isDevMode: false, enableDevMockTokens: false);
        Assert.False(prodResult.IsValid);

        var devDisabledResult = GatewayJwtValidator.ValidateToken(authHeader, ValidSecret, Issuer, Audience, isDevMode: true, enableDevMockTokens: false);
        Assert.False(devDisabledResult.IsValid);
    }

    [Fact]
    public void DevMockTokens_AllowedOnlyWhenDevAndFlagExplicitlyEnabled()
    {
        var authHeader = "Bearer fm-token";
        var devEnabledResult = GatewayJwtValidator.ValidateToken(authHeader, ValidSecret, Issuer, Audience, isDevMode: true, enableDevMockTokens: true);
        
        Assert.True(devEnabledResult.IsValid);
        Assert.Equal("Finance Manager", devEnabledResult.Role);
        Assert.Equal("financial_manager_user", devEnabledResult.Username);
    }

    [Theory]
    [InlineData("replace_with_a_secure_random_jwt_secret_at_least_32_characters")]
    [InlineData("change_me_super_secret_jwt_string_12345")]
    [InlineData("change-me-jwt-secret-string-at-least-32-chars-long")]
    [InlineData("your_jwt_secret_key_needs_to_be_32_chars_long")]
    [InlineData("FOMS_Capstone_JWT_SecureKey_2026_DevelopmentOnly")]
    [InlineData("dev-secret-key-change-in-production-long-enough")]
    [InlineData("12345678901234567890123456789012")]
    public void IsPlaceholderSecret_DetectsPlaceholders(string candidate)
    {
        Assert.True(GatewayJwtValidator.IsPlaceholderSecret(candidate));
    }

    [Fact]
    public void IsPlaceholderSecret_AcceptsCryptographicallySecureSecret()
    {
        Assert.False(GatewayJwtValidator.IsPlaceholderSecret("f8934b1a78e22c95d01249bcf281a941efb3490ad671234081c798e21a7c"));
    }

    [Theory]
    [InlineData("Finance Manager", "duplicates/scan", "POST", false)]
    [InlineData("Head Accountant", "duplicates/scan", "POST", false)]
    [InlineData("Finance Manager", "duplicates/validate", "POST", false)]
    [InlineData("Head Accountant", "duplicates/validate", "POST", false)]
    [InlineData("Finance Manager", "documents/scan", "POST", false)]
    [InlineData("Head Accountant", "documents/scan", "POST", false)]
    [InlineData("Finance Manager", "ocr", "POST", false)]
    [InlineData("Head Accountant", "ocr", "POST", false)]
    [InlineData("Accountant", "duplicates/scan", "POST", true)]
    [InlineData("Assistant of Finance Manager", "duplicates/scan", "POST", true)]
    [InlineData("Accountant", "documents/scan", "POST", true)]
    [InlineData("Assistant of Finance Manager", "documents/scan", "POST", true)]
    [InlineData("Accountant", "duplicates/FLAG-001/review", "POST", false)]
    [InlineData("Assistant of Finance Manager", "duplicates/FLAG-001/review", "POST", false)]
    [InlineData("Finance Manager", "duplicates/FLAG-001/review", "POST", true)]
    [InlineData("Head Accountant", "duplicates/FLAG-001/review", "POST", true)]
    public void HasRoutePermission_EnforcesPhase5RoleMatrix(string role, string route, string method, bool expectedAllowed)
    {
        var token = GenerateToken(ValidSecret, username: "EMP-TEST", role: role);
        var result = GatewayJwtValidator.ValidateToken($"Bearer {token}", ValidSecret, Issuer, Audience);
        Assert.True(result.IsValid);

        var allowed = GatewayJwtValidator.HasRoutePermission(result.Role, result.Permissions, route, method);
        Assert.Equal(expectedAllowed, allowed);
    }

    [Theory]
    [InlineData("Accountant", "recommendations/1/decision", "POST", false)]
    [InlineData("Assistant of Finance Manager", "recommendations/1/decision", "POST", false)]
    [InlineData("Finance Manager", "recommendations/1/decision", "POST", true)]
    [InlineData("Head Accountant", "recommendations/1/decision", "POST", true)]
    public void HasRoutePermission_EnforcesRecommendationDecideRoles(string role, string route, string method, bool expectedAllowed)
    {
        var token = GenerateToken(ValidSecret, username: "EMP-TEST", role: role);
        var result = GatewayJwtValidator.ValidateToken($"Bearer {token}", ValidSecret, Issuer, Audience);
        Assert.True(result.IsValid);

        var allowed = GatewayJwtValidator.HasRoutePermission(result.Role, result.Permissions, route, method);
        Assert.Equal(expectedAllowed, allowed);
    }

    [Fact]
    public void ClientSuppliedXUserRoleHeader_CannotPassThroughGatewayOrEscalatePrivileges()
    {
        // An Accountant (EMP-003) sends a valid Accountant JWT, but attempts to spoof X-User-Role: Finance Manager
        var accountantToken = GenerateToken(ValidSecret, username: "EMP-003", role: "Accountant");
        var authResult = GatewayJwtValidator.ValidateToken($"Bearer {accountantToken}", ValidSecret, Issuer, Audience);
        Assert.True(authResult.IsValid);
        Assert.Equal("Accountant", authResult.Role);

        // 1. Gateway RBAC evaluates the JWT role (Accountant), NOT any client-supplied X-User-Role header
        Assert.False(GatewayJwtValidator.HasRoutePermission(authResult.Role, authResult.Permissions, "duplicates/101/review", "POST"));
        Assert.False(GatewayJwtValidator.HasRoutePermission(authResult.Role, authResult.Permissions, "recommendations/101/decision", "POST"));

        // 2. Even if an outgoing proxy HttpRequestMessage had a pre-populated/spoofed X-User-Role header,
        // ApplyVerifiedIdentityHeaders strips it and overwrites it with the verified JWT role ("Accountant")
        using var proxyRequest = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, "api/ai/duplicates");
        proxyRequest.Headers.Add("X-User-Role", "Finance Manager");
        proxyRequest.Headers.Add("X-User-Username", "EMP-001");
        proxyRequest.Headers.Add("X-API-Key", "spoofed-client-key");

        GatewayJwtValidator.ApplyVerifiedIdentityHeaders(proxyRequest, authResult, "trusted-gateway-service-key");

        Assert.Equal(new[] { "Accountant" }, proxyRequest.Headers.GetValues("X-User-Role"));
        Assert.Equal(new[] { "EMP-003" }, proxyRequest.Headers.GetValues("X-User-Username"));
        Assert.Equal(new[] { "trusted-gateway-service-key" }, proxyRequest.Headers.GetValues("X-API-Key"));
    }
}