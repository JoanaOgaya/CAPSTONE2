using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using FOMS.Application.DTOs;
using FOMS.Application.Interfaces;
using FOMS.Application.Models;
using FOMS.Domain.Entities;

using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace FOMS.Application.Services;

/// <summary>
/// Handles all authentication operations for FOMS.
/// 
/// SECURITY DESIGN:
/// - Passwords are ALWAYS stored and verified using PBKDF2 with SHA-256, 150,000 iterations.
/// - There is NO plaintext password fallback. Any account without a valid PBKDF2 hash
///   cannot authenticate until an admin resets their password.
/// - Failed login attempts are counted. After 5 consecutive failures the account is
///   locked and Status is set to "Locked". Only an Accountant can unlock it.
/// - Refresh tokens are single-use (rotated on each use) and stored in the database.
///   Revoked tokens are permanently rejected.
/// </summary>
public class AuthService : IAuthService
{
    private readonly IApplicationDbContext _context;
    private readonly JwtSettings _jwtSettings;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;
    private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

    private const int MaxFailedAttempts = 5;

    public AuthService(
        IApplicationDbContext context,
        IConfiguration configuration,
        ILogger<AuthService> logger)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
        _jwtSettings = configuration.GetSection("JwtSettings").Get<JwtSettings>()
            ?? new JwtSettings();
        if (string.IsNullOrWhiteSpace(_jwtSettings.Secret))
        {
            _jwtSettings.Secret = configuration["JWT_SECRET"]
                ?? configuration["JwtSettings:Secret"]
                ?? string.Empty;
        }
        if (string.IsNullOrWhiteSpace(_jwtSettings.Secret) || _jwtSettings.Secret.Length < 32)
        {
            throw new InvalidOperationException("Fatal Auth Error: JWT Secret is missing or too short (minimum 32 characters required).");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // LOGIN
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<AuthResultDto> LoginAsync(string employeeNumber, string password)
    {
        // C1 FR-001 / C2 FR-001: Staff sign in with their employee number only.
        var user = await _context.Employees
            .FirstOrDefaultAsync(u => u.Id == employeeNumber);

        if (user == null)
        {
            await WriteAuditAsync("Unknown", "Unknown", "Login Denied",
                $"Login attempt for unknown employee number: {employeeNumber}.");
            throw new UnauthorizedAccessException("Invalid employee number or password.");
        }

        var isPasswordValid = VerifyPassword(password, user.PasswordHash);

        // Step 2: Reject inactive accounts immediately (before consuming a lockout slot)
        if (!user.IsActive)
        {
            await WriteAuditAsync(user.Id, user.Id, "Login Denied",
                $"Login denied for inactive account: {employeeNumber}.");
            throw new UnauthorizedAccessException("User account is inactive.");
        }

        // Step 3: Reject locked accounts
        if (user.Status == "Locked")
        {
            await WriteAuditAsync(user.Id, user.Id, "Login Blocked",
                $"Login attempted on locked account: {employeeNumber}. Locked at: {user.LockedAt:u}.");
            throw new UnauthorizedAccessException(
                "Your account has been locked after too many failed attempts. Contact an Accountant to unlock it.");
        }

        // Step 3.5: Reject expired temporary passwords
        if (user.IsTemporaryPassword && user.TemporaryPasswordExpiresAt.HasValue && user.TemporaryPasswordExpiresAt.Value < DateTime.UtcNow)
        {
            await WriteAuditAsync(user.Id, user.Id, "Login Denied",
                $"Login denied for expired temporary password: {employeeNumber}.");
            throw new UnauthorizedAccessException("Temporary password has expired. Please contact an administrator to reset your password.");
        }


        if (!isPasswordValid)
        {
            user.FailedLoginCount += 1;

            if (user.FailedLoginCount >= MaxFailedAttempts)
            {
                user.Status = "Locked";
                user.LockedAt = DateTime.UtcNow;
                user.IsActive = false;

                await WriteAuditAsync(user.Id, user.Id, "Account Locked",
                    $"Account {employeeNumber} locked after {user.FailedLoginCount} consecutive failed login attempts.");
            }
            else
            {
                int remaining = MaxFailedAttempts - user.FailedLoginCount;
                await WriteAuditAsync(user.Id, user.Id, "Login Denied",
                    $"Incorrect password for {employeeNumber}. Failed attempts: {user.FailedLoginCount}. {remaining} attempt(s) remaining before lockout.");
            }

            await _context.SaveChangesAsync(default);
            throw new UnauthorizedAccessException("Invalid employee number or password.");
        }

        // C1 FR-004 / C2 FR-004: Only the four approved staff roles receive FOMS sessions.
        var normalizedRole = NormalizeStaffRole(user.Role);
        if (normalizedRole == null)
        {
            await WriteAuditAsync(user.Id, user.Id, "Login Denied",
                $"Login denied for unsupported role '{user.Role}'. Coordinators are account handlers only, not login roles.");
            throw new UnauthorizedAccessException("User is not assigned an authorized FOMS staff role. Coordinators are account handlers only.");
        }

        user.Role = normalizedRole;

        // Step 5: Successful login — reset counters, issue tokens
        user.FailedLoginCount = 0;
        user.LockedAt = null;

        var token = GenerateJwtToken(user);
        var refreshToken = CreateRefreshToken(user.Id);
        await _context.RefreshTokens.AddAsync(refreshToken);

        await WriteAuditAsync(user.Id, user.Id, "Login",
            $"User {user.Username} ({user.Role}) logged in successfully.");

        await _context.SaveChangesAsync(default);

        return BuildAuthResult(user, token, refreshToken.Token);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // REFRESH TOKEN
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<AuthResultDto> RefreshTokenAsync(string refreshToken)
    {
        var existing = await _context.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == refreshToken);

        if (existing == null)
            throw new UnauthorizedAccessException("Invalid refresh token.");

        if (existing.IsRevoked)
            throw new UnauthorizedAccessException("Refresh token has been revoked.");

        if (existing.ExpiresAt < DateTime.UtcNow)
            throw new UnauthorizedAccessException("Refresh token has expired.");

        var user = await _context.Employees
            .FirstOrDefaultAsync(u => u.Id == existing.UserId);

        if (user == null || !user.IsActive || user.Status == "Locked")
            throw new UnauthorizedAccessException("Account is no longer valid.");

        // Rotate the refresh token (single-use)
        existing.IsRevoked = true;
        _context.RefreshTokens.Update(existing);

        var newRefreshToken = CreateRefreshToken(user.Id);
        await _context.RefreshTokens.AddAsync(newRefreshToken);
        await _context.SaveChangesAsync(default);

        return BuildAuthResult(user, GenerateJwtToken(user), newRefreshToken.Token);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // LOGOUT
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<bool> LogoutAsync(string refreshToken)
    {
        var existing = await _context.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == refreshToken);

        if (existing == null)
            return false;

        // Idempotent — already revoked counts as success
        if (!existing.IsRevoked)
        {
            existing.IsRevoked = true;
            _context.RefreshTokens.Update(existing);

            await WriteAuditAsync(existing.UserId, existing.UserId, "Logout",
                $"User {existing.UserId} logged out and refresh token was revoked.");

            await _context.SaveChangesAsync(default);
        }

        return true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET PROFILE
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<UserDto?> GetProfileAsync(string userId)
    {
        var user = await _context.Employees
            .FirstOrDefaultAsync(u => u.Id == userId);

        return user == null ? null : MapToDto(user);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CHANGE PASSWORD
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<bool> ChangePasswordAsync(string userId, string currentPassword, string newPassword)
    {
        var user = await _context.Employees.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
            throw new UnauthorizedAccessException("User not found.");

        if (!VerifyPassword(currentPassword, user.PasswordHash))
        {
            await WriteAuditAsync(userId, userId, "Change Password Denied",
                "Incorrect current password provided during password change attempt.");
            throw new UnauthorizedAccessException("Current password is incorrect.");
        }

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            throw new ArgumentException("New password must be at least 8 characters.");

        user.PasswordHash = HashPassword(newPassword);
        user.FailedLoginCount = 0;
        user.MustChangePassword = false;
        user.IsTemporaryPassword = false;
        user.TemporaryPasswordExpiresAt = null;

        await WriteAuditAsync(userId, userId, "Change Password",
            $"User {user.Username} successfully changed their password.");

        await _context.SaveChangesAsync(default);
        return true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // UNLOCK ACCOUNT  (Accountant only — enforced at controller level too)
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<bool> UnlockAccountAsync(string targetUserId, string adminUserId)
    {
        var target = await _context.Employees.FirstOrDefaultAsync(u => u.Id == targetUserId);
        if (target == null)
            return false;

        var previousStatus = target.Status;

        target.Status = "Active";
        target.IsActive = true;
        target.FailedLoginCount = 0;
        target.LockedAt = null;

        await WriteAuditAsync(adminUserId, targetUserId, "Account Unlocked",
            $"Account {target.Username} was unlocked by admin {adminUserId}. " +
            $"Previous status: {previousStatus}. Locked at: {target.LockedAt:u}.");

        await _context.SaveChangesAsync(default);
        return true;
    }

    public async Task<List<object>> GetLoginHistoryAsync(string userId, int count = 20)
    {
        var history = await _context.AuditLogs
            .Where(a => a.UserId == userId && (a.Action == "Login" || a.Action == "Login Denied" || a.Action == "Logout" || a.Action == "Account Locked"))
            .OrderByDescending(a => a.LoggedAt)
            .Take(count)
            .Select(a => (object)new
            {
                id = a.Id,
                action = a.Action,
                details = a.Details,
                loggedAt = a.LoggedAt
            })
            .ToListAsync();

        return history;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PRIVATE HELPERS
    // ─────────────────────────────────────────────────────────────────────────

    public string GenerateJwtToken(Employee user)
    {
        var claimsList = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim("name", user.Name),
            new Claim("role", user.Role),
            new Claim("must_change_password", user.MustChangePassword ? "true" : "false"),
            new Claim("is_temporary_password", user.IsTemporaryPassword ? "true" : "false")
        };
        if (user.TemporaryPasswordExpiresAt.HasValue)
        {
            claimsList.Add(new Claim("temp_pw_expires_at", user.TemporaryPasswordExpiresAt.Value.ToString("o")));
        }
        var claims = claimsList.ToArray();

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Secret));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string? NormalizeStaffRole(string? role)
    {
        return role?.Trim().ToLowerInvariant() switch
        {
            "finance manager" => "Finance Manager",
            "assistant of finance manager" => "Assistant of Finance Manager",
            "head accountant" => "Head Accountant",
            "accountant" => "Accountant",
            _ => null
        };
    }

    private RefreshToken CreateRefreshToken(string userId)
    {
        return new RefreshToken
        {
            UserId = userId,
            Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
            ExpiresAt = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationDays),
            CreatedAt = DateTime.UtcNow,
            IsRevoked = false
        };
    }

    /// <summary>
    /// Verifies a plaintext password against a PBKDF2 stored hash.
    /// ONLY accepts PBKDF2 format (starts with "PBKDF2$").
    /// Plaintext password comparison has been PERMANENTLY REMOVED.
    /// Any password not in PBKDF2 format will always return false.
    /// </summary>
    public static bool VerifyPassword(string password, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(storedHash) || string.IsNullOrWhiteSpace(password))
            return false;

        // PBKDF2 format: PBKDF2$iterations$base64salt$base64hash
        if (!storedHash.StartsWith("PBKDF2$"))
            return false;

        try
        {
            var parts = storedHash.Split('$');
            if (parts.Length != 4)
                return false;

            var iterations = int.Parse(parts[1]);
            var salt = Convert.FromBase64String(parts[2]);
            var storedPasswordBytes = Convert.FromBase64String(parts[3]);

            var computed = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                storedPasswordBytes.Length);

            // Constant-time comparison to prevent timing attacks
            return CryptographicOperations.FixedTimeEquals(computed, storedPasswordBytes);
        }
        catch
        {
            // Any parsing error means invalid hash — deny access
            return false;
        }
    }

