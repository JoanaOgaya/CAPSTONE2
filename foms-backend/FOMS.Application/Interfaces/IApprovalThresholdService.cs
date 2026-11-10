using System.Threading;
using System.Threading.Tasks;
using FOMS.Domain.Entities.SqlServer;

namespace FOMS.Application.Interfaces;

/// <summary>
/// Single authoritative configuration service for transaction limits and approval thresholds.
/// Ensures all threshold checks read from a unified source without scattered literal constants.
/// </summary>
public interface IApprovalThresholdService
{
    /// <summary>
    /// Retrieves the active maximum limit for Assistant of Finance Manager (EMP-004) approvals.
    /// Amounts at or below this limit may be approved at Tier 1.
    /// Amounts strictly greater than this limit require strict two-tier endorsement (escalated to Tier 2 FM).
    /// </summary>
    Task<decimal> GetAssistantFmThresholdAsync(string transactionType = "CashOutflow", CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the active threshold setting record from the database.
    /// </summary>
    Task<ApprovalThresholdSetting?> GetActiveSettingAsync(string transactionType = "CashOutflow", CancellationToken cancellationToken = default);
}
