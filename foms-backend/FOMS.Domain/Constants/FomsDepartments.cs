using System;
using System.Collections.Generic;
using System.Linq;

namespace FOMS.Domain.Constants;

public static class FomsDepartments
{
    public const string Finance = "DEP-FIN";
    public const string Coordinator = "DEP-CCS";
    public const string Dispatch = "DEP-DSP";
    public const string Forwarding = "DEP-FWD";
    public const string Overhead = "DEP-OVH";

    public const string LegacyOperations = "DEP-OPS";

    public static readonly IReadOnlyList<string> CanonicalCodes = new[]
    {
        Finance,
        Coordinator,
        Dispatch,
        Forwarding,
        Overhead
    };

    public static readonly IReadOnlyList<string> NonFmSelectableCodes = new[]
    {
        Finance,
        Coordinator,
        Dispatch,
        Forwarding
    };

    public static readonly IReadOnlyDictionary<string, string> DepartmentNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { Finance, "Finance & Accounting" },
        { Coordinator, "Coordinator & Customer Service Team" },
        { Dispatch, "Dispatch Team" },
        { Forwarding, "Forwarding Team" },
        { Overhead, "Company-wide (Overhead)" },
        { LegacyOperations, "Operations (Legacy)" }
    };

    public static string Normalize(string? rawCode, string userRole, bool isNewRequest = true)
    {
        if (string.IsNullOrWhiteSpace(rawCode))
        {
            throw new ArgumentException("Department is strictly required.");
        }

        var trimmed = rawCode.Trim();

        // 1. Finance & Accounting
        if (trimmed.Equals(Finance, StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("FINANCE", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Finance & Accounting", StringComparison.OrdinalIgnoreCase))
        {
            return Finance;
        }

        // 2. Coordinator & Customer Service Team
        if (trimmed.Equals(Coordinator, StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("COORDINATOR", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Coordinator & Customer Service Team", StringComparison.OrdinalIgnoreCase))
        {
            return Coordinator;
        }

        // 3. Dispatch Team
        if (trimmed.Equals(Dispatch, StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("DISPATCH", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Dispatch Team", StringComparison.OrdinalIgnoreCase))
        {
            return Dispatch;
        }

        // 4. Forwarding Team
        if (trimmed.Equals(Forwarding, StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("FORWARDING", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Forwarding Team", StringComparison.OrdinalIgnoreCase))
        {
            return Forwarding;
        }

        // 5. Company-wide (Overhead) - Finance Manager ONLY
        if (trimmed.Equals(Overhead, StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("OVERHEAD", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("COMPANY_OVERHEAD", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Company-wide (Overhead)", StringComparison.OrdinalIgnoreCase))
        {
            bool isFm = string.Equals(userRole, "Finance Manager", StringComparison.OrdinalIgnoreCase);

            if (!isFm)
            {
                throw new UnauthorizedAccessException("Department 'DEP-OVH' (Company-wide Overhead) can only be selected by the Finance Manager.");
            }
            return Overhead;
        }

        // 6. Legacy Operations - Rejected for new requests
        if (trimmed.Equals(LegacyOperations, StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Operations", StringComparison.OrdinalIgnoreCase))
        {
            if (isNewRequest)
            {
                throw new ArgumentException("Department 'DEP-OPS' is a legacy department and cannot be selected for new requests. Allowed departments: DEP-FIN, DEP-CCS, DEP-DSP, DEP-FWD (and DEP-OVH for Finance Manager).");
            }
            return LegacyOperations;
        }

        throw new ArgumentException($"Unknown department '{rawCode}'. Allowed departments: DEP-FIN, DEP-CCS, DEP-DSP, DEP-FWD (and DEP-OVH for Finance Manager).");
    }
}