    /// <summary>
    /// Creates a PBKDF2 password hash.
    /// Format: PBKDF2$150000$base64(salt)$base64(hash)
    /// Uses 150,000 iterations with SHA-256 and a 16-byte random salt.
    /// </summary>
    public static string HashPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Password cannot be empty.");

        var salt = RandomNumberGenerator.GetBytes(16);
        const int iterations = 150_000;
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, iterations, HashAlgorithmName.SHA256, 32);

        return $"PBKDF2${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    private AuthResultDto BuildAuthResult(Employee user, string accessToken, string refreshToken)
    {
        return new AuthResultDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes),
            User = MapToDto(user)
        };
    }

    private static UserDto MapToDto(Employee user) => new UserDto
    {
        Id = user.Id,
        EmployeeNumber = user.Id,
        Username = user.Username,
        FullName = user.Name,
        Email = user.Email,
        Role = user.Role,
        Position = user.Position,
        MustChangePassword = user.MustChangePassword,
        IsTemporaryPassword = user.IsTemporaryPassword,
        IsActive = user.IsActive,
        CreatedAt = user.CreatedAt
    };

    private async Task WriteAuditAsync(string userId, string entityId, string action, string details)
    {
        try
        {
            var audit = new AuditLog
            {
                UserId = userId,
                EntityName = "Employee",
                EntityId = entityId,
                Action = action,
                Details = details,
                LoggedAt = DateTime.UtcNow
            };
            await _context.AuditLogs.AddAsync(audit);
        }
        catch
        {
            // Logging failure must never fail the authentication transaction
        }
    }

    private class StarsMeResponse
    {
        public bool IsSuccess { get; set; }
        public StarsMeData? Data { get; set; }
    }

    private class StarsMeData
    {
        public string EmployeeNumber { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string AllowedSystems { get; set; } = string.Empty;
    }

    public async Task<AuthResultDto?> SsoExchangeAsync(string starsToken)
    {
        if (string.IsNullOrWhiteSpace(starsToken)) return null;

        string? employeeId = null;
        string? fullName = null;
        string? email = null;
        string? starsRole = null;

        // 1. Authoritative verification via STARS /api/Auth/me
        var starsBaseUrl = _configuration["StarsIntegration:StarsBaseUrl"]
            ?? _configuration["StarsBaseUrl"]
            ?? "https://stars-h8bgfbhghzbdc9fd.southeastasia-01.azurewebsites.net";

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{starsBaseUrl.TrimEnd('/')}/api/Auth/me");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", starsToken);
            var res = await _httpClient.SendAsync(req);
            if (res.IsSuccessStatusCode)
            {
                var content = await res.Content.ReadFromJsonAsync<StarsMeResponse>();
                if (content?.IsSuccess == true && content.Data != null)
                {
                    employeeId = content.Data.EmployeeNumber;
                    fullName = content.Data.FullName;
                    email = content.Data.Email;
                    starsRole = content.Data.Role;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "STARS authoritative /api/Auth/me check failed during SSO exchange");
        }

        // 2. Fail closed: the STARS token must be verified by STARS. The previous fallback read claims from an
        //    UNSIGNED/unverified JWT, which let anyone forge an employee number and obtain a FOMS token.
        if (string.IsNullOrWhiteSpace(employeeId))
        {
            _logger.LogWarning("SSO exchange rejected: STARS could not verify the supplied token.");
            return null;
        }

        // Normalize employee ID to comply with SQL check constraint CK_Employees_Id_Pattern (EMP-[0-9][0-9][0-9])
        string fomsEmpId = employeeId;
        if (!fomsEmpId.StartsWith("EMP-", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(fomsEmpId, out var num))
            {
                fomsEmpId = $"EMP-{num:D3}";
            }
            else
            {
                fomsEmpId = $"EMP-{fomsEmpId.PadLeft(3, '0')}";
            }
        }

        // Find employee in FOMS database
        var user = await _context.Employees.FirstOrDefaultAsync(u =>
            u.Id == fomsEmpId || u.Id == employeeId || u.Username == employeeId || (!string.IsNullOrEmpty(email) && u.Email == email));

        if (user == null)
        {
            _logger.LogWarning("SSO exchange rejected: Employee {EmployeeId} ({Email}) is not registered in FOMS.", employeeId, email);
            throw new UnauthorizedAccessException($"Employee '{employeeId}' is not registered in FOMS. Access denied.");
        }

        if (!user.IsActive || user.Status == "Locked")
        {
            throw new UnauthorizedAccessException("Account is not active in FOMS. Contact your system administrator.");
        }

        user.FailedLoginCount = 0;
        user.LockedAt = null;

        var token = GenerateJwtToken(user);
        var refreshToken = CreateRefreshToken(user.Id);
        await _context.RefreshTokens.AddAsync(refreshToken);

        await WriteAuditAsync(user.Id, user.Id, "SSO Login",
            $"User {user.Username} ({user.Role}) authenticated via STARS SSO exchange.");

        await _context.SaveChangesAsync(default);

        return BuildAuthResult(user, token, refreshToken.Token);
    }
}
