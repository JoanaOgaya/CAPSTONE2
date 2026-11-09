using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using FOMS.Api.Controllers;
using FOMS.Application.Services;
using FOMS.Domain.Constants;
using FOMS.Domain.Entities;
using FOMS.Domain.Entities.SqlServer;
using FOMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FOMS.Tests;

public class Phase7LiquidationAndExpenseEndorsementTests
{
    private static FomsDbContext CreateFomsDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<FomsDbContext>()
            .UseInMemoryDatabase(databaseName: $"{dbName}_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new FomsDbContext(options);
    }

    private static ApplicationDbContext CreateAppDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"{dbName}_App_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static void SetUserContext(ControllerBase controller, string role, string username, string empId, ApplicationDbContext? appDb = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, role),
            new("role", role),
            new(ClaimTypes.Name, username),
            new("name", username),
            new("unique_name", username),
            new(ClaimTypes.NameIdentifier, empId),
            new("sub", empId),
            new("employeeNumber", empId),
            new("employee_id", empId)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };

        if (appDb != null)
        {
            var spMock = new Mock<IServiceProvider>();
            spMock.Setup(sp => sp.GetService(typeof(ApplicationDbContext))).Returns(appDb);
            httpContext.RequestServices = spMock.Object;
        }

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    [Fact]
    public async Task ThresholdRouting_ExactBoundaries_EvaluatesCorrectlyWithoutRoundingErrors()
    {
        using var fomsDb = CreateFomsDb(nameof(ThresholdRouting_ExactBoundaries_EvaluatesCorrectlyWithoutRoundingErrors));

        // Seed threshold 10,000.00
        fomsDb.ApprovalThresholdSettings.Add(new ApprovalThresholdSetting
        {
            ThresholdId = 1,
            TransactionType = ThresholdConstants.CategoryCashAdvance,
            RoleName = ThresholdConstants.RoleAssistantFinanceManager,
            MaximumAmount = 10000.00m,
            Currency = "PHP",
            IsActive = true,
            EffectiveFrom = DateTime.UtcNow.AddDays(-10)
        });
        await fomsDb.SaveChangesAsync();

        var threshold = await ThresholdConfigHelper.GetAssistantFmThresholdAsync(fomsDb.ApprovalThresholdSettings, ThresholdConstants.CategoryCashAdvance);
        Assert.Equal(10000.00m, threshold);

        // 1. Just below threshold: 9,999.99m
        decimal justBelow = 9999.99m;
        Assert.True(ThresholdConstants.IsWithinThreshold(justBelow, threshold));
        Assert.False(ThresholdConstants.ExceedsThreshold(justBelow, threshold));

        // 2. Exactly at threshold: 10,000.00m
        decimal exactlyAt = 10000.00m;
        Assert.True(ThresholdConstants.IsWithinThreshold(exactlyAt, threshold));
        Assert.False(ThresholdConstants.ExceedsThreshold(exactlyAt, threshold));

        // 3. Just above threshold: 10,000.01m
        decimal justAbove = 10000.01m;
        Assert.False(ThresholdConstants.IsWithinThreshold(justAbove, threshold));
        Assert.True(ThresholdConstants.ExceedsThreshold(justAbove, threshold));

        // Decimal high-precision test: 10000.0001m
        decimal fractionalAbove = 10000.0001m;
        Assert.True(ThresholdConstants.ExceedsThreshold(fractionalAbove, threshold));
    }

    [Fact]
    public async Task CashOutflows_ThresholdRouting_AssignsLevel1OrLevel2AtExactBoundaries()
    {
        using var fomsDb = CreateFomsDb(nameof(CashOutflows_ThresholdRouting_AssignsLevel1OrLevel2AtExactBoundaries));

        fomsDb.ApprovalThresholdSettings.Add(new ApprovalThresholdSetting
        {
            ThresholdId = 1,
            TransactionType = ThresholdConstants.CategoryCashOutflow,
            RoleName = ThresholdConstants.RoleAssistantFinanceManager,
            MaximumAmount = 10000.00m,
            Currency = "PHP",
            IsActive = true,
            EffectiveFrom = DateTime.UtcNow.AddDays(-10)
        });
        await fomsDb.SaveChangesAsync();

        var controller = new CashOutflowsController(fomsDb, NullLogger<CashOutflowsController>.Instance);
        SetUserContext(controller, "Assistant of Finance Manager", "EMP-004", "EMP-004");

        // Request 1: Below threshold (9,999.99m) -> Within threshold
        var res1 = await controller.Create(new CashOutflowsController.CreateCashOutflowDto
        {
            ReferenceNumber = "CO-REF-001",
            Amount = 9999.99m,
            Category = "Courier Advances",
            Description = "Fuel advance below threshold",
            Payee = "Courier Alpha"
        });
        var obj1 = Assert.IsType<ObjectResult>(res1);
        Assert.Equal(201, obj1.StatusCode);
        var req1 = await fomsDb.CashOutflowApprovalRequests.FirstOrDefaultAsync(r => r.Amount == 9999.99m);
        Assert.NotNull(req1);
        Assert.False(req1.IsEscalated);
        Assert.Equal("Pending Assistant Review", req1.Status);

        // Request 2: Exactly at threshold (10,000.00m) -> Within threshold
        var res2 = await controller.Create(new CashOutflowsController.CreateCashOutflowDto
        {
            ReferenceNumber = "CO-REF-002",
            Amount = 10000.00m,
            Category = "Courier Advances",
            Description = "Fuel advance exact boundary",
            Payee = "Courier Beta"
        });
        var obj2 = Assert.IsType<ObjectResult>(res2);
        Assert.Equal(201, obj2.StatusCode);
        var req2 = await fomsDb.CashOutflowApprovalRequests.FirstOrDefaultAsync(r => r.Amount == 10000.00m);
        Assert.NotNull(req2);
        Assert.False(req2.IsEscalated);
        Assert.Equal("Pending Assistant Review", req2.Status);

        // Request 3: Just above threshold (10,000.01m) -> Escalated to Level 2
        var res3 = await controller.Create(new CashOutflowsController.CreateCashOutflowDto
        {
            ReferenceNumber = "CO-REF-003",
            Amount = 10000.01m,
            Category = "Courier Advances",
            Description = "Fuel advance above threshold",
            Payee = "Courier Gamma"
        });
        var obj3 = Assert.IsType<ObjectResult>(res3);
        Assert.Equal(201, obj3.StatusCode);
        var req3 = await fomsDb.CashOutflowApprovalRequests.FirstOrDefaultAsync(r => r.Amount == 10000.01m);
        Assert.NotNull(req3);
        Assert.True(req3.IsEscalated);
        Assert.Equal("Pending Assistant Review", req3.Status);
    }

    [Fact]
    public async Task CashAdvance_Escalated_EnforcesStrictTwoTierFlowAndBlocksSkippedTier()
    {
        using var fomsDb = CreateFomsDb(nameof(CashAdvance_Escalated_EnforcesStrictTwoTierFlowAndBlocksSkippedTier));
        using var appDb = CreateAppDb(nameof(CashAdvance_Escalated_EnforcesStrictTwoTierFlowAndBlocksSkippedTier));

        // Threshold 10,000.00
        fomsDb.ApprovalThresholdSettings.Add(new ApprovalThresholdSetting
        {
            ThresholdId = 1,
            TransactionType = ThresholdConstants.CategoryCashAdvance,
            RoleName = ThresholdConstants.RoleAssistantFinanceManager,
            MaximumAmount = 10000.00m,
            Currency = "PHP",
            IsActive = true
        });

        // Seed an escalated cash outflow request (15,000.00) in Pending Assistant Review
        fomsDb.CashOutflows.Add(new CashOutflow
        {
            CashOutflowId = "OUT-ESC-001",
            Amount = 15000.00m,
            Category = "Courier Advances",
            Status = "Pending Assistant Review",
            TransactionDate = DateTime.UtcNow
        });

        fomsDb.CashOutflowApprovalRequests.Add(new CashOutflowApprovalRequest
        {
            ApprovalRequestId = 101,
            RequestGuid = "REQ-ESC-001",
            CashOutflowId = "OUT-ESC-001",
            TransactionReference = "OUT-ESC-001",
            Amount = 15000.00m,
            ApprovalLevel = "Level 2",
            Status = "Pending Assistant Review",
            IsEscalated = true,
            EscalationThreshold = 10000.00m,
            SubmittedBy = "EMP-005",
            SubmittedById = "EMP-005"
        });
        await fomsDb.SaveChangesAsync();

        var approvalsController = new ApprovalsController(fomsDb, NullLogger<ApprovalsController>.Instance, appDb);

        // 1. Skipped Tier Attempt: FM tries to approve while status is Pending Assistant Review -> 400 Bad Request
        SetUserContext(approvalsController, "Finance Manager", "EMP-001", "EMP-001");
        var skippedRes = await approvalsController.Approve("REQ-ESC-001");
        Assert.IsType<BadRequestObjectResult>(skippedRes);

        // Verify NO cash outflow in Treasury
        var initialTreasuryCount = await appDb.CashFlowTransactions.CountAsync();
        Assert.Equal(0, initialTreasuryCount);

        // 2. Tier 1: Assistant FM endorses / escalates request
        SetUserContext(approvalsController, "Assistant of Finance Manager", "EMP-004", "EMP-004");
        var escalateRes = await approvalsController.Escalate("REQ-ESC-001", null);
        Assert.IsType<OkObjectResult>(escalateRes);

        var escalatedReq = await fomsDb.CashOutflowApprovalRequests.FirstAsync(r => r.RequestGuid == "REQ-ESC-001");
        Assert.Equal("Pending Finance Manager Approval", escalatedReq.Status);

        // Still NO cash outflow in Treasury before FM signs off
        Assert.Equal(0, await appDb.CashFlowTransactions.CountAsync());

        // 3. Tier 2: FM approves after Assistant endorsement
        SetUserContext(approvalsController, "Finance Manager", "EMP-001", "EMP-001");
        var fmApproveRes = await approvalsController.Approve("REQ-ESC-001");
        Assert.IsType<OkObjectResult>(fmApproveRes);

        var approvedReq = await fomsDb.CashOutflowApprovalRequests.FirstAsync(r => r.RequestGuid == "REQ-ESC-001");
        Assert.Equal("Approved by Finance Manager", approvedReq.Status);

        var outflow = await fomsDb.CashOutflows.FirstAsync(o => o.CashOutflowId == "OUT-ESC-001");
        Assert.Equal("Approved", outflow.Status);

        // Cash outflow NOW recognized in Treasury
        var treasuryTx = await appDb.CashFlowTransactions.FirstOrDefaultAsync(t => t.ReferenceNo == "OUT-ESC-001");
        Assert.NotNull(treasuryTx);
        Assert.Equal(15000.00m, treasuryTx.Amount);
        Assert.Equal("Outflow", treasuryTx.Type);
    }

    [Fact]
    public async Task Liquidation_StrictTwoTierFlow_EndorseCreatesNoOutflowOrLedger_FMApproveCreatesBalancedEntriesOnceOnly()
    {
        using var fomsDb = CreateFomsDb(nameof(Liquidation_StrictTwoTierFlow_EndorseCreatesNoOutflowOrLedger_FMApproveCreatesBalancedEntriesOnceOnly));
        using var appDb = CreateAppDb(nameof(Liquidation_StrictTwoTierFlow_EndorseCreatesNoOutflowOrLedger_FMApproveCreatesBalancedEntriesOnceOnly));

        // Seed Chart of Accounts
        appDb.ChartOfAccounts.AddRange(
            new ChartOfAccount { AccountCode = "101", Name = "Cash and Cash Equivalents", Type = "Asset", NormalBalance = "Debit" },
            new ChartOfAccount { AccountCode = "102", Name = "Accounts Receivable", Type = "Asset", NormalBalance = "Debit" },
            new ChartOfAccount { AccountCode = "105", Name = "Advances to Officers & Employees", Type = "Asset", NormalBalance = "Debit" },
            new ChartOfAccount { AccountCode = "201", Name = "Accounts Payable", Type = "Liability", NormalBalance = "Credit" },
            new ChartOfAccount { AccountCode = "501", Name = "Operating Expenses", Type = "Expense", NormalBalance = "Debit" }
        );
        await appDb.SaveChangesAsync();

        // Seed Liquidation: Total = 5,000, Leftover = 500, Reimbursement = 0 -> Settled Advance = 5,500
        fomsDb.Liquidations.Add(new Liquidation
        {
            LiquidationId = 501,
            ReferenceNumber = "LIQ-2026-501",
            SubmittedByName = "EMP-003",
            TotalAmount = 5000.00m,
            LeftoverCash = 500.00m,
            ReimbursementAmount = 0.00m,
            Status = "Pending Validation",
            CreatedAt = DateTime.UtcNow
        });
        await fomsDb.SaveChangesAsync();

        var liqController = new LiquidationsController(fomsDb, appDb);

        // 1. Skipped Tier Attempt: FM tries to approve unendorsed liquidation -> 400 Bad Request
        SetUserContext(liqController, "Finance Manager", "EMP-001", "EMP-001");
        var illegalApproveRes = await liqController.Act(501, "approve", new LiquidationDto { Remarks = "Skipping Asst FM" });
        Assert.IsType<BadRequestObjectResult>(illegalApproveRes);

        // Verify zero ledger entries & zero cash outflow in Treasury
        Assert.Equal(0, await appDb.JournalEntries.CountAsync());
        Assert.Equal(0, await appDb.GeneralLedgerEntries.CountAsync());
        Assert.Equal(0, await appDb.CashFlowTransactions.CountAsync());

        // 2. Tier 1: Assistant FM endorses liquidation
        SetUserContext(liqController, "Assistant of Finance Manager", "EMP-004", "EMP-004");
        var endorseRes = await liqController.Act(501, "endorse", new LiquidationDto { Remarks = "Verified receipts and vouchers" });
        Assert.IsType<OkObjectResult>(endorseRes);

        var liqAfterEndorse = await fomsDb.Liquidations.FindAsync(501);
        Assert.Equal("Endorsed", liqAfterEndorse!.Status);

        // Zero outflow and zero ledger entries after Tier 1 endorsement
        Assert.Equal(0, await appDb.JournalEntries.CountAsync());
        Assert.Equal(0, await appDb.GeneralLedgerEntries.CountAsync());
        Assert.Equal(0, await appDb.CashFlowTransactions.CountAsync());

        // 3. Tier 2: Finance Manager signs off (Approve)
        SetUserContext(liqController, "Finance Manager", "EMP-001", "EMP-001");
        var fmApproveRes = await liqController.Act(501, "approve", new LiquidationDto { Remarks = "Approved by FM" });
        Assert.IsType<OkObjectResult>(fmApproveRes);

        var liqAfterApprove = await fomsDb.Liquidations.FindAsync(501);
        Assert.Equal("Approved", liqAfterApprove!.Status);

        // Ledger entries created and verified for exact balance
        var journalEntries = await appDb.JournalEntries.ToListAsync();
        Assert.Single(journalEntries);
        var je = journalEntries[0];

        var glEntries = await appDb.GeneralLedgerEntries.Where(g => g.JournalEntryId == je.Id).ToListAsync();
        Assert.NotEmpty(glEntries);

        decimal totalDebit = glEntries.Sum(g => g.Debit);
        decimal totalCredit = glEntries.Sum(g => g.Credit);
        Assert.Equal(5500.00m, totalDebit);
        Assert.Equal(5500.00m, totalCredit);
        Assert.Equal(totalDebit, totalCredit);

        // Leftover cash (₱500) created in Treasury as INFLOW
        var cashInflows = await appDb.CashFlowTransactions.Where(t => t.Type == "Inflow").ToListAsync();
        Assert.Single(cashInflows);
        Assert.Equal(500.00m, cashInflows[0].Amount);

        // 4. Idempotency test: Second call to approve does not duplicate ledger or cash flows
        var secondApproveRes = await liqController.Act(501, "approve", new LiquidationDto { Remarks = "Duplicate approve call" });
        Assert.IsType<OkObjectResult>(secondApproveRes);

        Assert.Single(await appDb.JournalEntries.ToListAsync());
        Assert.Equal(glEntries.Count, await appDb.GeneralLedgerEntries.CountAsync());
        Assert.Single(await appDb.CashFlowTransactions.ToListAsync());
    }

    [Fact]
    public async Task Liquidation_WithReimbursement_PostsBalancedLedgerAndTreasuryOutflow()
    {
        using var fomsDb = CreateFomsDb(nameof(Liquidation_WithReimbursement_PostsBalancedLedgerAndTreasuryOutflow));
        using var appDb = CreateAppDb(nameof(Liquidation_WithReimbursement_PostsBalancedLedgerAndTreasuryOutflow));

        // Seed COA
        appDb.ChartOfAccounts.AddRange(
            new ChartOfAccount { AccountCode = "101", Name = "Cash and Cash Equivalents", Type = "Asset", NormalBalance = "Debit" },
            new ChartOfAccount { AccountCode = "105", Name = "Advances to Officers & Employees", Type = "Asset", NormalBalance = "Debit" },
            new ChartOfAccount { AccountCode = "501", Name = "Operating Expenses", Type = "Expense", NormalBalance = "Debit" }
        );
        await appDb.SaveChangesAsync();

        // Total = 7,500, Leftover = 0, ReimbursementAmount = 1,500 -> Advance settled = 6,000
        fomsDb.Liquidations.Add(new Liquidation
        {
            LiquidationId = 502,
            ReferenceNumber = "LIQ-2026-502",
            SubmittedByName = "EMP-003",
            TotalAmount = 7500.00m,
            LeftoverCash = 0.00m,
            ReimbursementAmount = 1500.00m,
            Status = "Endorsed",
            CreatedAt = DateTime.UtcNow
        });
        await fomsDb.SaveChangesAsync();

        var liqController = new LiquidationsController(fomsDb, appDb);
        SetUserContext(liqController, "Finance Manager", "EMP-001", "EMP-001");

        var approveRes = await liqController.Act(502, "approve", new LiquidationDto { Remarks = "Reimbursement approved" });
        Assert.IsType<OkObjectResult>(approveRes);

        // Verify Journal Entry is strictly balanced:
        // Debit: 501 Operating Expenses (7,500.00)
        // Credit: 105 Advances (6,000.00), Credit: 101 Cash Reimbursement (1,500.00)
        var je = await appDb.JournalEntries.FirstAsync();
        var glEntries = await appDb.GeneralLedgerEntries.Where(g => g.JournalEntryId == je.Id).ToListAsync();
        Assert.Equal(7500.00m, glEntries.Sum(g => g.Debit));
        Assert.Equal(7500.00m, glEntries.Sum(g => g.Credit));

        // Treasury Cash Flow OUTFLOW created for Reimbursement (1,500.00)
        var outflows = await appDb.CashFlowTransactions.Where(t => t.Type == "Outflow").ToListAsync();
        Assert.Single(outflows);
        Assert.Equal(1500.00m, outflows[0].Amount);

        // FomsDb CashOutflows record created
        var fomsOutflow = await fomsDb.CashOutflows.FirstOrDefaultAsync(o => o.CashOutflowId.Contains("502"));
        Assert.NotNull(fomsOutflow);
        Assert.Equal(1500.00m, fomsOutflow.Amount);
    }

    [Fact]
    public async Task Liquidation_RejectedOrReturned_LeavesNoLedgerEntriesAndNoTreasuryOutflow()
    {
        using var fomsDb = CreateFomsDb(nameof(Liquidation_RejectedOrReturned_LeavesNoLedgerEntriesAndNoTreasuryOutflow));
        using var appDb = CreateAppDb(nameof(Liquidation_RejectedOrReturned_LeavesNoLedgerEntriesAndNoTreasuryOutflow));

        fomsDb.Liquidations.AddRange(
            new Liquidation
            {
                LiquidationId = 601,
                ReferenceNumber = "LIQ-REJ-601",
                SubmittedByName = "EMP-003",
                TotalAmount = 3000.00m,
                Status = "Pending Validation",
                CreatedAt = DateTime.UtcNow
            },
            new Liquidation
            {
                LiquidationId = 602,
                ReferenceNumber = "LIQ-RET-602",
                SubmittedByName = "EMP-003",
                TotalAmount = 4000.00m,
                Status = "Endorsed",
                CreatedAt = DateTime.UtcNow
            }
        );
        await fomsDb.SaveChangesAsync();

        var liqController = new LiquidationsController(fomsDb, appDb);

        // 1. Assistant FM rejects Liquidation 601
        SetUserContext(liqController, "Assistant of Finance Manager", "EMP-004", "EMP-004");
        var rejectRes = await liqController.Act(601, "reject", new LiquidationDto { Remarks = "Invalid receipts" });
        Assert.IsType<OkObjectResult>(rejectRes);
        Assert.Equal("Rejected", (await fomsDb.Liquidations.FindAsync(601))!.Status);

        // 2. Finance Manager returns Liquidation 602 for correction
        SetUserContext(liqController, "Finance Manager", "EMP-001", "EMP-001");
        var returnRes = await liqController.Act(602, "return", new LiquidationDto { Remarks = "Missing official receipt copy" });
        Assert.IsType<OkObjectResult>(returnRes);
        Assert.Equal("Returned", (await fomsDb.Liquidations.FindAsync(602))!.Status);

        // Verify strictly zero ledger entries and zero treasury cash flows
        Assert.Equal(0, await appDb.JournalEntries.CountAsync());
        Assert.Equal(0, await appDb.GeneralLedgerEntries.CountAsync());
        Assert.Equal(0, await appDb.CashFlowTransactions.CountAsync());
        Assert.Equal(0, await fomsDb.CashOutflows.CountAsync());
    }

    [Fact]
    public async Task SeparationOfDuties_SelfEndorsementAndSelfApproval_StrictlyProhibitedWith403()
    {
        using var fomsDb = CreateFomsDb(nameof(SeparationOfDuties_SelfEndorsementAndSelfApproval_StrictlyProhibitedWith403));
        using var appDb = CreateAppDb(nameof(SeparationOfDuties_SelfEndorsementAndSelfApproval_StrictlyProhibitedWith403));

        // Submitter is EMP-004 (who happens to hold Assistant FM role)
        fomsDb.Liquidations.Add(new Liquidation
        {
            LiquidationId = 701,
            ReferenceNumber = "LIQ-SOD-701",
            SubmittedByName = "EMP-004",
            TotalAmount = 2500.00m,
            Status = "Pending Validation",
            CreatedAt = DateTime.UtcNow
        });

        // Submitter is EMP-001 (who happens to hold Finance Manager role)
        fomsDb.Liquidations.Add(new Liquidation
        {
            LiquidationId = 702,
            ReferenceNumber = "LIQ-SOD-702",
            SubmittedByName = "EMP-001",
            TotalAmount = 3500.00m,
            Status = "Endorsed",
            CreatedAt = DateTime.UtcNow
        });

        // Cash outflow request submitted by EMP-004
        fomsDb.CashOutflowApprovalRequests.Add(new CashOutflowApprovalRequest
        {
            ApprovalRequestId = 703,
            RequestGuid = "REQ-SOD-703",
            CashOutflowId = "OUT-SOD-703",
            TransactionReference = "OUT-SOD-703",
            Amount = 12000.00m,
            ApprovalLevel = "Level 2",
            Status = "Pending Assistant Review",
            SubmittedBy = "EMP-004",
            SubmittedById = "EMP-004"
        });

        await fomsDb.SaveChangesAsync();

        var liqController = new LiquidationsController(fomsDb, appDb);
        var approvalsController = new ApprovalsController(fomsDb, NullLogger<ApprovalsController>.Instance, appDb);

        // 1. Assistant FM (EMP-004) attempts to endorse own liquidation -> 403 Forbidden
        SetUserContext(liqController, "Assistant of Finance Manager", "EMP-004", "EMP-004");
        var endorseSelfRes = await liqController.Act(701, "endorse", new LiquidationDto { Remarks = "Self endorse" });
        Assert.Equal(403, Assert.IsType<ObjectResult>(endorseSelfRes).StatusCode);

        // 2. Finance Manager (EMP-001) attempts to approve own liquidation -> 403 Forbidden
        SetUserContext(liqController, "Finance Manager", "EMP-001", "EMP-001");
        var approveSelfRes = await liqController.Act(702, "approve", new LiquidationDto { Remarks = "Self approve" });
        Assert.Equal(403, Assert.IsType<ObjectResult>(approveSelfRes).StatusCode);

        // 3. Assistant FM (EMP-004) attempts to escalate own cash advance request -> 403 Forbidden
        SetUserContext(approvalsController, "Assistant of Finance Manager", "EMP-004", "EMP-004");
        var escalateSelfRes = await approvalsController.Escalate("REQ-SOD-703", null);
        Assert.Equal(403, Assert.IsType<ObjectResult>(escalateSelfRes).StatusCode);
    }

    [Fact]
    public async Task FinanceManagerController_Endpoints_ApproveRejectReturn_HandleEndorsementIntegrity()
    {
        using var fomsDb = CreateFomsDb(nameof(FinanceManagerController_Endpoints_ApproveRejectReturn_HandleEndorsementIntegrity));
        using var appDb = CreateAppDb(nameof(FinanceManagerController_Endpoints_ApproveRejectReturn_HandleEndorsementIntegrity));

        appDb.ChartOfAccounts.AddRange(
            new ChartOfAccount { AccountCode = "101", Name = "Cash and Cash Equivalents", Type = "Asset", NormalBalance = "Debit" },
            new ChartOfAccount { AccountCode = "105", Name = "Advances to Officers & Employees", Type = "Asset", NormalBalance = "Debit" },
            new ChartOfAccount { AccountCode = "501", Name = "Operating Expenses", Type = "Expense", NormalBalance = "Debit" }
        );
        await appDb.SaveChangesAsync();

        fomsDb.Liquidations.AddRange(
            new Liquidation
            {
                LiquidationId = 801,
                ReferenceNumber = "LIQ-FM-801",
                SubmittedByName = "EMP-003",
                TotalAmount = 2000.00m,
                Status = "Pending Validation",
                CreatedAt = DateTime.UtcNow
            },
            new Liquidation
            {
                LiquidationId = 802,
                ReferenceNumber = "LIQ-FM-802",
                SubmittedByName = "EMP-003",
                TotalAmount = 2500.00m,
                Status = "Endorsed",
                CreatedAt = DateTime.UtcNow
            }
        );
        await fomsDb.SaveChangesAsync();

        var fmController = new FinanceManagerController(fomsDb);
        SetUserContext(fmController, "Finance Manager", "EMP-001", "EMP-001", appDb);

        // 1. FM Approve unendorsed liquidation (801) -> 400 Bad Request (Skipped tier)
        var skippedRes = await fmController.ApproveLiquidation(801, new LiquidationDto { Remarks = "Direct approval attempt" });
        Assert.IsType<BadRequestObjectResult>(skippedRes);

        // 2. FM Approve endorsed liquidation (802) -> 200 OK
        var approveRes = await fmController.ApproveLiquidation(802, new LiquidationDto { Remarks = "Approved by FM" });
        Assert.IsType<OkObjectResult>(approveRes);

        var liq802 = await fomsDb.Liquidations.FindAsync(802);
        Assert.Equal("Approved", liq802!.Status);
        Assert.Single(await appDb.JournalEntries.ToListAsync());
    }
}
