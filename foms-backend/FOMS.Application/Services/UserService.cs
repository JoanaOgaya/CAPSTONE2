using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FOMS.Application.DTOs;
using FOMS.Application.Interfaces;
using FOMS.Domain.Constants;
using FOMS.Domain.Entities;

namespace FOMS.Application.Services;

public class UserService : IUserService
{
    private readonly IApplicationDbContext _context;
    private readonly IStarsIntegrationService? _starsService;

    private static readonly HashSet<string> FinancePositions = new(StringComparer.OrdinalIgnoreCase)
    {
        FomsRoles.FinanceManager,
        FomsRoles.HeadAccountant,
        FomsRoles.Accountant,
        FomsRoles.AssistantFinanceManager
    };

    public UserService(IApplicationDbContext context, IStarsIntegrationService? starsService = null)
    {
        _context = context;
        _starsService = starsService;
    }

    public async Task<UserDto> CreateAsync(UserCreateDto request)
    {
        // 1. Guard against CEO/President creation (IAM-07: CEO is not a FOMS user)
        ValidateNotCeoOrPresident(request.Role, request.Position, request.Username, request.EmployeeId);

        // 2. Validate Position and FOMS Role alignment
        var (resolvedPosition, resolvedRole, resolvedDmsRole) = ValidateAndAlignPositionAndRole(request.Position, request.Role, request.DmsRole);

        // 3. Check local email uniqueness
        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var emailLower = request.Email.Trim().ToLowerInvariant();
            if (await _context.Employees.AnyAsync(e => e.Email.ToLower() == emailLower))
            {
                throw new InvalidOperationException($"Email '{request.Email}' is already registered in FOMS. Each email must exist only once.");
            }
        }

        // 4. Check local username / employee ID uniqueness
        if (!string.IsNullOrWhiteSpace(request.Username))
        {
            var usernameLower = request.Username.Trim().ToLowerInvariant();
            if (await _context.Employees.AnyAsync(e => e.Username.ToLower() == usernameLower || e.Id.ToLower() == usernameLower))
            {
                throw new InvalidOperationException($"Username or Employee ID '{request.Username}' is already registered in FOMS.");
            }
        }

        // 5. Cross-system check against STARS
        if (_starsService != null && _starsService.IsConfigured && !string.IsNullOrWhiteSpace(request.Email))
        {
            var (existsInStars, starsMsg) = await _starsService.CheckUserExistsInStarsAsync(request.Email, request.EmployeeId);
            if (existsInStars)
            {
                throw new InvalidOperationException($"This email is already in use by an existing STARS account. Each email must be unique across Speedex.");
            }
        }

        var roleEntity = await EnsureRoleExistsAsync(resolvedRole);
        var empId = !string.IsNullOrWhiteSpace(request.EmployeeId) ? request.EmployeeId.Trim() : request.Username.Trim();

        // 6. Password generation & temporary password flag
        string rawPassword;
        bool isTemporary;
        if (string.IsNullOrWhiteSpace(request.Password))
        {
            rawPassword = GenerateRandomPassword(14);
            isTemporary = true;
        }
        else
        {
            if (string.Equals(request.Password.Trim(), "Password@123", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Default password 'Password@123' is forbidden for new accounts. System will generate a secure temporary password if left blank.");
            }
            rawPassword = request.Password;
            isTemporary = true;
        }

        var user = new Employee
        {
            Id = empId,
            Username = request.Username.Trim(),
            Name = request.FullName.Trim(),
            Email = request.Email.Trim().ToLowerInvariant(),
            Role = roleEntity.Name,
            RoleId = roleEntity.Id,
            Position = resolvedPosition,
            Phone = request.Phone?.Trim(),
            SalaryRate = request.SalaryRate,
            DateHired = request.DateHired ?? DateTime.UtcNow,
            IsActive = true,
            Status = "Active",
            SystemAccess = "Finance Operation Service",
            CreatedAt = DateTime.UtcNow,
            PasswordHash = AuthService.HashPassword(rawPassword),
            MustChangePassword = true,
            IsTemporaryPassword = isTemporary,
            TemporaryPasswordExpiresAt = isTemporary ? DateTime.UtcNow.AddHours(72) : null
        };

        await _context.Employees.AddAsync(user);
        await _context.SaveChangesAsync(default);

        return new UserDto
        {
            Id = user.Id,
            EmployeeNumber = user.Id,
            Username = user.Username,
            FullName = user.Name,
            Email = user.Email,
            Role = user.Role,
            RoleId = user.RoleId,
            Position = user.Position,
            Phone = user.Phone,
            SalaryRate = user.SalaryRate,
            Status = user.Status,
            IsActive = user.IsActive,
            MustChangePassword = user.MustChangePassword,
            IsTemporaryPassword = user.IsTemporaryPassword,
            TemporaryPassword = rawPassword,
            CreatedAt = user.CreatedAt,
            DateHired = user.DateHired
        };
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var existing = await _context.Employees.FindAsync(id);
        if (existing == null)
            return false;

        _context.Employees.Remove(existing);
        await _context.SaveChangesAsync(default);
        return true;
    }

