using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;

namespace FOMS.Api.Filters;

/// <summary>
/// Machine-to-machine guard for the read-only /api/ai-data endpoints consumed by the Python AI service.
/// Authenticates with the inbound AI service API key (Authorization: ApiKey &lt;key&gt; or X-API-Key).
/// Fails closed: key not configured (or placeholder) => 503, missing/invalid key => 401.
/// Identity is never taken from any other header.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireAiServiceApiKeyAttribute : Attribute, IAsyncActionFilter
{
    public const string ConfigKey = "AiService:InboundApiKey";
    public const string EnvConfigKey = "FOMS_AI_SERVICE_API_KEY";

    private static readonly string[] Placeholders = { "change-me", "change_me", "replace_with", "your_secret", "foms-secret-key-2026" };

    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var config = context.HttpContext.RequestServices.GetService(typeof(IConfiguration)) as IConfiguration;
        var expected = (config?[ConfigKey] ?? config?[EnvConfigKey])?.Trim();

        if (string.IsNullOrWhiteSpace(expected) || expected.Length < 16 ||
            Placeholders.Any(p => expected.Contains(p, StringComparison.OrdinalIgnoreCase)))
        {
            context.Result = new ObjectResult(new { isSuccess = false, message = "AI service inbound API key is not configured on this server." }) { StatusCode = 503 };
            return Task.CompletedTask;
        }

        var headers = context.HttpContext.Request.Headers;
        string? supplied = null;
        var auth = headers["Authorization"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(auth) && auth.StartsWith("ApiKey ", StringComparison.OrdinalIgnoreCase))
            supplied = auth.Substring("ApiKey ".Length).Trim();
        supplied ??= headers["X-API-Key"].FirstOrDefault()?.Trim();

        if (string.IsNullOrWhiteSpace(supplied) || !ConstantTimeEquals(supplied, expected))
        {
            context.Result = new UnauthorizedObjectResult(new { isSuccess = false, message = "Invalid or missing API key." });
            return Task.CompletedTask;
        }

        return next();
    }

    private static bool ConstantTimeEquals(string a, string b)
    {
        var ab = SHA256.HashData(Encoding.UTF8.GetBytes(a));
        var bb = SHA256.HashData(Encoding.UTF8.GetBytes(b));
        return CryptographicOperations.FixedTimeEquals(ab, bb);
    }
}
