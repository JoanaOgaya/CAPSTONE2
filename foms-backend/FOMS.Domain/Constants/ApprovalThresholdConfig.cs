namespace FOMS.Domain.Constants;

/// <summary>
/// Centralized configuration constants and defaults for approval threshold routing.
/// Backed by dbo.ApprovalThresholdSettings as the authoritative database source of truth.
/// </summary>
public static class ApprovalThresholdConfig
{
    /// <summary>
    /// Default Assistant of Finance Manager (EMP-004) approval limit: ₱10,000.00.
    /// Transactions exceeding this threshold require Tier 1 Assistant FM endorsement
    /// followed by Tier 2 Finance Manager (EMP-001) final sign-off.
    /// </summary>
    public const decimal DefaultAssistantFmThreshold = 10000.00m;

    public const string CashOutflowTransactionType = "CashOutflow";
    public const string AllTransactionType = "All";
    public const string AssistantFmRoleName = FomsRoles.AssistantFinanceManager;
    public const string DefaultCurrency = "PHP";
}