    public async Task<IEnumerable<UserDto>> GetAllAsync()
    {
        return await _context.Employees
            .AsNoTracking()
            .Select(user => new UserDto
            {
                Id = user.Id,
                EmployeeNumber = user.Id,
                Username = user.Username,
                FullName = user.Name,
                Email = user.Email,
                Role = user.Role,
                RoleId = user.RoleId,
                Position = user.Position,
                Phone = user.Phone,
                SalaryRate = user.SalaryRate,
                Status = user.Status,
                IsActive = user.IsActive,
                MustChangePassword = user.MustChangePassword,
                IsTemporaryPassword = user.IsTemporaryPassword,
                CreatedAt = user.CreatedAt,
                DateHired = user.DateHired
            })
            .ToListAsync();
    }

    public async Task<UserDto?> GetByIdAsync(string id)
    {
        var user = await _context.Employees.FindAsync(id);
        if (user == null)
            return null;

        return new UserDto
        {
            Id = user.Id,
            EmployeeNumber = user.Id,
            Username = user.Username,
            FullName = user.Name,
            Email = user.Email,
            Role = user.Role,
            RoleId = user.RoleId,
            Position = user.Position,
            Phone = user.Phone,
            SalaryRate = user.SalaryRate,
            Status = user.Status,
            IsActive = user.IsActive,
            MustChangePassword = user.MustChangePassword,
            IsTemporaryPassword = user.IsTemporaryPassword,
            CreatedAt = user.CreatedAt,
            DateHired = user.DateHired
        };
    }

    public async Task<IEnumerable<RoleDto>> GetRolesAsync()
    {
        return await _context.Roles
            .AsNoTracking()
            .Select(role => new RoleDto
            {
                Id = role.Id,
                Name = role.Name,
                Description = role.Description
            })
            .ToListAsync();
    }

    public async Task<RoleDto> CreateRoleAsync(RoleCreateDto request)
    {
        ValidateNotCeoOrPresident(request.Name, null, null, null);

        var role = new Role
        {
            Name = request.Name,
            Description = request.Description
        };

        await _context.Roles.AddAsync(role);
        await _context.SaveChangesAsync(default);

        return new RoleDto
        {
            Id = role.Id,
            Name = role.Name,
            Description = role.Description
        };
    }

    public async Task<UserDto?> UpdateAsync(string id, UserUpdateDto request)
    {
        ValidateNotCeoOrPresident(request.Role, request.Position, null, null);

        var existing = await _context.Employees.FindAsync(id);
        if (existing == null)
            return null;

        var (resolvedPosition, resolvedRole, resolvedDmsRole) = ValidateAndAlignPositionAndRole(request.Position, request.Role, request.DmsRole);

        existing.Name = request.FullName.Trim();
        existing.Email = request.Email.Trim().ToLowerInvariant();
        existing.IsActive = request.IsActive;
        existing.Role = resolvedRole;
        existing.Position = resolvedPosition;
        existing.Phone = request.Phone?.Trim();
        existing.SalaryRate = request.SalaryRate;
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            existing.Status = request.Status;
        }

        var role = await EnsureRoleExistsAsync(resolvedRole);
        existing.RoleId = role.Id;

        _context.Employees.Update(existing);
        await _context.SaveChangesAsync(default);

