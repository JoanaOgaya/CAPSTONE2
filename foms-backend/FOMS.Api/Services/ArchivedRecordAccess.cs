using System;
using System.Linq;
using System.Security.Claims;

namespace FOMS.Api.Services;

public static class ArchivedRecordAccess
{
    public static bool CanViewArchivedRecords(ClaimsPrincipal user)
    {
        return user.Claims
            .Where(claim => claim.Type == ClaimTypes.Role || claim.Type == "role")
            .Select(claim => claim.Value)
            .Any(role =>
                role.Equals("Finance Manager", StringComparison.OrdinalIgnoreCase) ||
                role.Equals("Finance Manager", StringComparison.OrdinalIgnoreCase) ||
                role.Equals("Head Accountant", StringComparison.OrdinalIgnoreCase) ||
                role.Equals("HeadAccountant", StringComparison.OrdinalIgnoreCase));
    }
}
