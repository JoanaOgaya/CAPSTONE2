using Microsoft.EntityFrameworkCore.Diagnostics;
using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using FOMS.Api.Controllers;
using FOMS.Domain.Constants;
using FOMS.Domain.Entities.SqlServer;
using FOMS.Infrastructure.Persistence;
using Xunit;

namespace FOMS.Tests;

/// <summary>
/// Comprehensive verification test suite for EMP-004 -> EMP-001 workflow:
/// 1. Strict two-tier endorsement for /liquidations (Asst FM endorses -> FM signs off).
/// 2. Escalated cash advances (> ₱10,000.00 threshold requires two-tier approval).
/// 3. Cash outflow recognized only after final FM sign-off.
/// 4. Boundary tests (₱9,999.99, ₱10,000.00, ₱10,000.01).
/// 5. Skipped tier enforcement (FM cannot approve un-endorsed items, Asst FM cannot sign off).
/// 6. Separation of duties / self-endorsement & self-approval prevention.
/// 7. Rejected / Returned items leave zero cash outflow and zero ledger entries.
/// 8. Decimal types without rounding errors & Treasury matching ledger.
/// </summary>
public class LiquidationsAndEscalatedApprovalsWorkflowTests
{
    private const decimal StandardThreshold = 10000.00m;

    private FomsDbContext CreateFomsDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<FomsDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new FomsDbContext(options);

        // Seed single authoritative threshold in dbo.ApprovalThresholdSettings
        db.ApprovalThresholdSettings.Add(new ApprovalThresholdSetting
        {
            TransactionType = ApprovalThresholdConfig.CashOutflowTransactionType,
            RoleName = ApprovalThresholdConfig.AssistantFmRoleName,
            MaximumAmount = StandardThreshold,
            Currency = "PHP",
            IsActive = true,
            UpdatedAt = DateTime.UtcNow
        });
        db.SaveChanges();

