namespace FOMS.Domain.Constants;

public static class EmployeePositions
{
    public static readonly string[] FinancePositions =
    {
        FomsRoles.FinanceManager,
        FomsRoles.HeadAccountant,
        FomsRoles.Accountant,
        FomsRoles.AssistantFinanceManager
    };

    /// <summary>
    /// ASSUMED: Non-finance positions provisionally listed pending DMS confirmation (R5.11 / dms-handoff.md).
    /// Sourced originally from commit bb4d0ee (Phase 20) as operational defaults; not verified in DMS database.
    /// </summary>
    public static readonly string[] AssumedNonFinancePositions =
    {
        "Courier",
        "Dispatcher",
        "Driver",
        "Hub Supervisor",
        "Sorter"
    };

    public static readonly string[] AllowedDmsRoles =
    {
        "Admin",
        "Encoder",
        "Driver",
        "None (no DMS access)"
    };
}
