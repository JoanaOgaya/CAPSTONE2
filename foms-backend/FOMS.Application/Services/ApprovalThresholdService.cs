using System.Threading;
using System.Threading.Tasks;
using FOMS.Application.Interfaces;
using FOMS.Domain.Constants;
using FOMS.Domain.Entities.SqlServer;

namespace FOMS.Application.Services;

/// <summary>
/// Single configuration source implementation for approval threshold limits.
/// Reads from dbo.ApprovalThresholdSettings with the standardized fallback defined in ApprovalThresholdConfig.
/// All amounts are compared using decimal types to guarantee zero rounding errors.
/// </summary>
public class ApprovalThresholdService : IApprovalThresholdService
{
    private readonly IApplicationDbContext _context;

    public ApprovalThresholdService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<decimal> GetAssistantFmThresholdAsync(string transactionType = ApprovalThresholdConfig.CashOutflowTransactionType, CancellationToken cancellationToken = default)
    {
        return await ApprovalThresholdResolver.ResolveThresholdLimitAsync(_context.ApprovalThresholdSettings, transactionType, cancellationToken);
    }

    public async Task<ApprovalThresholdSetting?> GetActiveSettingAsync(string transactionType = ApprovalThresholdConfig.CashOutflowTransactionType, CancellationToken cancellationToken = default)
    {
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
            _context.ApprovalThresholdSettings.Where(t => t.IsActive && (t.TransactionType == transactionType || t.TransactionType == ApprovalThresholdConfig.AllTransactionType)),
            cancellationToken);
    }
}
