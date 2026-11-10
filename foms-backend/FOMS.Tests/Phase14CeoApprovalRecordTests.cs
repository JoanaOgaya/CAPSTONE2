using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FOMS.Application.DTOs;
using FOMS.Application.Interfaces;
using FOMS.Application.Services;
using FOMS.Domain.Entities.SqlServer;
using FOMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FOMS.Tests;

public class Phase14CeoApprovalRecordTests
{
    private ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var context = new ApplicationDbContext(options);

        // Seed employees
        context.Employees.AddRange(
            new FOMS.Domain.Entities.Employee
            {
                Id = "EMP-001",
                Name = "Crystalyn Joyce C. Fajardo",
                Role = "Finance Manager",
                Position = "Finance Manager",
                Email = "crystalyn@foms.local",
                Username = "crystalyn",
                PasswordHash = "hash",
                SystemAccess = "FOMS",
                Status = "Active"
            },
            new FOMS.Domain.Entities.Employee
            {
                Id = "EMP-002",
                Name = "Misty",
                Role = "Head Accountant",
                Position = "Head Accountant",
                Email = "misty@foms.local",
                Username = "misty",
                PasswordHash = "hash",
                SystemAccess = "FOMS",
                Status = "Active"
            },
            new FOMS.Domain.Entities.Employee
            {
                Id = "EMP-003",
                Name = "Maria Mariel Jane Anonuevo",
                Role = "Accountant",
                Position = "Accountant",
                Email = "maria@foms.local",
                Username = "maria",
                PasswordHash = "hash",
                SystemAccess = "FOMS",
                Status = "Active"
            },
            new FOMS.Domain.Entities.Employee
            {
                Id = "EMP-004",
                Name = "Joana Marie Ogaya",
                Role = "Assistant of Finance Manager",
                Position = "Assistant of Finance Manager",
                Email = "joana@foms.local",
                Username = "joana",
                PasswordHash = "hash",
                SystemAccess = "FOMS",
                Status = "Active"
            }
        );

        // Seed Limit A threshold
        context.ApprovalThresholdSettings.Add(new ApprovalThresholdSetting
        {
            ThresholdId = 1,
            TransactionType = "CashOutflow",
            RoleName = "Assistant of Finance Manager",
            MaximumAmount = 10000.00m,
            Currency = "PHP",
            IsActive = true,
            EffectiveFrom = DateTime.UtcNow.AddYears(-1)
        });

        // Seed AnnualBudget
        context.AnnualBudgets.Add(new AnnualBudget
        {
            AnnualBudgetId = 1,
            FiscalYear = 2026,
            Status = "Submitted",
            TotalBudget = 500000m,
            PreparedBy = "EMP-001",
            CreatedAt = DateTime.UtcNow
        });

        // Seed BudgetTransfer
        context.BudgetTransfers.Add(new BudgetTransfer
        {
            TransferId = 101,
            FiscalYear = 2026,
            FromDepartmentCode = "DISPATCH",
            FromCategory = "Fuel",
            FromMonth = 1,
            ToDepartmentCode = "FORWARDING",
            ToCategory = "Fuel",
            ToMonth = 1,
            Amount = 15000m,
            Reason = "Transfer needed for province deliveries",
            Status = "Pending",
            RequestedBy = "EMP-001",
            RequestedAt = DateTime.UtcNow
        });

        // Seed BudgetOverride
        context.BudgetOverrides.Add(new BudgetOverride
        {
            OverrideId = 201,
            BudgetLineId = 1,
            RequestedAmount = 25000m,
            OverBudgetAmount = 15000m,
            Reason = "Emergency truck engine overhaul",
            Status = "Pending",
            RequestedBy = "EMP-003",
            RequestedAt = DateTime.UtcNow,
            RequiresCeoApproval = true
        });

        // Seed Reimbursement
        context.Reimbursements.Add(new Reimbursement
        {
            ReimbursementId = 301,
            ReferenceNumber = "REIM-2026-001",
            EmployeeId = "EMP-003",
            EmployeeName = "Maria",
            DepartmentCode = "FINANCE",
            Category = "Office Supplies",
            Amount = 12000m,
            Purpose = "Stationery and filing supplies",
            Status = "Validated",
            SubmittedAt = DateTime.UtcNow
        });

