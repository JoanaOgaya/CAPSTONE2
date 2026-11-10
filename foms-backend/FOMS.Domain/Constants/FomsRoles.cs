using System;
using System.Linq;
using System.Security.Claims;

namespace FOMS.Domain.Constants;

public static class FomsRoles
{
    public const string FinanceManager = "Finance Manager";
    public const string HeadAccountant = "Head Accountant";
    public const string Accountant = "Accountant";
    public const string AssistantFinanceManager = "Assistant of Finance Manager";

    [Obsolete("Use FinanceManager instead. Legacy Financial Manager role is deprecated.")]
    public const string LegacyFinancialManager = "Financial Manager";

    [Obsolete("Use AssistantFinanceManager instead. Legacy Assistant Financial Manager role is deprecated.")]
    public const string LegacyAssistantFinancialManager = "Assistant Financial Manager";

    public const string EmpFinanceManager = "EMP-001";
    public const string EmpHeadAccountant = "EMP-002";
    public const string EmpAccountant = "EMP-003";
    public const string EmpAssistantFinanceManager = "EMP-004";

    public static readonly string[] AllCanonicalRoles =
    {
        FinanceManager,
        HeadAccountant,
        Accountant,
        AssistantFinanceManager
    };

    public static string NormalizeRole(string? rawRole)
    {
        if (string.IsNullOrWhiteSpace(rawRole))
            return string.Empty;

        var trimmed = rawRole.Trim();
        if (string.Equals(trimmed, FinanceManager, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "FinanceManager", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "Financial Manager", StringComparison.OrdinalIgnoreCase))
        {
            return FinanceManager;
        }

        if (string.Equals(trimmed, HeadAccountant, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "HeadAccountant", StringComparison.OrdinalIgnoreCase))
        {
            return HeadAccountant;
        }

        if (string.Equals(trimmed, Accountant, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "Staff", StringComparison.OrdinalIgnoreCase))
        {
            return Accountant;
        }

        if (string.Equals(trimmed, AssistantFinanceManager, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "Assistant Finance Manager", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "AssistantFinanceManager", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "Assistant Financial Manager", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "Assistant of Financial Manager", StringComparison.OrdinalIgnoreCase))
        {
            return AssistantFinanceManager;
        }

        return string.Empty;
    }

    public static bool IsRole(string? actualRole, params string[] allowedCanonicalRoles)
    {
        var normalized = NormalizeRole(actualRole);
        if (string.IsNullOrEmpty(normalized) || allowedCanonicalRoles == null || allowedCanonicalRoles.Length == 0)
            return false;

        return allowedCanonicalRoles.Any(allowed =>
            string.Equals(normalized, NormalizeRole(allowed), StringComparison.Ordinal));
    }

    public static bool IsFinanceManager(string? role) => IsRole(role, FinanceManager);
    public static bool IsHeadAccountant(string? role) => IsRole(role, HeadAccountant);
    public static bool IsAccountant(string? role) => IsRole(role, Accountant);
    public static bool IsAssistantFinanceManager(string? role) => IsRole(role, AssistantFinanceManager);

    public static string GetClaimRole(ClaimsPrincipal? user)
    {
        if (user == null)
            return string.Empty;

        var rawRole = user.FindFirst(ClaimTypes.Role)?.Value
                      ?? user.FindFirst("role")?.Value
                      ?? user.FindFirst("http://schemas.microsoft.com/ws/2008/06/identity/claims/role")?.Value;

        return NormalizeRole(rawRole);
    }

    public static string GetClaimEmployeeId(ClaimsPrincipal? user)
    {
        if (user == null)
            return string.Empty;

        var candidates = new[]
        {
            user.FindFirst("employee_number")?.Value,
            user.FindFirst("employeeNumber")?.Value,
            user.FindFirst("employee_id")?.Value,
            user.FindFirst("emp_id")?.Value,
            user.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            user.FindFirst("sub")?.Value,
            user.FindFirst("id")?.Value,
            user.FindFirst("user_id")?.Value
        };

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) &&
                candidate.Trim().StartsWith("EMP-", StringComparison.OrdinalIgnoreCase))
            {
                return candidate.Trim().ToUpperInvariant();
            }
        }

        var firstNonEmpty = candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
        return firstNonEmpty?.Trim() ?? string.Empty;
    }

    public static string GetClaimUsername(ClaimsPrincipal? user)
    {
        if (user == null)
            return string.Empty;

        return (user.FindFirst(ClaimTypes.Name)?.Value
                ?? user.FindFirst("unique_name")?.Value
                ?? user.FindFirst("username")?.Value
                ?? user.FindFirst("name")?.Value
                ?? user.Identity?.Name
                ?? string.Empty).Trim();
    }

    public static bool IsSameEmployeeId(string? submitterEmployeeId, string? actorEmployeeId)
    {
        if (string.IsNullOrWhiteSpace(submitterEmployeeId) || string.IsNullOrWhiteSpace(actorEmployeeId))
            return false;

        return string.Equals(
            submitterEmployeeId.Trim(),
            actorEmployeeId.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }
}
