using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace FomsAiGateway;

public record TokenValidationResult(
    bool IsValid,
    string Username,
    string Role,
    string[] Permissions,
    string ClientId,
    string AccountType,
    string PasswordVersion,
    string? FailureReason = null
);

public static class GatewayJwtValidator
{
    public static readonly Dictionary<string, string[]> RolePermissionsMatrix = new()
    {
        { "Finance Manager", new[] {
            "ai.dashboard.view", "ai.duplicate.view", "ai.duplicate.review",
            "ai.collection.view", "ai.collection.generate", "ai.collection.validate", 
            "ai.recommendation.view", "ai.recommendation.decide", "ai.recommendation.export",
            "ai.reports.view", "ai.reports.export", "ai.audit.view", "ai.system.health.view"
        }},
        { "Head Accountant", new[] {
            "ai.dashboard.view", "ai.duplicate.view", "ai.duplicate.review",
            "ai.collection.view", "ai.collection.generate", "ai.collection.validate", "ai.recommendation.view", 
            "ai.recommendation.decide", "ai.reports.view", "ai.audit.view_limited"
        }},
        { "Accountant", new[] {
            "ai.dashboard.view", "ai.duplicate.view", "ai.duplicate.scan",
            "ai.collection.view", "ai.collection.generate", "ai.recommendation.view", 
            "ai.reports.view", "ai.audit.view_limited"
        }},
        { "Coordinator", new[] {
            "ai.dashboard.view_limited", "ai.duplicate.waybill.view", "ai.duplicate.view", "ai.duplicate.scan"
        }},
        { "Assistant of Finance Manager", new[] {
            "ai.dashboard.view_limited", "ai.reports.view_limited", "ai.audit.view_limited", "ai.duplicate.view", "ai.duplicate.scan"
        }},
        { "Client", Array.Empty<string>() }
    };

    public static TokenValidationResult ValidateToken(
        string? authHeader,
        string jwtSecret,
        string jwtIssuer = "FOMS.Api",
        string jwtAudience = "FOMS.Api",
        bool isDevMode = false,
        bool enableDevMockTokens = false)
    {
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return new TokenValidationResult(false, "anonymous", "Anonymous", Array.Empty<string>(), "", "", "", "Missing or malformed Authorization header.");
        }

        var token = authHeader.Substring(7).Trim();

        // Developer mock tokens allowed ONLY in development when explicitly enabled
        if (isDevMode && enableDevMockTokens)
        {
            if (token == "fm-token") return new(true, "financial_manager_user", "Finance Manager", RolePermissionsMatrix["Finance Manager"], "", "Staff", "2");
            if (token == "accountant-token") return new(true, "head_accountant_user", "Head Accountant", RolePermissionsMatrix["Head Accountant"], "", "Staff", "2");
            if (token == "staff-accountant-token") return new(true, "accountant_user", "Accountant", RolePermissionsMatrix["Accountant"], "", "Staff", "2");
            if (token == "coordinator-token") return new(true, "coordinator_user", "Coordinator", RolePermissionsMatrix["Coordinator"], "", "Staff", "2");
            if (token == "assistant-token") return new(true, "assistant_fm_user", "Assistant of Finance Manager", RolePermissionsMatrix["Assistant of Finance Manager"], "", "Staff", "2");
            if (token == "client-token") return new(true, "client_user", "Client", RolePermissionsMatrix["Client"], "JD-001", "Client", "2");
        }

