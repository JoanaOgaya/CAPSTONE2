namespace FOMS.Domain.Constants;

/// <summary>
/// Centralized threshold configuration and transaction limits for FOMS.
/// Replaces scattered literals across controllers with a single canonical source of truth.
/// </summary>
public static class ThresholdConstants
{
    /// <summary>
    /// CSH-09 canonical authorization limit for Assistant of Finance Manager (EMP-004): ₱10,000.00.
    /// Transactions up to and including this limit (Amount &lt;= 10,000.00) are within Assistant FM authority (Level 1).
    /// Transactions strictly exceeding this limit (Amount &gt; 10,000.00) escalate to Level 2 and require strict two-tier endorsement.
    /// </summary>
    public const decimal DefaultAssistantFmThreshold = 10000.00m;

    public const string DefaultCurrency = "PHP";

    public const string RoleAssistantFinanceManager = FomsRoles.AssistantFinanceManager;
    public const string RoleFinanceManager = FomsRoles.FinanceManager;

    public const string CategoryCashOutflow = "CashOutflow";
    public const string CategoryCashAdvance = "CashAdvance";
    public const string CategoryLiquidation = "Liquidation";

    /// <summary>
    /// Strict decimal comparison: returns true if and only if amount strictly exceeds the threshold.
    /// Boundary: Amount == Threshold is within limit (false).
    /// </summary>
    public static bool ExceedsThreshold(decimal amount, decimal threshold) => amount > threshold;

    /// <summary>
    /// Strict decimal comparison: returns true if amount is less than or exactly equal to the threshold.
    /// Boundary: Amount == Threshold is within limit (true).
    /// </summary>
    public static bool IsWithinThreshold(decimal amount, decimal threshold) => amount <= threshold;
}