        return db;
    }

    private static void SetUserContext(ControllerBase controller, string role, string username, string employeeId)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, employeeId),
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, role),
            new Claim("role", role),
            new Claim("unique_name", username),
            new Claim("employee_id", employeeId),
            new Claim("employee_number", employeeId),
            new Claim("employeeNumber", employeeId),
            new Claim("sub", employeeId),
            new Claim("user_id", "4")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. Boundary Tests for Cash Advance Escalation
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Boundary_JustBelowThreshold_AssistantCanApprove_NoEscalation_CashOutflowRecognized()
    {
        using var db = CreateFomsDb(nameof(Boundary_JustBelowThreshold_AssistantCanApprove_NoEscalation_CashOutflowRecognized));
        var approvalsCtrl = new ApprovalsController(db, NullLogger<ApprovalsController>.Instance);

        const decimal amountJustBelow = 9999.99m;

        // Submitter creates cash advance
        db.CashAdvances.Add(new CashAdvance
        {
            AdvanceId = "CA-BOUNDARY-01",
            AmountGiven = amountJustBelow,
            CourierName = "Juan Courier",
            SubmittedByEmployeeId = "EMP-003",
            Status = "Draft"
        });
        await db.SaveChangesAsync();

        // Submit into approval workflow
        SetUserContext(approvalsCtrl, FomsRoles.Accountant, "EMP-003", "EMP-003");
        var submitRes = await approvalsCtrl.SubmitEntity("CashAdvance", "CA-BOUNDARY-01");
        Assert.IsType<OkObjectResult>(submitRes);

        var req = await db.CashOutflowApprovalRequests.FirstAsync(r => r.CashOutflowId == "CA-BOUNDARY-01");
        Assert.False(req.IsEscalated); // 9999.99m <= 10000.00m
        Assert.Equal(StandardThreshold, req.EscalationThreshold);

        // Assistant FM approves Level 1
        SetUserContext(approvalsCtrl, FomsRoles.AssistantFinanceManager, "EMP-004", "EMP-004");
        var approveRes = await approvalsCtrl.Approve(req.RequestGuid);
        Assert.IsType<OkObjectResult>(approveRes);

        // Verify status and Cash Outflow in Treasury
        var refreshedReq = await db.CashOutflowApprovalRequests.FirstAsync(r => r.CashOutflowId == "CA-BOUNDARY-01");
        Assert.Equal("Approved by Assistant", refreshedReq.Status);
        Assert.True(refreshedReq.IsPosted);

        var outflow = await db.CashOutflows.FirstOrDefaultAsync(o => o.SourceReference == "CA-BOUNDARY-01");
        Assert.NotNull(outflow);
        Assert.Equal(amountJustBelow, outflow.Amount);
        Assert.Equal("Approved", outflow.Status);
    }

    [Fact]
    public async Task Boundary_ExactlyAtThreshold_AssistantCanApprove_NoEscalation_CashOutflowRecognized()
    {
        using var db = CreateFomsDb(nameof(Boundary_ExactlyAtThreshold_AssistantCanApprove_NoEscalation_CashOutflowRecognized));
        var approvalsCtrl = new ApprovalsController(db, NullLogger<ApprovalsController>.Instance);

        const decimal amountExactlyAt = 10000.00m;

        db.CashAdvances.Add(new CashAdvance
        {
            AdvanceId = "CA-BOUNDARY-02",
            AmountGiven = amountExactlyAt,
            CourierName = "Juan Courier",
            SubmittedByEmployeeId = "EMP-003",
            Status = "Draft"
        });
        await db.SaveChangesAsync();

        SetUserContext(approvalsCtrl, FomsRoles.Accountant, "EMP-003", "EMP-003");
        var submitRes = await approvalsCtrl.SubmitEntity("CashAdvance", "CA-BOUNDARY-02");
        Assert.IsType<OkObjectResult>(submitRes);

        var req = await db.CashOutflowApprovalRequests.FirstAsync(r => r.CashOutflowId == "CA-BOUNDARY-02");
        Assert.False(req.IsEscalated); // Exactly at threshold: 10000.00m <= 10000.00m
        Assert.Equal(StandardThreshold, req.EscalationThreshold);

        // Assistant FM approves Level 1
        SetUserContext(approvalsCtrl, FomsRoles.AssistantFinanceManager, "EMP-004", "EMP-004");
        var approveRes = await approvalsCtrl.Approve(req.RequestGuid);
        Assert.IsType<OkObjectResult>(approveRes);

        var refreshedReq = await db.CashOutflowApprovalRequests.FirstAsync(r => r.CashOutflowId == "CA-BOUNDARY-02");
        Assert.Equal("Approved by Assistant", refreshedReq.Status);
        Assert.True(refreshedReq.IsPosted);

        var outflow = await db.CashOutflows.FirstOrDefaultAsync(o => o.SourceReference == "CA-BOUNDARY-02");
        Assert.NotNull(outflow);
        Assert.Equal(amountExactlyAt, outflow.Amount);
    }

    [Fact]
    public async Task Boundary_JustAboveThreshold_ExceedsLimit_AssistantBlocked_EscalationRequired_OutflowOnlyAfterFmSignOff()
    {
        using var db = CreateFomsDb(nameof(Boundary_JustAboveThreshold_ExceedsLimit_AssistantBlocked_EscalationRequired_OutflowOnlyAfterFmSignOff));
        var approvalsCtrl = new ApprovalsController(db, NullLogger<ApprovalsController>.Instance);

        const decimal amountJustAbove = 10000.01m;

        db.CashAdvances.Add(new CashAdvance
        {
            AdvanceId = "CA-BOUNDARY-03",
            AmountGiven = amountJustAbove,
            CourierName = "Juan Courier",
            SubmittedByEmployeeId = "EMP-003",
            Status = "Draft"
        });
        await db.SaveChangesAsync();

        SetUserContext(approvalsCtrl, FomsRoles.Accountant, "EMP-003", "EMP-003");
        var submitRes = await approvalsCtrl.SubmitEntity("CashAdvance", "CA-BOUNDARY-03");
        Assert.IsType<OkObjectResult>(submitRes);

        var req = await db.CashOutflowApprovalRequests.FirstAsync(r => r.CashOutflowId == "CA-BOUNDARY-03");
        Assert.True(req.IsEscalated); // 10000.01m > 10000.00m
        Assert.Equal(StandardThreshold, req.EscalationThreshold);

        // 1. Assistant FM attempts direct approval -> HTTP 403 Forbidden
        SetUserContext(approvalsCtrl, FomsRoles.AssistantFinanceManager, "EMP-004", "EMP-004");
        var blockedApprove = await approvalsCtrl.Approve(req.RequestGuid);
        var objResult = Assert.IsType<ObjectResult>(blockedApprove);
        Assert.Equal(403, objResult.StatusCode);

        // Verify NO cash outflow recognized in Treasury yet
        var noOutflowYet = await db.CashOutflows.FirstOrDefaultAsync(o => o.SourceReference == "CA-BOUNDARY-03");
        Assert.Null(noOutflowYet);

        // 2. Assistant FM endorses / escalates to Level 2 (Tier 1 endorsement)
        var escRes = await approvalsCtrl.Escalate(req.RequestGuid, null);
        Assert.IsType<OkObjectResult>(escRes);

        var escalatedReq = await db.CashOutflowApprovalRequests.FirstAsync(r => r.CashOutflowId == "CA-BOUNDARY-03");
        Assert.Equal("Pending Finance Manager Approval", escalatedReq.Status);
        Assert.Equal("Level 2", escalatedReq.ApprovalLevel);
        Assert.False(escalatedReq.IsPosted);

        // Still NO cash outflow before final sign-off
        noOutflowYet = await db.CashOutflows.FirstOrDefaultAsync(o => o.SourceReference == "CA-BOUNDARY-03");
        Assert.Null(noOutflowYet);

        // 3. Finance Manager (EMP-001) signs off (Tier 2 final sign-off)
        SetUserContext(approvalsCtrl, FomsRoles.FinanceManager, "EMP-001", "EMP-001");
        var fmApproveRes = await approvalsCtrl.Approve(req.RequestGuid);
        Assert.IsType<OkObjectResult>(fmApproveRes);

        // Cash outflow is recognized only AFTER final FM sign-off
        var finalReq = await db.CashOutflowApprovalRequests.FirstAsync(r => r.CashOutflowId == "CA-BOUNDARY-03");
        Assert.Equal("Approved by Finance Manager", finalReq.Status);
        Assert.True(finalReq.IsPosted);

        var finalOutflow = await db.CashOutflows.FirstOrDefaultAsync(o => o.SourceReference == "CA-BOUNDARY-03");
        Assert.NotNull(finalOutflow);
        Assert.Equal(amountJustAbove, finalOutflow.Amount);
        Assert.Equal("Approved", finalOutflow.Status);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. Skipped Tier Tests
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SkippedTier_EscalatedCashAdvance_FinanceManagerCannotApproveBeforeAssistantEscalation()
    {
        using var db = CreateFomsDb(nameof(SkippedTier_EscalatedCashAdvance_FinanceManagerCannotApproveBeforeAssistantEscalation));
        var approvalsCtrl = new ApprovalsController(db, NullLogger<ApprovalsController>.Instance);

        const decimal highAmount = 25000.00m;

        db.CashAdvances.Add(new CashAdvance
        {
            AdvanceId = "CA-SKIP-01",
            AmountGiven = highAmount,
            CourierName = "Juan Courier",
            SubmittedByEmployeeId = "EMP-003",
            Status = "Draft"
        });
        await db.SaveChangesAsync();

        SetUserContext(approvalsCtrl, FomsRoles.Accountant, "EMP-003", "EMP-003");
        await approvalsCtrl.SubmitEntity("CashAdvance", "CA-SKIP-01");

        var req = await db.CashOutflowApprovalRequests.FirstAsync(r => r.CashOutflowId == "CA-SKIP-01");
        Assert.True(req.IsEscalated);
        Assert.Equal("Pending Assistant Review", req.Status);

        // Finance Manager attempts to approve directly, SKIPPING Assistant FM review/escalation
        SetUserContext(approvalsCtrl, FomsRoles.FinanceManager, "EMP-001", "EMP-001");
        var skipApproveRes = await approvalsCtrl.Approve(req.RequestGuid);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(skipApproveRes);
        Assert.Contains("Skipped tier", badRequestResult.Value?.ToString() ?? "");

        // Verify NO cash outflow created
        var outflow = await db.CashOutflows.FirstOrDefaultAsync(o => o.SourceReference == "CA-SKIP-01");
        Assert.Null(outflow);
    }

    [Fact]
    public async Task SkippedTier_Liquidation_FinanceManagerCannotSignOffBeforeAssistantEndorsement()
    {
        using var db = CreateFomsDb(nameof(SkippedTier_Liquidation_FinanceManagerCannotSignOffBeforeAssistantEndorsement));
        var liqCtrl = new LiquidationsController(db);

        // Create liquidation in Pending Validation state
        var liq = new Liquidation
        {
            LiquidationId = 501,
            ReferenceNumber = "LIQ-SKIP-01",
            SubmittedByName = "Courier Staff",
            SubmittedByEmployeeId = "EMP-COURIER-01",
            TotalAmount = 5000.00m,
            ReimbursementAmount = 1500.00m,
            Status = "Pending Validation",
            CreatedAt = DateTime.UtcNow
        };
        db.Liquidations.Add(liq);
        await db.SaveChangesAsync();

        // Finance Manager attempts to sign off directly, SKIPPING Assistant FM endorsement
        SetUserContext(liqCtrl, FomsRoles.FinanceManager, "EMP-001", "EMP-001");
        var skipActRes = await liqCtrl.Act(501, "validate", null);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(skipActRes);
        Assert.Contains("Skipped tier", badRequestResult.Value?.ToString() ?? "");

        // Verify NO cash outflow recognized in Treasury
        var outflows = await db.CashOutflows.Where(o => o.SourceReference == "LIQ-SKIP-01").ToListAsync();
        Assert.Empty(outflows);
    }

    [Fact]
    public async Task SkippedTier_Liquidation_AssistantFmCannotPerformFinalSignOff()
    {
        using var db = CreateFomsDb(nameof(SkippedTier_Liquidation_AssistantFmCannotPerformFinalSignOff));
        var liqCtrl = new LiquidationsController(db);

        var liq = new Liquidation
        {
            LiquidationId = 502,
            ReferenceNumber = "LIQ-SKIP-02",
            SubmittedByName = "Courier Staff",
            SubmittedByEmployeeId = "EMP-COURIER-02",
            TotalAmount = 3000.00m,
            ReimbursementAmount = 800.00m,
            Status = "Endorsed",
            CreatedAt = DateTime.UtcNow
        };
        db.Liquidations.Add(liq);
        await db.SaveChangesAsync();

        // Assistant FM attempts final sign-off / validation (Tier 2 is restricted to FM)
        SetUserContext(liqCtrl, FomsRoles.AssistantFinanceManager, "EMP-004", "EMP-004");
        var blockedSignOff = await liqCtrl.Act(502, "validate", null);

        var forbiddenResult = Assert.IsType<ObjectResult>(blockedSignOff);
        Assert.Equal(403, forbiddenResult.StatusCode);

        // Verify NO cash outflow created
        var outflows = await db.CashOutflows.Where(o => o.SourceReference == "LIQ-SKIP-02").ToListAsync();
        Assert.Empty(outflows);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. Strict Two-Tier Liquidation Workflow & Treasury Synchronization
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Liquidations_StrictTwoTier_EndorseThenSignOff_ReflectsInTreasuryOnceOnly()
    {
        using var db = CreateFomsDb(nameof(Liquidations_StrictTwoTier_EndorseThenSignOff_ReflectsInTreasuryOnceOnly));
        var liqCtrl = new LiquidationsController(db);
        var cashFlowCtrl = new CashFlowController(null!, null!, db);

        const decimal totalExpense = 7500.00m;
        const decimal reimbursement = 1200.50m;
        const decimal leftover = 0.00m;

        var liq = new Liquidation
        {
            LiquidationId = 601,
            ReferenceNumber = "LIQ-2026-601",
            SubmittedByName = "Courier Juan",
            SubmittedByEmployeeId = "EMP-COURIER-03",
            TotalAmount = totalExpense,
            ReimbursementAmount = reimbursement,
            LeftoverCash = leftover,
            Status = "Pending Validation",
            CreatedAt = DateTime.UtcNow
        };
        db.Liquidations.Add(liq);
        await db.SaveChangesAsync();

        // Tier 1: Assistant of Finance Manager (EMP-004) endorses liquidation
        SetUserContext(liqCtrl, FomsRoles.AssistantFinanceManager, "EMP-004", "EMP-004");
        var endorseRes = await liqCtrl.Act(601, "endorse", null);
        Assert.IsType<OkObjectResult>(endorseRes);

        var endorsedLiq = await db.Liquidations.FirstAsync(l => l.LiquidationId == 601);
        Assert.Equal("Endorsed", endorsedLiq.Status);

        // Exit criterion: NO cash outflow before final FM sign-off!
        var outflowsBeforeSignOff = await db.CashOutflows.Where(o => o.SourceReference == "LIQ-2026-601").ToListAsync();
        Assert.Empty(outflowsBeforeSignOff);

        // Tier 2: Finance Manager (EMP-001) performs final sign-off
        SetUserContext(liqCtrl, FomsRoles.FinanceManager, "EMP-001", "EMP-001");
        var signOffRes = await liqCtrl.Act(601, "validate", null);
        Assert.IsType<OkObjectResult>(signOffRes);

        var validatedLiq = await db.Liquidations.FirstAsync(l => l.LiquidationId == 601);
        Assert.Equal("Validated", validatedLiq.Status);

        // Cash outflow is recognized only AFTER final sign-off
        var outflowsAfterSignOff = await db.CashOutflows.Where(o => o.SourceReference == "LIQ-2026-601").ToListAsync();
        Assert.Single(outflowsAfterSignOff);
        Assert.Equal(reimbursement, outflowsAfterSignOff[0].Amount);
        Assert.Equal("Completed", outflowsAfterSignOff[0].Status);

        // Treasury reflection: Treasury query matches exactly
        SetUserContext(cashFlowCtrl, FomsRoles.FinanceManager, "EMP-001", "EMP-001");
        var summaryAction = await cashFlowCtrl.GetSummary();
        var okSummary = Assert.IsType<OkObjectResult>(summaryAction);
        var json = JsonSerializer.Serialize(okSummary.Value);
        using var doc = JsonDocument.Parse(json);
        var treasuryOutflow = doc.RootElement.GetProperty("Outflow").GetDecimal();
        Assert.Equal(reimbursement, treasuryOutflow);

        // Idempotency: Attempting sign-off a second time must return 409 Conflict and create NO duplicate entries
        var duplicateSignOffRes = await liqCtrl.Act(601, "validate", null);
        var conflictResult = Assert.IsType<ConflictObjectResult>(duplicateSignOffRes);
        Assert.Contains("already been signed off", conflictResult.Value?.ToString() ?? "");

        var finalOutflows = await db.CashOutflows.Where(o => o.SourceReference == "LIQ-2026-601").ToListAsync();
        Assert.Single(finalOutflows); // Still exactly one entry
    }

    [Fact]
    public async Task Liquidations_LeftoverCash_CreatesInflowInTreasuryOnSignOff()
    {
        using var db = CreateFomsDb(nameof(Liquidations_LeftoverCash_CreatesInflowInTreasuryOnSignOff));
        var liqCtrl = new LiquidationsController(db);
        var cashFlowCtrl = new CashFlowController(null!, null!, db);

        const decimal leftoverCash = 850.75m;

        var liq = new Liquidation
        {
            LiquidationId = 602,
            ReferenceNumber = "LIQ-2026-602",
            SubmittedByName = "Courier Staff",
            SubmittedByEmployeeId = "EMP-COURIER-04",
            TotalAmount = 4149.25m,
            ReimbursementAmount = 0m,
            LeftoverCash = leftoverCash,
            Status = "Pending Validation",
            CreatedAt = DateTime.UtcNow
        };
        db.Liquidations.Add(liq);
        await db.SaveChangesAsync();

        // Asst FM endorses
        SetUserContext(liqCtrl, FomsRoles.AssistantFinanceManager, "EMP-004", "EMP-004");
        await liqCtrl.Act(602, "endorse", null);

        // FM signs off
        SetUserContext(liqCtrl, FomsRoles.FinanceManager, "EMP-001", "EMP-001");
        await liqCtrl.Act(602, "validate", null);

        // Cash Inflow recognized in Treasury
        var inflows = await db.CashInflows.Where(i => i.ReferenceNumber == "LIQ-2026-602").ToListAsync();
        Assert.Single(inflows);
        Assert.Equal(leftoverCash, inflows[0].Amount);

        SetUserContext(cashFlowCtrl, FomsRoles.FinanceManager, "EMP-001", "EMP-001");
        var summaryAction = await cashFlowCtrl.GetSummary();
        var okSummary = Assert.IsType<OkObjectResult>(summaryAction);
        var json = JsonSerializer.Serialize(okSummary.Value);
        using var doc = JsonDocument.Parse(json);
        var treasuryInflow = doc.RootElement.GetProperty("Inflow").GetDecimal();
        Assert.Equal(leftoverCash, treasuryInflow);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. Separation of Duties / Self-Endorsement & Self-Approval Prevention
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SelfEndorsement_Liquidation_SubmitterCannotEndorseOwnLiquidation()
    {
        using var db = CreateFomsDb(nameof(SelfEndorsement_Liquidation_SubmitterCannotEndorseOwnLiquidation));
        var liqCtrl = new LiquidationsController(db);

        var liq = new Liquidation
        {
            LiquidationId = 701,
            ReferenceNumber = "LIQ-SELF-01",
            SubmittedByName = "EMP-004",
            SubmittedByEmployeeId = "EMP-004",
            TotalAmount = 2500.00m,
            Status = "Pending Validation",
            CreatedAt = DateTime.UtcNow
        };
        db.Liquidations.Add(liq);
        await db.SaveChangesAsync();

        // Submitter (EMP-004) attempts to endorse own liquidation
        SetUserContext(liqCtrl, FomsRoles.AssistantFinanceManager, "EMP-004", "EMP-004");
        var endorseRes = await liqCtrl.Act(701, "endorse", null);

        var forbidden = Assert.IsType<ObjectResult>(endorseRes);
        Assert.Equal(403, forbidden.StatusCode);
        Assert.Contains("Separation of duties", forbidden.Value?.ToString() ?? "");
    }

    [Fact]
    public async Task SelfApproval_Liquidation_SubmitterCannotSignOffOwnLiquidation()
    {
        using var db = CreateFomsDb(nameof(SelfApproval_Liquidation_SubmitterCannotSignOffOwnLiquidation));
        var liqCtrl = new LiquidationsController(db);

        var liq = new Liquidation
        {
            LiquidationId = 702,
            ReferenceNumber = "LIQ-SELF-02",
            SubmittedByName = "EMP-001",
            SubmittedByEmployeeId = "EMP-001",
            TotalAmount = 4000.00m,
            ReimbursementAmount = 500.00m,
            Status = "Endorsed",
            CreatedAt = DateTime.UtcNow
        };
        db.Liquidations.Add(liq);
        await db.SaveChangesAsync();

        // Submitter (EMP-001) attempts to sign off own liquidation
        SetUserContext(liqCtrl, FomsRoles.FinanceManager, "EMP-001", "EMP-001");
        var signOffRes = await liqCtrl.Act(702, "validate", null);

        var forbidden = Assert.IsType<ObjectResult>(signOffRes);
        Assert.Equal(403, forbidden.StatusCode);
        Assert.Contains("Separation of duties", forbidden.Value?.ToString() ?? "");
    }

    [Fact]
    public async Task SelfEscalation_CashAdvance_SubmitterCannotEscalateOwnRequest()
    {
        using var db = CreateFomsDb(nameof(SelfEscalation_CashAdvance_SubmitterCannotEscalateOwnRequest));
        var approvalsCtrl = new ApprovalsController(db, NullLogger<ApprovalsController>.Instance);

        db.CashOutflowApprovalRequests.Add(new CashOutflowApprovalRequest
        {
            ApprovalRequestId = 801,
            RequestGuid = "REQ-SELF-CA-01",
            CashOutflowId = "CA-SELF-01",
            TransactionReference = "CA-SELF-01",
            Amount = 15000.00m,
            Status = "Pending Assistant Review",
            IsEscalated = true,
            SubmittedBy = "EMP-004",
            SubmittedById = "EMP-004",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // Submitter (EMP-004) attempts to escalate own request
        SetUserContext(approvalsCtrl, FomsRoles.AssistantFinanceManager, "EMP-004", "EMP-004");
        var escRes = await approvalsCtrl.Escalate("REQ-SELF-CA-01", null);

        var forbidden = Assert.IsType<ObjectResult>(escRes);
        Assert.Equal(403, forbidden.StatusCode);
        Assert.Contains("Separation of duties", forbidden.Value?.ToString() ?? "");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5. Rejected or Returned Liquidations Leave Zero Ledger / Outflow Entries
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RejectedOrReturned_Liquidation_LeavesNoOutflowOrInflowInTreasury()
    {
        using var db = CreateFomsDb(nameof(RejectedOrReturned_Liquidation_LeavesNoOutflowOrInflowInTreasury));
        var liqCtrl = new LiquidationsController(db);

        // Case A: Rejected Liquidation
        var liqRej = new Liquidation
        {
            LiquidationId = 901,
            ReferenceNumber = "LIQ-REJ-01",
            SubmittedByName = "Courier Staff",
            SubmittedByEmployeeId = "EMP-COURIER-05",
            TotalAmount = 5000.00m,
            ReimbursementAmount = 1200.00m,
            LeftoverCash = 300.00m,
            Status = "Pending Validation",
            CreatedAt = DateTime.UtcNow
        };
        db.Liquidations.Add(liqRej);
        await db.SaveChangesAsync();

        SetUserContext(liqCtrl, FomsRoles.AssistantFinanceManager, "EMP-004", "EMP-004");
        var rejRes = await liqCtrl.Act(901, "reject", null);
        Assert.IsType<OkObjectResult>(rejRes);

        var refreshedRej = await db.Liquidations.FirstAsync(l => l.LiquidationId == 901);
        Assert.Equal("Rejected", refreshedRej.Status);

        // Case B: Returned Liquidation
        var liqRet = new Liquidation
        {
            LiquidationId = 902,
            ReferenceNumber = "LIQ-RET-01",
            SubmittedByName = "Courier Staff",
            SubmittedByEmployeeId = "EMP-COURIER-06",
            TotalAmount = 6000.00m,
            ReimbursementAmount = 2000.00m,
            Status = "Pending Validation",
            CreatedAt = DateTime.UtcNow
        };
        db.Liquidations.Add(liqRet);
        await db.SaveChangesAsync();

        var retRes = await liqCtrl.Act(902, "return", null);
        Assert.IsType<OkObjectResult>(retRes);

        var refreshedRet = await db.Liquidations.FirstAsync(l => l.LiquidationId == 902);
        Assert.Equal("Returned", refreshedRet.Status);

        // Verify: ZERO cash outflows and ZERO cash inflows in database
        var anyOutflow = await db.CashOutflows.AnyAsync(o => o.SourceReference == "LIQ-REJ-01" || o.SourceReference == "LIQ-RET-01");
        var anyInflow = await db.CashInflows.AnyAsync(i => i.ReferenceNumber == "LIQ-REJ-01" || i.ReferenceNumber == "LIQ-RET-01");

        Assert.False(anyOutflow);
        Assert.False(anyInflow);
    }

    [Fact]
    public async Task RejectedOrReturned_CashAdvance_LeavesNoCashOutflowsInTreasury()
    {
        using var db = CreateFomsDb(nameof(RejectedOrReturned_CashAdvance_LeavesNoCashOutflowsInTreasury));
        var approvalsCtrl = new ApprovalsController(db, NullLogger<ApprovalsController>.Instance);

        db.CashOutflowApprovalRequests.Add(new CashOutflowApprovalRequest
        {
            ApprovalRequestId = 951,
            RequestGuid = "REQ-REJ-CA-01",
            CashOutflowId = "CA-REJ-01",
            TransactionReference = "CA-REJ-01",
            Amount = 18000.00m,
            Status = "Pending Finance Manager Approval",
            ApprovalLevel = "Level 2",
            IsEscalated = true,
            SubmittedBy = "EMP-003",
            SubmittedById = "EMP-003",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        SetUserContext(approvalsCtrl, FomsRoles.FinanceManager, "EMP-001", "EMP-001");
        using var doc = JsonDocument.Parse("{\"reason\":\"Budget allocation exhausted for current cycle.\"}");
        var rejRes = await approvalsCtrl.Reject("REQ-REJ-CA-01", doc.RootElement);
        Assert.IsType<OkObjectResult>(rejRes);

        var req = await db.CashOutflowApprovalRequests.FirstAsync(r => r.CashOutflowId == "CA-REJ-01");
        Assert.Equal("Rejected by Finance Manager", req.Status);
        Assert.False(req.IsPosted);

        // Verify NO cash outflow posted to Treasury
        var outflows = await db.CashOutflows.Where(o => o.SourceReference == "CA-REJ-01").ToListAsync();
        Assert.Empty(outflows);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 6. FinanceManagerController Liquidations Endpoints Coverage
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FinanceManagerController_ApproveLiquidation_DelegatesToSignOffAndPostsOutflow()
    {
        using var db = CreateFomsDb(nameof(FinanceManagerController_ApproveLiquidation_DelegatesToSignOffAndPostsOutflow));
        var fmCtrl = new FinanceManagerController(db);

        var liq = new Liquidation
        {
            LiquidationId = 999,
            ReferenceNumber = "LIQ-FM-999",
            SubmittedByName = "Courier Staff",
            SubmittedByEmployeeId = "EMP-COURIER-07",
            TotalAmount = 3500.00m,
            ReimbursementAmount = 500.00m,
            Status = "Endorsed",
            CreatedAt = DateTime.UtcNow
        };
        db.Liquidations.Add(liq);
        await db.SaveChangesAsync();

        SetUserContext(fmCtrl, FomsRoles.FinanceManager, "EMP-001", "EMP-001");
        var approveAction = await fmCtrl.ApproveLiquidation(999, null);
        Assert.IsType<OkObjectResult>(approveAction);

        var refreshedLiq = await db.Liquidations.FirstAsync(l => l.LiquidationId == 999);
        Assert.Equal("Validated", refreshedLiq.Status);

        var outflow = await db.CashOutflows.FirstOrDefaultAsync(o => o.SourceReference == "LIQ-FM-999");
        Assert.NotNull(outflow);
        Assert.Equal(500.00m, outflow.Amount);
    }
}
