using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FOMS.Domain.Constants;
using FOMS.Domain.Entities.SqlServer;

namespace FOMS.Application.Services;

/// <summary>
/// Authoritative resolver for approval threshold limits from the database.
/// Compares and resolves limits using decimal types without floating-point rounding errors.
/// </summary>
public static class ApprovalThresholdResolver
{
    public static async Task<decimal> ResolveThresholdLimitAsync(
        IQueryable<ApprovalThresholdSetting> settingsQuery,
        string transactionType = ApprovalThresholdConfig.CashOutflowTransactionType,
        CancellationToken cancellationToken = default)
    {
        var setting = await settingsQuery
            .AsNoTracking()
            .Where(t => t.IsActive && (t.TransactionType == transactionType || t.TransactionType == ApprovalThresholdConfig.AllTransactionType))
            .OrderByDescending(t => t.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return setting?.MaximumAmount ?? ApprovalThresholdConfig.DefaultAssistantFmThreshold;
    }
}