        if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Length < 32)
        {
            return new TokenValidationResult(false, "anonymous", "Anonymous", Array.Empty<string>(), "", "", "", "JWT Secret configuration is missing or too short.");
        }

        var handler = new JwtSecurityTokenHandler();
        if (!handler.CanReadToken(token))
        {
            return new TokenValidationResult(false, "anonymous", "Anonymous", Array.Empty<string>(), "", "", "", "Token is not a valid JWT format.");
        }

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        try
        {
            var principal = handler.ValidateToken(token, validationParameters, out var validatedToken);

            if (validatedToken is not JwtSecurityToken jwtToken ||
                !jwtToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.OrdinalIgnoreCase))
            {
                return new TokenValidationResult(false, "anonymous", "Anonymous", Array.Empty<string>(), "", "", "", "Invalid token algorithm or signing format.");
            }

            var username = principal.FindFirst(ClaimTypes.Name)?.Value
                ?? principal.FindFirst("unique_name")?.Value
                ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? principal.FindFirst("sub")?.Value
                ?? principal.FindFirst("name")?.Value
                ?? "unknown";

            var role = principal.FindFirst("role")?.Value
                ?? principal.FindFirst(ClaimTypes.Role)?.Value
                ?? "Client";

            var clientId = principal.FindFirst("client_id")?.Value ?? "";
            var accountType = principal.FindFirst("account_type")?.Value ?? "";
            var passwordVersion = principal.FindFirst("password_version")?.Value
                ?? principal.FindFirst("PasswordVersion")?.Value ?? "";

            var decodedPermissions = principal.FindAll("permissions").Select(p => p.Value).Where(v => !string.IsNullOrEmpty(v)).ToList();
            string[] finalPermissions = decodedPermissions.Count > 0
                ? decodedPermissions.ToArray()
                : (RolePermissionsMatrix.TryGetValue(role, out var mapVal) ? mapVal : Array.Empty<string>());

            return new TokenValidationResult(true, username, role, finalPermissions, clientId, accountType, passwordVersion);
        }
        catch (SecurityTokenExpiredException)
        {
            return new TokenValidationResult(false, "anonymous", "Anonymous", Array.Empty<string>(), "", "", "", "Token has expired.");
        }
        catch (SecurityTokenInvalidSignatureException)
        {
            return new TokenValidationResult(false, "anonymous", "Anonymous", Array.Empty<string>(), "", "", "", "Invalid token signature.");
        }
        catch (SecurityTokenInvalidIssuerException)
        {
            return new TokenValidationResult(false, "anonymous", "Anonymous", Array.Empty<string>(), "", "", "", "Invalid token issuer.");
        }
        catch (SecurityTokenInvalidAudienceException)
        {
            return new TokenValidationResult(false, "anonymous", "Anonymous", Array.Empty<string>(), "", "", "", "Invalid token audience.");
        }
        catch (Exception ex)
        {
            return new TokenValidationResult(false, "anonymous", "Anonymous", Array.Empty<string>(), "", "", "", $"Token validation failed: {ex.Message}");
        }
    }

    public static bool IsPlaceholderSecret(string? val)
    {
        if (string.IsNullOrWhiteSpace(val)) return true;
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

    public static string ResolveRequiredPermission(string pathLower, string httpMethod = "GET")
    {
        pathLower = (pathLower ?? "").Trim('/').ToLowerInvariant();
        httpMethod = (httpMethod ?? "GET").ToUpperInvariant();

        if (pathLower.StartsWith("dashboard/summary") ||
            pathLower.StartsWith("dashboard/recent-activity") ||
            pathLower.StartsWith("dashboard/trends") ||
            pathLower.StartsWith("trends"))
        {
            return "ai.dashboard.view";
        }

        if (pathLower.StartsWith("dashboard/attention-accounts") ||
            pathLower.StartsWith("collection-priorities") ||
            pathLower.StartsWith("collection-recommendations") ||
            pathLower.StartsWith("collection/priorities") ||
            pathLower.StartsWith("collection/readiness") ||
            pathLower.StartsWith("collection/recommendations"))
        {
            if (pathLower.EndsWith("review") && httpMethod == "POST")
            {
                return "ai.recommendation.decide";
            }
            if (pathLower.Contains("generate") && httpMethod == "POST")
            {
                return "ai.collection.generate";
            }
            return "ai.collection.view";
        }

        if (pathLower.StartsWith("recommendations"))
        {
            if (pathLower.Contains("decision") && httpMethod == "POST")
            {
                return "ai.recommendation.decide";
            }
            if (pathLower.Contains("summary") || pathLower.Contains("export"))
            {
                return "ai.reports.view";
            }
            return "ai.recommendation.view";
        }

        if (pathLower.StartsWith("duplicates"))
        {
            if (pathLower.Contains("scan") ||
                pathLower.Contains("validate") ||
                pathLower.Contains("save-unique") ||
                pathLower.Contains("ocr") ||
                pathLower.Contains("document-extraction"))
            {
                return "ai.duplicate.scan";
            }
            if ((pathLower.Contains("review") && httpMethod == "POST") || pathLower.Contains("run"))
            {
                return "ai.duplicate.review";
            }
            return "ai.duplicate.view";
        }

        if (pathLower.StartsWith("documents") ||
            pathLower.StartsWith("ocr") ||
            pathLower.StartsWith("document-extraction"))
        {
            return "ai.duplicate.scan";
        }

        if (pathLower.StartsWith("collection-reports") || pathLower.StartsWith("reports"))
        {
            return "ai.reports.view";
        }

        if (pathLower.StartsWith("review-history") || pathLower.StartsWith("audit-trail"))
        {
            return "ai.audit.view";
        }

        return "";
    }

    public static bool HasRoutePermission(string role, string[] permissions, string pathLower, string httpMethod = "GET")
    {
        if (string.Equals(role, "Client", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string requiredPermission = ResolveRequiredPermission(pathLower, httpMethod);
        if (string.IsNullOrEmpty(requiredPermission))
        {
            return true;
        }

        if (permissions.Contains(requiredPermission))
        {
            return true;
        }

        if (requiredPermission == "ai.dashboard.view" && permissions.Contains("ai.dashboard.view_limited"))
        {
            return true;
        }
        if (requiredPermission == "ai.duplicate.view" && permissions.Contains("ai.duplicate.waybill.view"))
        {
            return true;
        }
        if (requiredPermission == "ai.reports.view" && permissions.Contains("ai.reports.view_limited"))
        {
            return true;
        }
        if (requiredPermission == "ai.audit.view" && (permissions.Contains("ai.audit.view_limited") || permissions.Contains("ai.reports.view")))
        {
            return true;
        }

        return false;
    }

    public static void ApplyVerifiedIdentityHeaders(
        System.Net.Http.HttpRequestMessage proxyRequest,
        TokenValidationResult authResult,
        string aiServiceApiKey)
    {
        // Strip any client-supplied identity or service headers to prevent spoofing
        proxyRequest.Headers.Remove("X-API-Key");
        proxyRequest.Headers.Remove("X-User-Username");
        proxyRequest.Headers.Remove("X-User-Role");
        proxyRequest.Headers.Remove("X-Client-Id");
        proxyRequest.Headers.Remove("X-Account-Type");
        proxyRequest.Headers.Remove("X-Password-Version");

        proxyRequest.Headers.Add("X-API-Key", aiServiceApiKey);
        proxyRequest.Headers.Add("X-User-Username", authResult.Username);
        proxyRequest.Headers.Add("X-User-Role", authResult.Role);
        proxyRequest.Headers.Add("X-Client-Id", authResult.ClientId);
        proxyRequest.Headers.Add("X-Account-Type", authResult.AccountType);
        proxyRequest.Headers.Add("X-Password-Version", authResult.PasswordVersion);
    }
}