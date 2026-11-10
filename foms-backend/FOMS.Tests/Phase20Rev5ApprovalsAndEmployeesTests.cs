using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FOMS.Application.DTOs;
using FOMS.Application.Interfaces;
using FOMS.Application.Services;
using FOMS.Domain.Constants;
using FOMS.Domain.Entities.SqlServer;
using FOMS.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

// Explicit aliases to avoid ambiguity between FOMS.Domain.Entities.Invoice and FOMS.Domain.Entities.SqlServer.Invoice
using DomainInvoice = FOMS.Domain.Entities.Invoice;
using InvoiceDocumentStatus = FOMS.Domain.Entities.InvoiceDocumentStatus;
using DomainEmployee = FOMS.Domain.Entities.Employee;

namespace FOMS.Tests;

public class Phase20Rev5ApprovalsAndEmployeesTests
{
    private const string SqlConnString = "Server=localhost\\SQLEXPRESS;Database=FOMS_DB;Trusted_Connection=True;TrustServerCertificate=True;Connection Timeout=15;";

    private ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var context = new ApplicationDbContext(options);

        // Seed basic users
        context.Employees.AddRange(
            new DomainEmployee { Id = "EMP-001", Name = "Finance Manager Test", Role = "Finance Manager", Position = "Finance Manager", Email = "fm@foms.local", Username = "fm", PasswordHash = "hash", SystemAccess = "FOMS", Status = "Active" },
            new DomainEmployee { Id = "EMP-002", Name = "Head Accountant Test", Role = "Head Accountant", Position = "Head Accountant", Email = "ha@foms.local", Username = "ha", PasswordHash = "hash", SystemAccess = "FOMS", Status = "Active" },
            new DomainEmployee { Id = "EMP-003", Name = "Accountant Test", Role = "Accountant", Position = "Accountant", Email = "acc@foms.local", Username = "acc", PasswordHash = "hash", SystemAccess = "FOMS", Status = "Active" },
            new DomainEmployee { Id = "EMP-004", Name = "Assistant FM Test", Role = "Assistant of Finance Manager", Position = "Assistant of Finance Manager", Email = "afm@foms.local", Username = "afm", PasswordHash = "hash", SystemAccess = "FOMS", Status = "Active" }
        );

        // Seed Limit A
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

        // Seed CSH-09 CEO threshold
        context.CeoApprovalThresholdSettings.Add(new CeoApprovalThresholdSetting
        {
            SettingId = 1,
            SettingKey = "CSH-09_CEO_THRESHOLD",
            ThresholdAmount = 10000.00m,
            Currency = "PHP",
            EffectiveFrom = DateTime.UtcNow.AddYears(-1),
            IsActive = true,
            UpdatedBy = "EMP-001",
            UpdatedAt = DateTime.UtcNow.AddYears(-1)
        });

        // Seed items for all 8 gate categories
        context.AnnualBudgets.Add(new AnnualBudget { AnnualBudgetId = 1, FiscalYear = 2026, Status = "Draft", TotalBudget = 1000000m, PreparedBy = "EMP-001" });

        // BudgetOverride: uses BudgetLineId (FK) not FiscalYear directly on the entity
        context.MonthlyBudgetLines.Add(new MonthlyBudgetLine
        {
            BudgetLineId = 1,
            AnnualBudgetId = 1,
            FiscalYear = 2026,
            Month = 11,
            DepartmentCode = "DEP-FIN",
            DepartmentName = "Finance",
            Category = "Operating",
            BudgetAmount = 100000m,
            CommittedAmount = 25000m,
            ActualAmount = 0m
        });

