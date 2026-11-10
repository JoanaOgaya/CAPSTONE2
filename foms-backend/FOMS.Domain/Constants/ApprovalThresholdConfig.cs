namespace FOMS.Domain.Constants;

/// <summary>
/// Centralized configuration constants and defaults for approval threshold routing.
/// Backed by dbo.ApprovalThresholdSettings as the authoritative database source of truth.
/// </summary>
public static class ApprovalThresholdConfig
{
    /// <summary>
    /// ASSUMED: Default Assistant of Finance Manager (EMP-004) approval limit: ₱10,000.00 ("Limit A").
    /// Seeded in Phase 7. Not specified in Revision 5 addendum.
    /// Per Product Owner Decision E, Limit A is NOT used as a fallback for the CSH-09 CEO threshold.
    /// An unset CSH-09 threshold mandates CEO approval for all cash outflows; no amount is assumed.
    /// </summary>
    public const decimal DefaultAssistantFmThreshold = 10000.00m;

    public const string CashOutflowTransactionType = "CashOutflow";
    public const string AllTransactionType = "All";
    public const string AssistantFmRoleName = FomsRoles.AssistantFinanceManager;
    public const string DefaultCurrency = "PHP";
}
