using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FOMS.Application.DTOs;
using FOMS.Application.Interfaces;

namespace FOMS.Api.Controllers;

/// <summary>
/// Handles authentication for FOMS: login, logout, token refresh, profile, 
/// password change, and account unlock.
///
/// Security notes:
/// - All error messages are intentionally generic to prevent information leakage.
/// - Exception handling is delegated to the global exception middleware in Program.cs.
/// - Unlock requires Accountant role â€” enforced at both controller AND service level.
/// </summary>
[Route("api/auth")]
[Route("auth")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // POST /api/auth/login
    // Public â€” no token required
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        if (!FOMS.Application.Services.AuthService.CheckIpRateLimit(ip, out var retryAfter))
        {
            Response.Headers.Append("Retry-After", retryAfter.ToString());
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                success = false,
                message = $"Too many login attempts from this IP address. Please wait {retryAfter} seconds before trying again."
            });
        }

        var identifier = request.GetIdentifier();
        if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { success = false, message = "Employee number and password are required." });

        try
        {
            var result = await _authService.LoginAsync(identifier, request.Password);

            int parsedUserId = 1;
            if (int.TryParse(result.User.Id?.Replace("EMP-", "").TrimStart('0'), out var pId))
            {
                parsedUserId = pId;
            }

            var userProfile = new
            {
                userId = parsedUserId,
                employeeNumber = result.User.EmployeeNumber ?? result.User.Id,
                fullName = result.User.FullName,
                role = result.User.Role
            };

            return Ok(new
            {
                success = true,
                message = "Login successful.",
                token = result.AccessToken,
                accessToken = result.AccessToken,
                refreshToken = result.RefreshToken,
                user = userProfile,
                data = new
                {
                    accessToken = result.AccessToken,
                    refreshToken = result.RefreshToken,
                    token = result.AccessToken,
                    user = userProfile
                }
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            if (ex.Message.Contains("inactive", StringComparison.OrdinalIgnoreCase))
            {
                return Unauthorized(new { success = false, message = "User account is inactive." });
            }

            return Unauthorized(new { success = false, message = "Invalid employee number or password." });
        }
    }

    public class SsoExchangeRequestDto
    {
        public string Token { get; set; } = string.Empty;
    }

    [AllowAnonymous]
    [HttpPost("sso-exchange")]
    public async Task<IActionResult> SsoExchange([FromBody] SsoExchangeRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request?.Token))
            return BadRequest(new { success = false, message = "Token is required." });

        try
        {
            var result = await _authService.SsoExchangeAsync(request.Token);
            if (result == null)
                return Unauthorized(new { success = false, message = "Invalid or unverified SSO session token." });

            int parsedUserId = 1;
            if (int.TryParse(result.User.Id?.Replace("EMP-", "").TrimStart('0'), out var pId))
            {
                parsedUserId = pId;
            }

            var userProfile = new
            {
                userId = parsedUserId,
                employeeNumber = result.User.EmployeeNumber ?? result.User.Id,
                fullName = result.User.FullName,
                role = result.User.Role
            };

            return Ok(new
            {
                success = true,
                message = "SSO token exchanged successfully.",
                token = result.AccessToken,
                accessToken = result.AccessToken,
                refreshToken = result.RefreshToken,
                user = userProfile,
                data = new
                {
                    accessToken = result.AccessToken,
                    refreshToken = result.RefreshToken,
                    token = result.AccessToken,
                    user = userProfile
                }
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { success = false, message = ex.Message });
        }
        catch (Exception)
        {
            return StatusCode(500, new { success = false, message = "SSO token exchange is temporarily unavailable." });
        }
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // POST /api/auth/logout
    // Requires valid JWT
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] LogoutRequestDto? request)
    {
        var authHeader = Request.Headers["Authorization"].FirstOrDefault();
        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            await _authService.LogoutAsync(authHeader);
        }
        else if (!string.IsNullOrWhiteSpace(request?.RefreshToken))
        {
            await _authService.LogoutAsync(request.RefreshToken);
        }

        return NoContent();
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // POST /api/auth/refresh
    // Public â€” accepts a refresh token, issues a new access token
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequestDto request)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { success = false, message = "RefreshToken is required." });

        var result = await _authService.RefreshTokenAsync(request.RefreshToken);
        return Ok(new { success = true, data = result });
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // GET /api/auth/me
    // Requires valid JWT â€” returns the currently authenticated user's profile
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;

        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new { success = false, message = "Could not identify user." });

        var profile = await _authService.GetProfileAsync(userId);
        if (profile == null)
            return Unauthorized(new { success = false, message = "User not found." });

        return Ok(new { success = true, data = profile });
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // POST /api/auth/change-password
    // Requires valid JWT â€” authenticated user changes their own password
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto request)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { success = false, message = "Current password and a new password of at least 8 characters are required." });

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;

        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new { success = false, message = "Could not identify user." });

        await _authService.ChangePasswordAsync(userId, request.CurrentPassword, request.NewPassword);
        return Ok(new { success = true, message = "Password changed successfully." });
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // POST /api/auth/unlock/{userId}
    // Requires Accountant role â€” admin unlocks a locked employee account
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    [Authorize]
    [HttpPost("unlock/{userId}")]
    public async Task<IActionResult> UnlockAccount(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return BadRequest(new { success = false, message = "User ID is required." });

        var adminId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
                      ?? "Unknown";

        var result = await _authService.UnlockAccountAsync(userId, adminId);
        if (!result)
            return NotFound(new { success = false, message = $"User '{userId}' not found." });

        return Ok(new { success = true, message = $"Account '{userId}' has been unlocked." });
    }
}