        context.BudgetOverrides.Add(new BudgetOverride { OverrideId = 1, BudgetLineId = 1, RequestedAmount = 15000m, OverBudgetAmount = 5000m, Reason = "Emergency", Status = "Pending", RequestedBy = "EMP-001" });
        context.BudgetTransfers.Add(new BudgetTransfer { TransferId = 1, FiscalYear = 2026, FromDepartmentCode = "DEP-FIN", FromCategory = "Operating", FromMonth = 11, ToDepartmentCode = "DEP-CCS", ToCategory = "Operating", ToMonth = 11, Amount = 5000m, Reason = "Reallocation", Status = "Pending", RequestedBy = "EMP-001" });

        context.CashAdvances.Add(new CashAdvance { AdvanceId = "CA-2026-001", SubmittedByEmployeeId = "EMP-004", AmountGiven = 25000m, DepartmentCode = "DEP-FIN", Category = "Operating", FiscalYear = 2026, Month = 11, Status = "PendingApproval" });
        context.Reimbursements.Add(new Reimbursement { ReimbursementId = 1, EmployeeId = "EMP-004", Amount = 15000m, DepartmentCode = "DEP-FIN", Category = "Operating", FiscalYear = 2026, Month = 11, Status = "Submitted", EmployeeName = "Assistant FM Test", Purpose = "Fuel" });
        context.DirectDisbursements.Add(new DirectDisbursement { DisbursementId = 1, Amount = 50000m, DepartmentCode = "DEP-FIN", Category = "Operating", FiscalYear = 2026, Month = 11, Status = "Committed", RequestedBy = "EMP-001", Purpose = "Office Supplies" });
        context.Invoices.Add(new DomainInvoice { Id = "INV-2026-001", InvoiceNo = "INV-2026-001", ClientId = "CLI-001", TotalAmount = 50000m, Balance = 50000m, DocumentStatus = InvoiceDocumentStatus.ForReview });

        context.SaveChanges();
        return context;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. All 8 Gated Items Block Until Approved
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GateCheck_All8Items_BlockUntilApproved()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        // 1. Annual budget allocation
        var check1 = await gateService.CheckGateAsync(CeoApprovalItemTypes.AnnualBudget, "2026");
        Assert.True(check1.IsRequired);
        Assert.False(check1.IsSatisfied);

        // 2. Over-budget spend override
        var check2 = await gateService.CheckGateAsync(CeoApprovalItemTypes.BudgetOverride, "1", amount: 15000m);
        Assert.True(check2.IsRequired);
        Assert.False(check2.IsSatisfied);

        // 3. Budget transfer
        var check3 = await gateService.CheckGateAsync(CeoApprovalItemTypes.BudgetTransfer, "1");
        Assert.True(check3.IsRequired);
        Assert.False(check3.IsSatisfied);

        // 4. Advance above CSH-09
        var check4 = await gateService.CheckGateAsync(CeoApprovalItemTypes.CashOutflow, "CA-2026-001", amount: 25000m);
        Assert.True(check4.IsRequired);
        Assert.False(check4.IsSatisfied);

        // 5. Reimbursement above CSH-09
        var check5 = await gateService.CheckGateAsync(CeoApprovalItemTypes.Reimbursement, "1", amount: 15000m);
        Assert.True(check5.IsRequired);
        Assert.False(check5.IsSatisfied);

        // 6. Direct disbursement above CSH-09
        var check6 = await gateService.CheckGateAsync(CeoApprovalItemTypes.CashOutflow, "1", amount: 50000m);
        Assert.True(check6.IsRequired);
        Assert.False(check6.IsSatisfied);

        // 7. Bad debt write-off
        var check7 = await gateService.CheckGateAsync(CeoApprovalItemTypes.BadDebtWriteOff, "INV-2026-001");
        Assert.True(check7.IsRequired);
        Assert.False(check7.IsSatisfied);

