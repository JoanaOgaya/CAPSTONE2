using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using FomsAiGateway;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Security Check: Fail startup if critical secrets are missing
var jwtSecret = builder.Configuration["JWT_SECRET"]
    ?? builder.Configuration["JwtSettings:Secret"];

static bool IsPlaceholderSecret(string? val)
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

if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Length < 32 || IsPlaceholderSecret(jwtSecret))
{
    throw new InvalidOperationException("Fatal Startup Error: JWT_SECRET is missing, too short (minimum 32 characters required for HMAC-SHA256), or contains a placeholder value.");
}

var jwtIssuer = builder.Configuration["JWT_ISSUER"]
    ?? builder.Configuration["JwtSettings:Issuer"]
    ?? "FOMS.Api";

var jwtAudience = builder.Configuration["JWT_AUDIENCE"]
    ?? builder.Configuration["JwtSettings:Audience"]
    ?? "FOMS.Api";

var aiServiceApiKey = builder.Configuration["AI_SERVICE_API_KEY"];
if (string.IsNullOrWhiteSpace(aiServiceApiKey) || IsPlaceholderSecret(aiServiceApiKey))
{
    throw new InvalidOperationException("Fatal Startup Error: AI_SERVICE_API_KEY is missing, too short, or contains a placeholder value. Set a secure service key.");
}

// Configured CORS Origins (No wildcards)
var configuredOrigins = builder.Configuration.GetSection("AllowedCorsOrigins").Get<string[]>()
    ?? builder.Configuration["CORS_ALLOWED_ORIGINS"]?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    ?? Array.Empty<string>();

var allowedCorsOrigins = configuredOrigins.Length > 0
    ? configuredOrigins
    : new[] { "http://localhost:5173", "http://localhost:4173" };

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedCorsOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

builder.Services.AddHttpClient("AiServiceClient", client =>
{
    var aiServiceUrl = builder.Configuration["AI_SERVICE_URL"] ?? "http://foms-ai-service:8000";
    client.BaseAddress = new Uri(aiServiceUrl);
});

var app = builder.Build();

app.UseCors();

var isDevEnvironment = app.Environment.IsDevelopment();
var enableDevMockTokens = builder.Configuration.GetValue<bool>("ENABLE_DEV_MOCK_TOKENS", false);

// Proxy Endpoint
app.Map("/{*path}", async (string? path, HttpContext context, IHttpClientFactory clientFactory) =>
{
    path ??= "";

    // Normalize path to strip leading "api/ai/" or "api/ai" if present
    if (path.StartsWith("api/ai/", StringComparison.OrdinalIgnoreCase))
    {
        path = path.Substring(7);
    }
    else if (path.Equals("api/ai", StringComparison.OrdinalIgnoreCase))
    {
        path = "";
    }

    var pathLower = path.ToLowerInvariant();

    // 1. Authenticate user from Bearer Token with full cryptographic verification
    string? authHeader = context.Request.Headers["Authorization"];
    var authResult = GatewayJwtValidator.ValidateToken(
        authHeader,
        jwtSecret,
        jwtIssuer,
        jwtAudience,
        isDevEnvironment,
        enableDevMockTokens);

    // 2. Enforce Authentication check
    if (!authResult.IsValid && !pathLower.StartsWith("auth/login"))
    {
        context.Response.StatusCode = 401;
        await context.Response.WriteAsJsonAsync(new
        {
            code = "UNAUTHORIZED",
            message = authResult.FailureReason ?? "Authentication token is missing or invalid."
        });
        return;
    }

    var username = authResult.Username;
    var role = authResult.Role;
    var permissions = authResult.Permissions;
    var clientId = authResult.ClientId;
    var accountType = authResult.AccountType;
    var passwordVersion = authResult.PasswordVersion;

    // Block Clients completely from non-data fetching routes, or enforce password flow
    if (role == "Client" && !pathLower.StartsWith("auth/change-password"))
    {
        if (passwordVersion == "1")
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(new
            {
                code = "MUST_CHANGE_PASSWORD",
                message = "Access denied: Password must be changed before accessing AI features."
            });
            return;
        }
    }

    // 3. Resolve path-based permission checks (RBAC validation)
    string requiredPermission = GatewayJwtValidator.ResolveRequiredPermission(pathLower, context.Request.Method);
    bool hasPermission = GatewayJwtValidator.HasRoutePermission(role, permissions, pathLower, context.Request.Method);

    if (!hasPermission)
    {
        context.Response.StatusCode = 403;
        await context.Response.WriteAsJsonAsync(new { 
            code = "FORBIDDEN", 
            message = $"Access denied: User '{username}' does not have the required permission scope: '{requiredPermission}'" 
        });
        return;
    }

    // 4. Prepare proxy request to Python FastAPI Backend
    var client = clientFactory.CreateClient("AiServiceClient");
    
    var queryString = context.Request.QueryString.Value;
    var targetPath = $"api/ai/{path}{queryString}";
    
    var proxyRequest = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetPath);

    if (context.Request.ContentLength > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
    {
        var streamContent = new StreamContent(context.Request.Body);
        proxyRequest.Content = streamContent;
        if (context.Request.ContentType != null)
        {
            proxyRequest.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(context.Request.ContentType);
        }
    }

    // Inject Security Service Credentials and User Context Headers (stripping any client-supplied headers)
    GatewayJwtValidator.ApplyVerifiedIdentityHeaders(proxyRequest, authResult, aiServiceApiKey);

    try
    {
        var response = await client.SendAsync(proxyRequest, HttpCompletionOption.ResponseHeadersRead);

        context.Response.StatusCode = (int)response.StatusCode;
        foreach (var header in response.Headers)
        {
            context.Response.Headers[header.Key] = header.Value.ToArray();
        }
        foreach (var header in response.Content.Headers)
        {
            context.Response.Headers[header.Key] = header.Value.ToArray();
        }

        await response.Content.CopyToAsync(context.Response.Body);
    }
    catch (HttpRequestException ex)
    {
        context.Response.StatusCode = 503;
        await context.Response.WriteAsJsonAsync(new
        {
            code = "AI_SERVICE_UNAVAILABLE",
            message = "The financial intelligence AI backend service is currently offline or undergoing maintenance.",
            details = ex.Message,
            traceId = $"ERR-GW-{DateTime.UtcNow.Ticks}"
        });
    }
});

app.Run();

public static class ReferenceNormalizer
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value
            .Trim()
            .ToUpperInvariant()
            .Replace("_", "-");

        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s+", "-");
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"-+", "-");
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"[^A-Z0-9\-]", "");

        return normalized;
    }
}