        return new UserDto
        {
            Id = existing.Id,
            EmployeeNumber = existing.Id,
            Username = existing.Username,
            FullName = existing.Name,
            Email = existing.Email,
            Role = existing.Role,
            RoleId = existing.RoleId,
            Position = existing.Position,
            Phone = existing.Phone,
            SalaryRate = existing.SalaryRate,
            Status = existing.Status,
            IsActive = existing.IsActive,
            MustChangePassword = existing.MustChangePassword,
            IsTemporaryPassword = existing.IsTemporaryPassword,
            CreatedAt = existing.CreatedAt,
            DateHired = existing.DateHired
        };
    }

    public static (string position, string role, string dmsRole) ValidateAndAlignPositionAndRole(string? position, string? role, string? dmsRole = null)
    {
        if (string.IsNullOrWhiteSpace(position))
        {
            throw new ArgumentException("Position is strictly required.");
        }

        var trimmedPosition = position.Trim();
        var trimmedRole = (role ?? string.Empty).Trim();

        if (string.Equals(trimmedPosition, "Client", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmedRole, "Client", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(dmsRole?.Trim(), "Client", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Client role is forbidden and not offered in FOMS (IAM-06, IAM-11).");
        }

        if (string.Equals(trimmedPosition, "Billing & Collection Coordinator", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmedRole, "Billing & Collection Coordinator", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmedRole, "ROLE-005", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmedRole, "ROLE-006", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Roles ROLE-005 and ROLE-006 are retained for historical audit only; assignment is blocked (IAM-01).");
        }

        var normPosition = FomsRoles.NormalizeRole(trimmedPosition);
        bool isFinancePosition = !string.IsNullOrEmpty(normPosition) && FinancePositions.Contains(normPosition);

        if (isFinancePosition)
        {
            // Finance positions: FOMS role auto-filled from position; DMS role must be None
            if (!string.IsNullOrEmpty(trimmedRole) && !string.Equals(trimmedRole, normPosition, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Role '{role}' does not match Position '{position}'. For finance staff, FOMS role is derived from Position.");
            }

            if (!string.IsNullOrWhiteSpace(dmsRole) &&
                !string.Equals(dmsRole.Trim(), "None", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(dmsRole.Trim(), "None (no DMS access)", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Finance positions cannot be assigned an active DMS operational role (received '{dmsRole}'). Must be 'None'.");
            }

            return (normPosition, normPosition, "None");
        }
        else
        {
            // Non-finance positions: FOMS role must be 'None'
            if (string.IsNullOrWhiteSpace(trimmedRole))
            {
                throw new ArgumentException("Role is strictly required.");
            }

            if (!string.Equals(trimmedRole, "None", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Non-finance position '{trimmedPosition}' must have FOMS role 'None', but received '{role}'.");
            }

            if (string.IsNullOrWhiteSpace(dmsRole))
            {
                throw new ArgumentException("DMS operational role is required for non-finance staff.");
            }

            var allowedDmsRoles = new[] { "Admin", "Encoder", "Driver", "None (no DMS access)", "None" };
            if (!allowedDmsRoles.Any(a => string.Equals(a, dmsRole.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException($"Invalid DMS operational role '{dmsRole}'. Allowed roles: Admin, Encoder, Driver, None (no DMS access).");
            }

            return (trimmedPosition, "None", dmsRole.Trim());
        }
    }

    private static void ValidateNotCeoOrPresident(string? role, string? position, string? username, string? employeeId)
    {
        if (string.Equals(role?.Trim(), "CEO", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role?.Trim(), "President", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(position?.Trim(), "CEO", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(position?.Trim(), "President", StringComparison.OrdinalIgnoreCase) ||
            (username != null && username.IndexOf("ceo", StringComparison.OrdinalIgnoreCase) >= 0) ||
            (employeeId != null && employeeId.IndexOf("ceo", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            throw new InvalidOperationException("CEO/President is not a FOMS user and cannot be created as an employee account (IAM-07). All CEO approvals are recorded via in-person approval records.");
        }
    }

    private static string GenerateRandomPassword(int length = 14)
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        const string special = "!@#$%^&*";
        const string allChars = upper + lower + digits + special;

        var bytes = RandomNumberGenerator.GetBytes(length);
        var chars = new char[length];

        chars[0] = upper[bytes[0] % upper.Length];
        chars[1] = lower[bytes[1] % lower.Length];
        chars[2] = digits[bytes[2] % digits.Length];
        chars[3] = special[bytes[3] % special.Length];

        for (int i = 4; i < length; i++)
        {
            chars[i] = allChars[bytes[i] % allChars.Length];
        }

        var shuffleBytes = RandomNumberGenerator.GetBytes(length);
        for (int i = length - 1; i > 0; i--)
        {
            int j = shuffleBytes[i] % (i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }

    private async Task<Role> EnsureRoleExistsAsync(string roleName)
    {
        var existing = await _context.Roles.FirstOrDefaultAsync(r => r.Name == roleName);
        if (existing != null)
            return existing;

        var role = new Role { Name = roleName, Description = roleName };
        await _context.Roles.AddAsync(role);
        await _context.SaveChangesAsync(default);
        return role;
    }

    public async Task<(bool exists, string matchedBy)> CheckExistsAsync(string? email, string? employeeId)
    {
        if (!string.IsNullOrWhiteSpace(email))
        {
            var emailLower = email.Trim().ToLowerInvariant();
            if (await _context.Employees.AnyAsync(e => e.Email.ToLower() == emailLower))
                return (true, "email");
        }

        if (!string.IsNullOrWhiteSpace(employeeId))
        {
            var empLower = employeeId.Trim().ToLowerInvariant();
            if (await _context.Employees.AnyAsync(e => e.Id.ToLower() == empLower || e.Username.ToLower() == empLower))
                return (true, "employeeId");
        }

        return (false, "none");
    }
}