        // 8. Annual financial statement
        var check8 = await gateService.CheckGateAsync(CeoApprovalItemTypes.AnnualFinancialStatement, "2026");
        Assert.True(check8.IsRequired);
        Assert.False(check8.IsSatisfied);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. Future Approval Date Rejected
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task CreateCeoApprovalRecord_FutureApprovalDate_ThrowsArgumentException()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        var futureRequest = new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.AnnualBudget,
            ItemId = "2026",
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow.AddDays(2).Date,
            Notes = "Board approval from tomorrow"
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            gateService.RecordApprovalAsync(futureRequest, "EMP-001", "Finance Manager"));

        Assert.Contains("cannot be in the future", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. Saved Record Cannot Be Updated or Deleted (Trigger Enforcement)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task CeoApprovalRecords_ImmutableTrigger_PreventsUpdateAndDelete()
    {
        bool sqlAvailable = false;
        try
        {
            using var testConn = new SqlConnection(SqlConnString);
            await testConn.OpenAsync();
            sqlAvailable = true;
        }
        catch
        {
            // Ignore if local SQL Server is not reachable in this test runner environment
        }

        if (!sqlAvailable) return;

        using var conn = new SqlConnection(SqlConnString);
        await conn.OpenAsync();

        // Insert a test record directly
        string insertSql = @"
            INSERT INTO [dbo].[CeoApprovalRecords] 
            (ItemType, ItemId, Status, ApproverLabel, EnteredBy, EnteredAt, ApprovalDate, Notes, HasAttachment, IsCorrection, IsActive)
            VALUES ('AnnualBudget', '2099', 'Approved', 'CEO/President (in person)', 'EMP-001', SYSUTCDATETIME(), '2026-11-10', 'Test Immutability', 0, 0, 1);
            SELECT SCOPE_IDENTITY();";

        int insertedId;
        using (var insertCmd = new SqlCommand(insertSql, conn))
        {
            insertedId = Convert.ToInt32(await insertCmd.ExecuteScalarAsync());
        }

        try
        {
            // Attempt UPDATE -> must throw SqlException
            string updateSql = $"UPDATE [dbo].[CeoApprovalRecords] SET Notes = 'Tampered' WHERE RecordId = {insertedId};";
            using (var updateCmd = new SqlCommand(updateSql, conn))
            {
                var updateEx = await Assert.ThrowsAsync<SqlException>(() => updateCmd.ExecuteNonQueryAsync());
                Assert.Contains("immutable", updateEx.Message, StringComparison.OrdinalIgnoreCase);
            }

            // Attempt DELETE -> must throw SqlException
            string deleteSql = $"DELETE FROM [dbo].[CeoApprovalRecords] WHERE RecordId = {insertedId};";
            using (var deleteCmd = new SqlCommand(deleteSql, conn))
            {
                var deleteEx = await Assert.ThrowsAsync<SqlException>(() => deleteCmd.ExecuteNonQueryAsync());
                Assert.Contains("immutable", deleteEx.Message, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            // Clean up test row using administrative disable/re-enable trigger
            string cleanupSql = $@"
                ALTER TABLE [dbo].[CeoApprovalRecords] DISABLE TRIGGER [TR_CeoApprovalRecords_Immutable];
                DELETE FROM [dbo].[CeoApprovalRecords] WHERE RecordId = {insertedId};
                ALTER TABLE [dbo].[CeoApprovalRecords] ENABLE TRIGGER [TR_CeoApprovalRecords_Immutable];";
            using var cleanupCmd = new SqlCommand(cleanupSql, conn);
            await cleanupCmd.ExecuteNonQueryAsync();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. Only Finance Manager Can Record (403 for other roles)
    // ─────────────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("Head Accountant")]
    [InlineData("Accountant")]
    [InlineData("Assistant of Finance Manager")]
    [InlineData("Encoder")]
    public async Task CreateCeoApprovalRecord_NonFinanceManager_ThrowsUnauthorized(string role)
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

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            gateService.RecordApprovalAsync(request, "EMP-002", role));

        Assert.Contains("Only the Finance Manager", ex.Message);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5. Rejection Requires Reason and Releases Committed Reservation
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task CreateCeoApprovalRecord_RejectionWithoutReason_ThrowsArgumentException()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        var request = new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.CashOutflow,
            ItemId = "CA-2026-001",
            Status = "Rejected",
            ApprovalDate = DateTime.UtcNow.Date,
            RejectionReason = "" // Missing reason
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            gateService.RecordApprovalAsync(request, "EMP-001", "Finance Manager"));

        Assert.Contains("RejectionReason is strictly mandatory", ex.Message);
    }

    [Fact]
    public async Task CreateCeoApprovalRecord_Rejection_ReleasesCommittedBudget()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        var lineBefore = await context.MonthlyBudgetLines.FirstAsync(l => l.BudgetLineId == 1);
        Assert.Equal(25000m, lineBefore.CommittedAmount);

        var request = new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.CashOutflow,
            ItemId = "CA-2026-001",
            Status = "Rejected",
            ApprovalDate = DateTime.UtcNow.Date,
            RejectionReason = "Exceeds monthly limit"
        };

        await gateService.RecordApprovalAsync(request, "EMP-001", "Finance Manager");

        var lineAfter = await context.MonthlyBudgetLines.FirstAsync(l => l.BudgetLineId == 1);
        Assert.Equal(0m, lineAfter.CommittedAmount);

        var advance = await context.CashAdvances.FirstAsync(a => a.AdvanceId == "CA-2026-001");
        Assert.Equal("Rejected", advance.Status);
        Assert.Equal("Exceeds monthly limit", advance.RejectionReason);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 6. G1: Attachment Gating Matrix for All 8 Items (R5.3)
    //    Items 1, 6, 8 allow attachments (8 mandatory, 1 & 6 optional).
    //    Items 2, 3, 4, 5, 7 strictly forbid attachments.
    // ─────────────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData(CeoApprovalItemTypes.BudgetTransfer, "101")]
    [InlineData(CeoApprovalItemTypes.BudgetCarryOver, "102")]
    [InlineData(CeoApprovalItemTypes.BudgetOverride, "103")]
    [InlineData(CeoApprovalItemTypes.InvoiceDiscount, "INV-001")]
    [InlineData(CeoApprovalItemTypes.BadDebtWriteOff, "INV-002")]
    public async Task CreateCeoApprovalRecord_TickOnlyItems_RejectAttachment_And_SucceedWithoutAttachment(string itemType, string itemId)
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        // Seed referenced entity for existence validation
        if (itemType == CeoApprovalItemTypes.BudgetTransfer || itemType == CeoApprovalItemTypes.BudgetCarryOver)
            context.BudgetTransfers.Add(new BudgetTransfer { TransferId = int.Parse(itemId), Status = "Pending", Amount = 5000 });
        else if (itemType == CeoApprovalItemTypes.BudgetOverride)
            context.BudgetOverrides.Add(new BudgetOverride { OverrideId = int.Parse(itemId), Status = "Pending", RequestedAmount = 5000 });

        await context.SaveChangesAsync();

        // A. With attachment -> Throws ArgumentException
        var reqWithAttach = new CreateCeoApprovalRecordRequest
        {
            ItemType = itemType,
            ItemId = itemId,
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow.Date,
            AttachmentBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 },
            AttachmentFileName = "doc.pdf"
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            gateService.RecordApprovalAsync(reqWithAttach, "EMP-001", "Finance Manager"));
        Assert.Contains("Attachments are permitted only on items 1, 6, and 8", ex.Message);

        // B. Without attachment -> Succeeds
        var reqWithoutAttach = new CreateCeoApprovalRecordRequest
        {
            ItemType = itemType,
            ItemId = itemId,
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow.Date
        };

        var result = await gateService.RecordApprovalAsync(reqWithoutAttach, "EMP-001", "Finance Manager");
        Assert.NotNull(result);
        Assert.Equal("Approved", result.Status);
        Assert.False(result.HasAttachment);
        Assert.Equal("N/A", result.AttachmentFlag);
    }

    [Theory]
    [InlineData(CeoApprovalItemTypes.AnnualBudget, "2026")]
    [InlineData(CeoApprovalItemTypes.CashOutflow, "CA-2026-001")] // Cash advance outflow
    [InlineData(CeoApprovalItemTypes.CashOutflow, "10")] // Direct disbursement outflow
    [InlineData(CeoApprovalItemTypes.CashOutflow, "REIM-2026-001")] // Reimbursement outflow
    [InlineData(CeoApprovalItemTypes.AnnualFinancialStatement, "2026")] // Item 8 AFS
    public async Task CreateCeoApprovalRecord_OptionalAttachmentItems_SucceedWithOrWithoutAttachment(string itemType, string itemId)
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        string itemIdWithoutAttach;
        if (itemId == "CA-2026-001")
        {
            context.CashAdvances.Add(new CashAdvance { AdvanceId = "CA-2026-001-NOATTACH", AmountGiven = 15000, Status = "Submitted", DepartmentCode = "DEP-FIN" });
            itemIdWithoutAttach = "CA-2026-001-NOATTACH";
        }
        else if (itemId == "10")
        {
            context.DirectDisbursements.Add(new DirectDisbursement { DisbursementId = 10, Amount = 15000, Status = "Pending", DepartmentCode = "DEP-FIN" });
            context.DirectDisbursements.Add(new DirectDisbursement { DisbursementId = 11, Amount = 15000, Status = "Pending", DepartmentCode = "DEP-FIN" });
            itemIdWithoutAttach = "11";
        }
        else if (itemId == "REIM-2026-001")
        {
            var reim1 = await context.Reimbursements.FirstAsync(r => r.ReimbursementId == 1);
            reim1.ReferenceNumber = "REIM-2026-001";
            context.Reimbursements.Add(new Reimbursement { ReimbursementId = 2, ReferenceNumber = "REIM-2026-001-NOATTACH", Amount = 15000, Status = "Approved", DepartmentCode = "DEP-FIN" });
            itemIdWithoutAttach = "REIM-2026-001-NOATTACH";
        }
        else if (itemType == CeoApprovalItemTypes.AnnualFinancialStatement)
        {
            itemIdWithoutAttach = "2027";
        }
        else // AnnualBudget "2026"
        {
            context.AnnualBudgets.Add(new AnnualBudget { AnnualBudgetId = 2, FiscalYear = 2027, Status = "Draft", TotalBudget = 1000000m, PreparedBy = "EMP-001" });
            itemIdWithoutAttach = "2027";
        }
        await context.SaveChangesAsync();

        // A. With attachment -> Succeeds
        var reqWithAttach = new CreateCeoApprovalRecordRequest
        {
            ItemType = itemType,
            ItemId = itemId,
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow.Date,
            AttachmentBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 },
            AttachmentFileName = "voucher.pdf"
        };

        var resWith = await gateService.RecordApprovalAsync(reqWithAttach, "EMP-001", "Finance Manager");
        Assert.NotNull(resWith);
        Assert.True(resWith.HasAttachment);
        Assert.Equal("Attached", resWith.AttachmentFlag);

        // B. Without attachment -> Succeeds (optional)
        var reqWithoutAttach = new CreateCeoApprovalRecordRequest
        {
            ItemType = itemType,
            ItemId = itemIdWithoutAttach,
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow.Date
        };

        var resWithout = await gateService.RecordApprovalAsync(reqWithoutAttach, "EMP-001", "Finance Manager");
        Assert.NotNull(resWithout);
        Assert.False(resWithout.HasAttachment);
        Assert.Equal("No attachment", resWithout.AttachmentFlag);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 7. Overhead Hidden & Rejected for Non-FM
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Overhead_RejectedForNonFinanceManager()
    {
        // Allowed for FM
        var fmResult = FomsDepartments.Normalize("DEP-OVH", "Finance Manager", isNewRequest: true);
        Assert.Equal("DEP-OVH", fmResult);

        // Blocked for all non-FM
        Assert.Throws<UnauthorizedAccessException>(() =>
            FomsDepartments.Normalize("DEP-OVH", "Accountant", isNewRequest: true));

        Assert.Throws<UnauthorizedAccessException>(() =>
            FomsDepartments.Normalize("DEP-OVH", "Head Accountant", isNewRequest: true));

        Assert.Throws<UnauthorizedAccessException>(() =>
            FomsDepartments.Normalize("DEP-OVH", "Assistant of Finance Manager", isNewRequest: true));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 8. Threshold Tested as of Request Date
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GateCheck_EvaluatesThresholdAsOfRequestDate()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        var pastDate = DateTime.UtcNow.AddMonths(-3);

        // Update threshold today to 50,000
        await gateService.UpdateThresholdAsync(new UpdateCeoThresholdRequest
        {
            ThresholdAmount = 50000m,
            EffectiveFrom = DateTime.UtcNow.AddDays(-1),
            Reason = "Board increased limit"
        }, "EMP-001", "Finance Manager");

        // Request with past date evaluated against 10,000 threshold
        var checkPast = await gateService.CheckGateAsync(CeoApprovalItemTypes.CashOutflow, "CA-2026-001", amount: 20000m, requestDate: pastDate);
        Assert.True(checkPast.IsRequired);
        Assert.Equal(10000m, checkPast.ThresholdInForce);

        // Request with today evaluated against 50,000 threshold -> 20,000 is below threshold
        var checkToday = await gateService.CheckGateAsync(CeoApprovalItemTypes.CashOutflow, "CA-2026-001", amount: 20000m, requestDate: DateTime.UtcNow);
        Assert.False(checkToday.IsRequired);
        Assert.Equal(50000m, checkToday.ThresholdInForce);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 9. Unset Threshold Fails Safe (Tick Required)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GateCheck_UnsetThreshold_FailsSafeRequiringApproval()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        // Deactivate existing thresholds so it is unconfigured
        var settings = await context.CeoApprovalThresholdSettings.ToListAsync();
        foreach (var s in settings) s.IsActive = false;
        await context.SaveChangesAsync();

        // Amount above fail-safe Limit A (10,000) strictly requires approval
        var checkAbove = await gateService.CheckGateAsync(CeoApprovalItemTypes.CashOutflow, "CA-2026-001", amount: 15000m);
        Assert.True(checkAbove.IsRequired);
        Assert.False(checkAbove.IsSatisfied);
        Assert.True(checkAbove.UsedFailSafeLimit);

        // Without amount strictly requires approval
        var checkNull = await gateService.CheckGateAsync(CeoApprovalItemTypes.CashOutflow, "CA-2026-001", amount: null);
        Assert.True(checkNull.IsRequired);
        Assert.False(checkNull.IsSatisfied);
        Assert.True(checkNull.UsedFailSafeLimit);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 10. Server Rejects Bad Position/Role Combinations and Client Role
    // ─────────────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("Client", "Client")]
    [InlineData("Finance Manager", "None")]
    [InlineData("Courier", "Finance Manager")]
    [InlineData("", "Accountant")]
    public void UserService_RejectsInvalidPositionAndRole(string position, string role)
    {
        // Client and empty position throw ArgumentException or InvalidOperationException (both derive from Exception)
        Assert.ThrowsAny<Exception>(() =>
            UserService.ValidateAndAlignPositionAndRole(position, role, dmsRole: null));
    }

    [Fact]
    public void UserService_NonFinanceRequiresDmsRole()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            UserService.ValidateAndAlignPositionAndRole("Courier", "None", dmsRole: ""));

        Assert.Contains("DMS operational role is required", ex.Message);
    }

    [Fact]
    public void UserService_FinanceSetsDmsRoleNone()
    {
        // For finance staff, DMS role defaults to 'None' when not provided or explicitly 'None'
        var (derivedPosition, derivedRole, resolvedDmsRole) = UserService.ValidateAndAlignPositionAndRole("Head Accountant", "Head Accountant", dmsRole: null);
        Assert.Equal("Head Accountant", derivedRole);
        Assert.Equal("None", resolvedDmsRole);

        // Active DMS role for finance staff is rejected as an invalid combination
        Assert.Throws<InvalidOperationException>(() =>
            UserService.ValidateAndAlignPositionAndRole("Head Accountant", "Head Accountant", dmsRole: "Admin"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 11. Codebase Grep: Zero Dependency on CEO/President in Auth / Role Code
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Codebase_HasZeroCeoOrPresidentRoleChecksInServices()
    {
        var solutionDir = Directory.GetCurrentDirectory();
        // Traverse up to repository root
        while (!File.Exists(Path.Combine(solutionDir, "CAPSTONE2.sln")) &&
               !File.Exists(Path.Combine(solutionDir, "FOMS.sln")) &&
               Directory.GetParent(solutionDir) != null)
        {
            solutionDir = Directory.GetParent(solutionDir)!.FullName;
        }

        var backendDir = Path.Combine(solutionDir, "foms-backend");
        if (!Directory.Exists(backendDir))
        {
            backendDir = Path.Combine(Directory.GetCurrentDirectory(), "..");
        }

        var sourceFiles = Directory.GetFiles(backendDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("FOMS.Tests") && !f.Contains("obj") && !f.Contains("bin") && !f.Contains("Migrations"))
            .ToList();

        var pattern = new Regex(@"IsCeoOrPresident|\bRole\s*==\s*[""'](CEO|President)[""']|\bUserRole\s*==\s*[""'](CEO|President)[""']", RegexOptions.IgnoreCase);

        var offendingLines = (from file in sourceFiles
            let lines = File.ReadAllLines(file)
            from line in lines.Select((text, index) => new { text, index, file })
            where pattern.IsMatch(line.text)
            select $"{Path.GetFileName(line.file)}:{line.index + 1}: {line.text.Trim()}").ToList();

        Assert.True(offendingLines.Count == 0,
            $"Found forbidden CEO/President role checks:\n{string.Join("\n", offendingLines)}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 12. Codebase Grep: Zero Old Role Names in Non-Migration Code
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Codebase_HasZeroOldRoleNamesInNonMigrationCode()
    {
        var solutionDir = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(solutionDir, "CAPSTONE2.sln")) &&
               !File.Exists(Path.Combine(solutionDir, "FOMS.sln")) &&
               Directory.GetParent(solutionDir) != null)
        {
            solutionDir = Directory.GetParent(solutionDir)!.FullName;
        }

        var backendDir = Path.Combine(solutionDir, "foms-backend");
        if (!Directory.Exists(backendDir))
        {
            backendDir = Path.Combine(Directory.GetCurrentDirectory(), "..");
        }

        var sourceFiles = Directory.GetFiles(backendDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("FOMS.Tests") && !f.Contains("obj") && !f.Contains("bin") && !f.Contains("Migrations"))
            .ToList();

        // Check for old role names: "Financial Manager" and "Assistant Financial Manager" (unless part of comment or regex)
        var oldRolePattern = new Regex(@"""(Financial Manager|Assistant Financial Manager)""", RegexOptions.IgnoreCase);

        var offendingLines = (from file in sourceFiles
            let lines = File.ReadAllLines(file)
            from line in lines.Select((text, index) => new { text, index, file })
            where oldRolePattern.IsMatch(line.text)
            select $"{Path.GetFileName(line.file)}:{line.index + 1}: {line.text.Trim()}").ToList();

        Assert.True(offendingLines.Count == 0,
            $"Found old role name strings:\n{string.Join("\n", offendingLines)}");
    }
}
