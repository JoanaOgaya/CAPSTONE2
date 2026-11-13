using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using FOMS.Infrastructure.Persistence;

namespace FOMS.Api.Filters;

/// <summary>
/// Enforces server-side password change requirement and temporary password expiration.
/// When an authenticated user has MustChangePassword set to true or an active temporary password,
/// they may ONLY invoke change-password, logout, or refresh token endpoints.
/// All transactional and business endpoints return 403 Forbidden.
/// </summary>
public class MustChangePasswordFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            var path = context.HttpContext.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;

            // Allow password change, logout, and token refresh endpoints
            bool isAllowedAuthEndpoint = path.EndsWith("/auth/change-password") ||
                                         path.EndsWith("/api/auth/change-password") ||
                                         path.EndsWith("/auth/logout") ||
                                         path.EndsWith("/api/auth/logout") ||
                                         path.EndsWith("/auth/refresh") ||
                                         path.EndsWith("/api/auth/refresh");

            if (!isAllowedAuthEndpoint)
            {
                var empId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                            ?? user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

                var claimMustChange = user.FindFirst("must_change_password")?.Value;
                bool mustChange = string.Equals(claimMustChange, "true", StringComparison.OrdinalIgnoreCase);

                var tempExpiresStr = user.FindFirst("temp_pw_expires_at")?.Value;
                DateTime? expiresAt = null;
                if (!string.IsNullOrEmpty(tempExpiresStr) && DateTime.TryParse(tempExpiresStr, out var parsedExpires))
                {
                    expiresAt = parsedExpires;
                }

                // Check live DB status if available
                var dbContext = context.HttpContext.RequestServices?.GetService<ApplicationDbContext>();
                if (dbContext != null && !string.IsNullOrEmpty(empId))
                {
                    var employee = await dbContext.Employees.FindAsync(empId);
                    if (employee != null)
                    {
                        mustChange = employee.MustChangePassword;
                        if (employee.IsTemporaryPassword && employee.TemporaryPasswordExpiresAt.HasValue)
                        {
                            expiresAt = employee.TemporaryPasswordExpiresAt.Value;
                        }
                    }
                }

                if (expiresAt.HasValue && expiresAt.Value < DateTime.UtcNow)
                {
                    context.Result = new ObjectResult(new
                    {
                        success = false,
                        message = "Temporary password has expired. Please contact an administrator to reset your password."
                    })
                    {
                        StatusCode = StatusCodes.Status403Forbidden
                    };
                    return;
                }

                if (mustChange)
                {
                    context.Result = new ObjectResult(new
                    {
                        success = false,
                        message = "Password change required. You must change your temporary password before accessing system functions."
                    })
                    {
                        StatusCode = StatusCodes.Status403Forbidden
                    };
                    return;
                }
            }
        }

        await next();
    }
}