        // Seed CashAdvance
        context.CashAdvances.Add(new CashAdvance
        {
            AdvanceId = "CA-2026-001",
            CourierName = "Juan Dela Cruz",
            TripReference = "TRIP-001",
            AmountGiven = 15000m,
            DepartmentCode = "DISPATCH",
            Category = "Fuel",
            FiscalYear = 2026,
            Month = 1,
            Status = "Approved",
            CreatedAt = DateTime.UtcNow,
            RequiresCeoApproval = true
        });

        context.SaveChanges();
        return context;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Gate 1: Non-FM cannot record CEO approval
    // ─────────────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("Head Accountant", "EMP-002")]
    [InlineData("Accountant", "EMP-003")]
    [InlineData("Assistant of Finance Manager", "EMP-004")]
    public async Task RecordApproval_NonFinanceManager_ThrowsUnauthorizedAccessException(string role, string empId)
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        var request = new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.AnnualBudget,
            ItemId = "2026",
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow.Date
        };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            gateService.RecordApprovalAsync(request, empId, role));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Gate 2: Future ApprovalDate is rejected
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task RecordApproval_FutureApprovalDate_ThrowsArgumentException()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        var request = new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.AnnualBudget,
            ItemId = "2026",
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow.Date.AddDays(5) // Future date
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            gateService.RecordApprovalAsync(request, "EMP-001", "Finance Manager"));

        Assert.Contains("future", ex.Message);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Gate 3: Rejection requires a mandatory reason
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task RecordApproval_RejectedWithoutReason_ThrowsArgumentException()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        var request = new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.BudgetOverride,
            ItemId = "201",
            Status = "Rejected",
            ApprovalDate = DateTime.UtcNow.Date,
            RejectionReason = null // Missing
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            gateService.RecordApprovalAsync(request, "EMP-001", "Finance Manager"));

        Assert.Contains("mandatory", ex.Message);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Gate 4: Item 1 (Annual Budget) Always-On Gate Blocks Until Tick
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task AnnualBudget_BlocksUntilCeoApprovalRecordExists()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);
        var budgetService = new BudgetService(context, gateService);

        // Before record: check gate indicates not satisfied
        var gateCheck = await gateService.CheckGateAsync(CeoApprovalItemTypes.AnnualBudget, "2026");
        Assert.True(gateCheck.IsRequired);
        Assert.False(gateCheck.IsSatisfied);

        // FM attempts to approve annual budget before CEO tick -> fails
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            budgetService.ApproveAnnualBudgetAsync(1, "EMP-001", "Finance Manager"));

        // FM records in-person CEO approval with optional attachment
        var recordRequest = new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.AnnualBudget,
            ItemId = "2026",
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow.Date,
            Notes = "Approved in executive committee meeting"
        };
        await gateService.RecordApprovalAsync(recordRequest, "EMP-001", "Finance Manager");

        // Gate check is now satisfied
        var gateCheckAfter = await gateService.CheckGateAsync(CeoApprovalItemTypes.AnnualBudget, "2026");
        Assert.True(gateCheckAfter.IsSatisfied);

        // FM approves annual budget -> succeeds
        var approved = await budgetService.ApproveAnnualBudgetAsync(1, "EMP-001", "Finance Manager");
        Assert.Equal("Approved", approved.Status);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Gate 5: Item 2 (Budget Transfer) Requires Head Accountant Maker-Checker Confirmation
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task BudgetTransfer_BlocksUntilMakerCheckerConfirmationRecorded()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);
        var budgetService = new BudgetService(context, gateService);

        // 1. Before CEO record -> blocked
        var check1 = await gateService.CheckGateAsync(CeoApprovalItemTypes.BudgetTransfer, "101");
        Assert.False(check1.IsSatisfied);

        // 2. FM records CEO approval
        var record = await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.BudgetTransfer,
            ItemId = "101",
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow.Date
        }, "EMP-001", "Finance Manager");

        // 3. Before HA confirmation -> gate is still NOT satisfied
        var check2 = await gateService.CheckGateAsync(CeoApprovalItemTypes.BudgetTransfer, "101");
        Assert.False(check2.IsSatisfied);
        Assert.Contains("requires Head Accountant", check2.Message);

        // 4. FM attempts to confirm their own entered record -> rejected
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gateService.ConfirmRecordAsync(record.RecordId, "Recording", "EMP-001", "Finance Manager"));

        // 5. Head Accountant confirms the recording
        var conf = await gateService.ConfirmRecordAsync(record.RecordId, "Recording", "EMP-002", "Head Accountant");
        Assert.Equal("Recording", conf.Kind);
        Assert.Equal("EMP-002", conf.ConfirmedBy);

        // 6. Gate is now satisfied
        var check3 = await gateService.CheckGateAsync(CeoApprovalItemTypes.BudgetTransfer, "101");
        Assert.True(check3.IsSatisfied);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Gate 6: Threshold Boundary Tests (Setting B & Fail-Safe)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task ThresholdBoundary_FailSafeToLimitA_WhenSettingBUnset()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        // Setting (B) is unconfigured (NULL). Limit (A) is 10,000.
        var threshold = await gateService.GetCurrentThresholdAsync();
        Assert.True(threshold.IsUnsetFailSafe);
        Assert.Equal(10000.00m, threshold.EffectiveCeoLimit);

        // Amount <= 10,000 does NOT require CEO tick
        var checkBelow = await gateService.CheckGateAsync(CeoApprovalItemTypes.CashOutflow, "CA-2026-001", 9999.99m);
        Assert.False(checkBelow.IsRequired);
        Assert.True(checkBelow.IsSatisfied);

        var checkExact = await gateService.CheckGateAsync(CeoApprovalItemTypes.CashOutflow, "CA-2026-001", 10000.00m);
        Assert.False(checkExact.IsRequired);
        Assert.True(checkExact.IsSatisfied);

        // Amount > 10,000 strictly requires CEO tick
        var checkAbove = await gateService.CheckGateAsync(CeoApprovalItemTypes.CashOutflow, "CA-2026-001", 10000.01m);
        Assert.True(checkAbove.IsRequired);
        Assert.False(checkAbove.IsSatisfied);
        Assert.True(checkAbove.UsedFailSafeLimit);
    }

    [Fact]
    public async Task UpdateCeoThreshold_RejectsSettingBelowLimitA()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        // Attempting to set Setting (B) to 5,000 (below Limit A of 10,000) must throw
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gateService.UpdateThresholdAsync(new UpdateCeoThresholdRequest
            {
                ThresholdAmount = 5000.00m,
                Reason = "Setting below Limit A test"
            }, "EMP-001", "Finance Manager"));

        Assert.Contains("cannot be set lower than Limit (A)", ex.Message);

        // Setting above Limit A (e.g. 50,000) succeeds and writes change log
        var updated = await gateService.UpdateThresholdAsync(new UpdateCeoThresholdRequest
        {
            ThresholdAmount = 50000.00m,
            Reason = "Executive approved higher board threshold"
        }, "EMP-001", "Finance Manager");

        Assert.Equal(50000.00m, updated.ThresholdAmount);

        var history = await gateService.GetThresholdHistoryAsync();
        Assert.Single(history);
        Assert.Equal(50000.00m, history[0].NewAmount);
        Assert.Null(history[0].OldAmount);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Gate 7: Attachment Magic Bytes and 10 MB Size Validation
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Attachment_ValidatesMagicBytes_AndRejectsUnauthorizedTypes()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        // 1. Fake PDF (text payload with .pdf extension) -> rejected
        byte[] fakePdfBytes = Encoding.UTF8.GetBytes("This is not a real PDF document");
        var ex1 = await Assert.ThrowsAsync<ArgumentException>(() =>
            gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
            {
                ItemType = CeoApprovalItemTypes.AnnualBudget,
                ItemId = "2026",
                Status = "Approved",
                AttachmentBytes = fakePdfBytes,
                AttachmentFileName = "fake.pdf"
            }, "EMP-001", "Finance Manager"));

        Assert.Contains("Real file format must be genuine PDF, JPG, or PNG", ex1.Message);

        // 2. Genuine PDF (%PDF header) -> accepted on AnnualBudget (Item 1)
        byte[] genuinePdfBytes = Encoding.UTF8.GetBytes("%PDF-1.4 Fake valid PDF stream content");
        var record = await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.AnnualBudget,
            ItemId = "2026",
            Status = "Approved",
            AttachmentBytes = genuinePdfBytes,
            AttachmentFileName = "valid_board_approval.pdf"
        }, "EMP-001", "Finance Manager");

        Assert.True(record.HasAttachment);
        Assert.Equal("valid_board_approval.pdf", record.AttachmentFileName);

        // 3. Attachment on non-allowed item (Item 2: BudgetTransfer) -> rejected
        var ex2 = await Assert.ThrowsAsync<ArgumentException>(() =>
            gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
            {
                ItemType = CeoApprovalItemTypes.BudgetTransfer,
                ItemId = "101",
                Status = "Approved",
                AttachmentBytes = genuinePdfBytes,
                AttachmentFileName = "transfer.pdf"
            }, "EMP-001", "Finance Manager"));

        Assert.Contains("Attachments are permitted only on items 1, 6, and 8", ex2.Message);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Gate 8: Verification of Item Existence Before Record Creation
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task RecordApproval_NonExistentItem_ThrowsKeyNotFoundException()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        var request = new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.AnnualBudget,
            ItemId = "9999", // Does not exist
            Status = "Approved"
        };

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            gateService.RecordApprovalAsync(request, "EMP-001", "Finance Manager"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Gate 9: Corrections Require Second-Person Confirmation
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task CorrectionRecord_RequiresSecondPersonConfirmation()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        // Create initial approval record
        var original = await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.Reimbursement,
            ItemId = "301",
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow.Date
        }, "EMP-001", "Finance Manager");

        // Record a correction
        var correction = await gateService.RecordCorrectionAsync(new CreateCeoApprovalCorrectionRequest
        {
            OriginalRecordId = original.RecordId,
            ItemType = CeoApprovalItemTypes.Reimbursement,
            ItemId = "301",
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow.Date,
            CorrectionReason = "Adjusted date to match physical sign-off date"
        }, "EMP-001", "Finance Manager");

        Assert.True(correction.IsCorrection);
        Assert.Equal(original.RecordId, correction.OriginalRecordId);

        // Before correction confirmation -> gate check is NOT satisfied
        var checkBefore = await gateService.CheckGateAsync(CeoApprovalItemTypes.Reimbursement, "301", 12000m);
        Assert.False(checkBefore.IsSatisfied);
        Assert.Contains("requires confirmation by a second authorized employee", checkBefore.Message);

        // Confirmed by second authorized person (EMP-002 Head Accountant)
        await gateService.ConfirmRecordAsync(correction.RecordId, "Correction", "EMP-002", "Head Accountant");

        // After confirmation -> gate check is satisfied
        var checkAfter = await gateService.CheckGateAsync(CeoApprovalItemTypes.Reimbursement, "301", 12000m);
        Assert.True(checkAfter.IsSatisfied);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Gate 10: Changed Threshold Does Not Reopen Old Approvals
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task ChangedThreshold_DoesNotReopenOldApprovals()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        // 1. Request created on past date when threshold was 50,000
        var pastDate = DateTime.UtcNow.AddMonths(-2);
        context.CeoApprovalThresholdSettings.Add(new CeoApprovalThresholdSetting
        {
            SettingId = 10,
            SettingKey = "CSH-09_CEO_THRESHOLD",
            ThresholdAmount = 50000m,
            EffectiveFrom = pastDate.AddDays(-1),
            EffectiveTo = pastDate.AddDays(30),
            IsActive = false,
            Currency = "PHP",
            UpdatedBy = "EMP-001",
            UpdatedAt = pastDate.AddDays(-1)
        });

        // Current setting is 15,000
        context.CeoApprovalThresholdSettings.Add(new CeoApprovalThresholdSetting
        {
            SettingId = 11,
            SettingKey = "CSH-09_CEO_THRESHOLD",
            ThresholdAmount = 15000m,
            EffectiveFrom = pastDate.AddDays(30),
            EffectiveTo = null,
            IsActive = true,
            Currency = "PHP",
            UpdatedBy = "EMP-001",
            UpdatedAt = pastDate.AddDays(30)
        });
        await context.SaveChangesAsync();

        // Transaction of 20,000 evaluated against pastDate (when limit was 50,000)
        // Does NOT require CEO approval
        var checkPast = await gateService.CheckGateAsync(CeoApprovalItemTypes.CashOutflow, "CA-2026-001", 20000m, pastDate);
        Assert.False(checkPast.IsRequired);
        Assert.True(checkPast.IsSatisfied);
        Assert.Equal(50000m, checkPast.ThresholdInForce);

        // Transaction of 20,000 evaluated against today (when limit is 15,000)
        // Strictly requires CEO approval
        var checkToday = await gateService.CheckGateAsync(CeoApprovalItemTypes.CashOutflow, "CA-2026-001", 20000m, DateTime.UtcNow);
        Assert.True(checkToday.IsRequired);
        Assert.False(checkToday.IsSatisfied);
        Assert.Equal(15000m, checkToday.ThresholdInForce);

        // Record approval for today's transaction
        await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.CashOutflow,
            ItemId = "CA-2026-001",
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow.Date
        }, "EMP-001", "Finance Manager");

        // Gate is now satisfied
        var checkTodayApproved = await gateService.CheckGateAsync(CeoApprovalItemTypes.CashOutflow, "CA-2026-001", 20000m, DateTime.UtcNow);
        Assert.True(checkTodayApproved.IsSatisfied);

        // Later threshold change (e.g. lowering to 12,000 or raising to 100,000) does not invalidate/reopen the approval
        await gateService.UpdateThresholdAsync(new UpdateCeoThresholdRequest
        {
            ThresholdAmount = 100000m,
            Reason = "Executive board threshold increase"
        }, "EMP-001", "Finance Manager");

        var checkStillApproved = await gateService.CheckGateAsync(CeoApprovalItemTypes.CashOutflow, "CA-2026-001", 20000m, DateTime.UtcNow);
        Assert.True(checkStillApproved.IsSatisfied);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Gate 11: Item 3 (Budget Carry-Over), Item 7 (Discount), Item 8 (AFS) Gate Checks
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task CarryOver_Discount_And_AFS_Gates_BlockUntilTick()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        // Item 3: BudgetCarryOver
        var checkCarryOver = await gateService.CheckGateAsync(CeoApprovalItemTypes.BudgetCarryOver, "CO-2026-01");
        Assert.True(checkCarryOver.IsRequired);
        Assert.False(checkCarryOver.IsSatisfied);

        // Item 7: InvoiceDiscount
        var checkDiscount = await gateService.CheckGateAsync(CeoApprovalItemTypes.InvoiceDiscount, "INV-2026-001");
        Assert.True(checkDiscount.IsRequired);
        Assert.False(checkDiscount.IsSatisfied);

        // Item 8: AnnualFinancialStatement
        var checkAfs = await gateService.CheckGateAsync(CeoApprovalItemTypes.AnnualFinancialStatement, "2026");
        Assert.True(checkAfs.IsRequired);
        Assert.False(checkAfs.IsSatisfied);

        // Record Item 8 approval with optional attachment
        byte[] pdfBytes = Encoding.UTF8.GetBytes("%PDF-1.5 Signed AFS Audit Report");
        await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.AnnualFinancialStatement,
            ItemId = "2026",
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow.Date,
            AttachmentBytes = pdfBytes,
            AttachmentFileName = "AFS_2026_Signed.pdf"
        }, "EMP-001", "Finance Manager");

        var checkAfsAfter = await gateService.CheckGateAsync(CeoApprovalItemTypes.AnnualFinancialStatement, "2026");
        Assert.True(checkAfsAfter.IsSatisfied);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Gate 12: Rejection Sets Item Status and Releases Budget Reservation
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Rejection_SetsItemToRejected_AndReleasesBudgetReservation()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        // Seed monthly budget line with committed amount
        var budgetLine = new MonthlyBudgetLine
        {
            BudgetLineId = 55,
            FiscalYear = 2026,
            DepartmentCode = "DISPATCH",
            Category = "Fuel",
            Month = 1,
            BudgetAmount = 100000m,
            CommittedAmount = 15000m,
            ActualAmount = 0m,
            CreatedAt = DateTime.UtcNow
        };
        context.MonthlyBudgetLines.Add(budgetLine);
        await context.SaveChangesAsync();

        // Record Rejection for CA-2026-001 (amount 15,000)
        var record = await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.CashOutflow,
            ItemId = "CA-2026-001",
            Status = "Rejected",
            ApprovalDate = DateTime.UtcNow.Date,
            RejectionReason = "Trip deferred by management"
        }, "EMP-001", "Finance Manager");

        Assert.Equal("Rejected", record.Status);
        Assert.Equal("Trip deferred by management", record.RejectionReason);

        // Verify CashAdvance status is updated to Rejected
        var updatedAdvance = await context.CashAdvances.FirstAsync(a => a.AdvanceId == "CA-2026-001");
        Assert.Equal("Rejected", updatedAdvance.Status);
        Assert.Equal("Trip deferred by management", updatedAdvance.RejectionReason);

        // Verify committed amount on budget line was released from 15,000 to 0
        var updatedLine = await context.MonthlyBudgetLines.FirstAsync(l => l.BudgetLineId == 55);
        Assert.Equal(0m, updatedLine.CommittedAmount);
    }
}
