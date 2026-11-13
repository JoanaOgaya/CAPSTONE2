using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using ClosedXML.Excel;
using FOMS.Application.Configuration;
using FOMS.Application.Contracts;
using FOMS.Application.DTOs;
using FOMS.Application.Features;
using FOMS.Application.Interfaces;
using FOMS.Application.Services;
using FOMS.Domain.Constants;
using FOMS.Domain.Entities.SqlServer;
using FOMS.Infrastructure.Persistence;
using FOMS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

// Explicit aliases
using DomainInvoice = FOMS.Domain.Entities.Invoice;
using InvoiceDocumentStatus = FOMS.Domain.Entities.InvoiceDocumentStatus;
using DomainEmployee = FOMS.Domain.Entities.Employee;
using DomainRole = FOMS.Domain.Entities.Role;
using DomainAdjustment = FOMS.Domain.Entities.PaymentAdjustment;
using DomainPeriodClose = FOMS.Domain.Entities.AccountingPeriodClose;

namespace FOMS.Tests;

public class Phase22Rev5CorrectionsTests
{
    private ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var context = new ApplicationDbContext(options);

        string testHash = AuthService.HashPassword("TestPass123!");

        // Seed basic users
        context.Employees.AddRange(
            new DomainEmployee { Id = "EMP-001", Name = "Finance Manager Test", Role = "Finance Manager", Position = "Finance Manager", Email = "fm@foms.local", Username = "fm", PasswordHash = testHash, SystemAccess = "FOMS", Status = "Active", IsActive = true },
            new DomainEmployee { Id = "EMP-002", Name = "Head Accountant Test", Role = "Head Accountant", Position = "Head Accountant", Email = "ha@foms.local", Username = "ha", PasswordHash = testHash, SystemAccess = "FOMS", Status = "Active", IsActive = true },
            new DomainEmployee { Id = "EMP-003", Name = "Accountant Test", Role = "Accountant", Position = "Accountant", Email = "acc@foms.local", Username = "acc", PasswordHash = testHash, SystemAccess = "FOMS", Status = "Active", IsActive = true },
            new DomainEmployee { Id = "EMP-004", Name = "Assistant FM Test", Role = "Assistant of Finance Manager", Position = "Assistant of Finance Manager", Email = "afm@foms.local", Username = "afm", PasswordHash = testHash, SystemAccess = "FOMS", Status = "Active", IsActive = true },
            new DomainEmployee { Id = "EMP-005", Name = "Role 5 Logistics Staff", Role = "Logistics Staff", Position = "Logistics Staff", Email = "role5@foms.local", Username = "role5", PasswordHash = testHash, SystemAccess = "FOMS", Status = "Active", IsActive = true },
            new DomainEmployee { Id = "EMP-006", Name = "Role 6 Courier", Role = "Courier", Position = "Courier", Email = "role6@foms.local", Username = "role6", PasswordHash = testHash, SystemAccess = "FOMS", Status = "Active", IsActive = true }
        );

        // Seed roles in dbo.Roles
        context.Roles.AddRange(
            new DomainRole { Id = "1", Name = "Finance Manager", Description = "Finance Manager" },
            new DomainRole { Id = "2", Name = "Head Accountant", Description = "Head Accountant" },
            new DomainRole { Id = "3", Name = "Accountant", Description = "Accountant" },
            new DomainRole { Id = "4", Name = "Assistant of Finance Manager", Description = "Assistant of Finance Manager" },
            new DomainRole { Id = "5", Name = "Logistics Staff", Description = "Logistics Staff" },
            new DomainRole { Id = "6", Name = "Courier", Description = "Courier" }
        );

        // Seed base entities for all 8 gate items
        context.AnnualBudgets.Add(new AnnualBudget { AnnualBudgetId = 1, FiscalYear = 2026, Status = "Draft", TotalBudget = 1000000m, PreparedBy = "EMP-001" });
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
        context.MonthlyBudgetLines.Add(new MonthlyBudgetLine
        {
            BudgetLineId = 2,
            AnnualBudgetId = 1,
            FiscalYear = 2026,
            Month = 11,
            DepartmentCode = "DEP-CCS",
            DepartmentName = "Customer Care",
            Category = "Operating",
            BudgetAmount = 50000m,
            CommittedAmount = 0m,
            ActualAmount = 0m
        });
        context.BudgetOverrides.Add(new BudgetOverride { OverrideId = 1, BudgetLineId = 1, RequestedAmount = 15000m, OverBudgetAmount = 5000m, Reason = "Emergency", Status = "Pending", RequestedBy = "EMP-001" });
        context.BudgetTransfers.Add(new BudgetTransfer { TransferId = 1, FiscalYear = 2026, FromDepartmentCode = "DEP-FIN", FromCategory = "Operating", FromMonth = 11, ToDepartmentCode = "DEP-CCS", ToCategory = "Operating", ToMonth = 11, Amount = 5000m, Reason = "Reallocation", Status = "Pending", RequestedBy = "EMP-001" });
        context.BudgetTransfers.Add(new BudgetTransfer { TransferId = 2, FiscalYear = 2026, FromDepartmentCode = "DEP-FIN", FromCategory = "Operating", FromMonth = 11, ToDepartmentCode = "DEP-FIN", ToCategory = "Operating", ToMonth = 12, Amount = 3000m, Reason = "Year-end carryover", Status = "Pending", RequestedBy = "EMP-001" });

        context.CashAdvances.Add(new CashAdvance { AdvanceId = "CA-2026-001", SubmittedByEmployeeId = "EMP-004", AmountGiven = 25000m, DepartmentCode = "DEP-FIN", Category = "Operating", FiscalYear = 2026, Month = 11, Status = "PendingApproval" });
        context.Reimbursements.Add(new Reimbursement { ReimbursementId = 1, EmployeeId = "EMP-004", Amount = 15000m, DepartmentCode = "DEP-FIN", Category = "Operating", FiscalYear = 2026, Month = 11, Status = "Submitted", EmployeeName = "Assistant FM Test", Purpose = "Fuel" });
        context.DirectDisbursements.Add(new DirectDisbursement { DisbursementId = 1, Amount = 50000m, DepartmentCode = "DEP-FIN", Category = "Operating", FiscalYear = 2026, Month = 11, Status = "Committed", RequestedBy = "EMP-001", Purpose = "Office Supplies" });
        context.Invoices.Add(new DomainInvoice { Id = "INV-2026-001", InvoiceNo = "INV-2026-001", ClientId = "CLI-001", TotalAmount = 50000m, Balance = 50000m, DocumentStatus = InvoiceDocumentStatus.ForReview });

        context.SaveChanges();
        return context;
    }

    private FomsDbContext CreateInMemoryFomsDbContext()
    {
        var options = new DbContextOptionsBuilder<FomsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new FomsDbContext(options);
    }

    private AuthService CreateAuthService(ApplicationDbContext context)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:Secret"] = new string('k', 48),
            ["JwtSettings:Issuer"] = "FOMS",
            ["JwtSettings:Audience"] = "FOMS_Audience",
            ["JwtSettings:AccessTokenExpirationMinutes"] = "60",
            ["JwtSettings:RefreshTokenExpirationDays"] = "7"
        }).Build();

        return new AuthService(context, cfg, NullLogger<AuthService>.Instance);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. G1 Full Matrix: 8 Items x 3 Statuses (None, Rejected, Approved)
    // ─────────────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData(CeoApprovalItemTypes.AnnualBudget, "2026", 0)]
    [InlineData(CeoApprovalItemTypes.BudgetTransfer, "1", 0)]
    [InlineData(CeoApprovalItemTypes.BudgetCarryOver, "2", 0)]
    [InlineData(CeoApprovalItemTypes.BudgetOverride, "1", 15000)]
    [InlineData(CeoApprovalItemTypes.Reimbursement, "1", 15000)]
    [InlineData(CeoApprovalItemTypes.CashOutflow, "1", 50000)]
    [InlineData(CeoApprovalItemTypes.InvoiceDiscount, "INV-2026-001", 5000)]
    [InlineData(CeoApprovalItemTypes.AnnualFinancialStatement, "2026", 0)]
    public async Task G1_Matrix_NoRecord_BlocksGate(string itemType, string itemId, decimal amount)
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        var check = await gateService.CheckGateAsync(itemType, itemId, amount);
        Assert.True(check.IsRequired);
        Assert.False(check.IsSatisfied);
    }

    [Theory]
    [InlineData(CeoApprovalItemTypes.AnnualBudget, "2026", 0)]
    [InlineData(CeoApprovalItemTypes.BudgetTransfer, "1", 0)]
    [InlineData(CeoApprovalItemTypes.BudgetCarryOver, "2", 0)]
    [InlineData(CeoApprovalItemTypes.BudgetOverride, "1", 15000)]
    [InlineData(CeoApprovalItemTypes.Reimbursement, "1", 15000)]
    [InlineData(CeoApprovalItemTypes.CashOutflow, "1", 50000)]
    [InlineData(CeoApprovalItemTypes.InvoiceDiscount, "INV-2026-001", 5000)]
    [InlineData(CeoApprovalItemTypes.AnnualFinancialStatement, "2026", 0)]
    public async Task G1_Matrix_RejectedRecord_BlocksGate_AndStoresRejectionReason(string itemType, string itemId, decimal amount)
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        // Record a Rejection
        var req = new CreateCeoApprovalRecordRequest
        {
            ItemType = itemType,
            ItemId = itemId,
            Status = "Rejected",
            ApprovalDate = DateTime.UtcNow,
            RejectionReason = $"CEO declined request for {itemType} {itemId} due to fiscal policy."
        };
        var record = await gateService.RecordApprovalAsync(req, "EMP-001", "Finance Manager");
        Assert.Equal("Rejected", record.Status);
        Assert.False(string.IsNullOrWhiteSpace(record.RejectionReason));

        var check = await gateService.CheckGateAsync(itemType, itemId, amount);
        Assert.True(check.IsRequired);
        Assert.False(check.IsSatisfied);
        Assert.Contains("rejected", check.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(CeoApprovalItemTypes.BudgetTransfer)]
    [InlineData(CeoApprovalItemTypes.BudgetCarryOver)]
    [InlineData(CeoApprovalItemTypes.BudgetOverride)]
    [InlineData(CeoApprovalItemTypes.Reimbursement)]
    [InlineData(CeoApprovalItemTypes.InvoiceDiscount)]
    public async Task G1_Matrix_ProhibitedAttachmentItems_RejectAttachment(string itemType)
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        var req = new CreateCeoApprovalRecordRequest
        {
            ItemType = itemType,
            ItemId = "1",
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow,
            AttachmentBytes = Encoding.UTF8.GetBytes("Fake attachment"),
            AttachmentFileName = "test.pdf"
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            gateService.RecordApprovalAsync(req, "EMP-001", "Finance Manager"));
        Assert.Contains("permitted only on items 1, 6, and 8", ex.Message);
    }

    [Fact]
    public async Task G1_Matrix_Item8_Afs_WithoutAttachment_Succeeds_OptionalAttachment()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        var req = new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.AnnualFinancialStatement,
            ItemId = "2026",
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow,
            AttachmentBytes = null
        };

        var record = await gateService.RecordApprovalAsync(req, "EMP-001", "Finance Manager");
        Assert.NotNull(record);
        Assert.False(record.HasAttachment);
        Assert.Equal("No attachment", record.AttachmentFlag);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. Roles 5 & 6 Blocked Login & Role Assignment
    // ─────────────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("EMP-005", "TestPass123!")]
    [InlineData("EMP-006", "TestPass123!")]
    public async Task Roles5And6_PasswordLogin_Blocked(string employeeId, string password)
    {
        using var context = CreateInMemoryDbContext();
        var authService = CreateAuthService(context);

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            authService.LoginAsync(employeeId, password));
        Assert.Contains("User is not assigned an authorized FOMS staff role", ex.Message);
    }

    [Theory]
    [InlineData("Logistics Staff")]
    [InlineData("Courier")]
    [InlineData("Client")]
    public async Task Roles5And6_RoleAssignment_RejectedByUserService(string forbiddenRole)
    {
        using var context = CreateInMemoryDbContext();
        var userService = new UserService(context);

        var createDto = new UserCreateDto
        {
            EmployeeId = "EMP-099",
            Username = "EMP-099",
            FullName = "Disallowed Role User",
            Email = "disallowed@foms.local",
            Position = "Finance Manager",
            Role = forbiddenRole
        };

        await Assert.ThrowsAnyAsync<Exception>(() => userService.CreateAsync(createDto));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. Head Accountant Confirmation Blocks Transfers & Carry-Overs; HA Budget Approval Blocked
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task HeadAccountantConfirmation_BlocksTransferAndCarryOver_UntilConfirmed()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        // 1. FM records CEO approval for BudgetTransfer
        var transferRecord = await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.BudgetTransfer,
            ItemId = "1",
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow,
            Notes = "Approved by CEO in person"
        }, "EMP-001", "Finance Manager");

        // Before HA confirmation, gate is NOT satisfied
        var preCheck = await gateService.CheckGateAsync(CeoApprovalItemTypes.BudgetTransfer, "1");
        Assert.True(preCheck.IsRequired);
        Assert.False(preCheck.IsSatisfied);
        Assert.Contains("requires Head Accountant maker-checker recording confirmation", preCheck.Message);

        // 2. Non-HA attempts confirmation -> throws UnauthorizedAccessException
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            gateService.ConfirmRecordAsync(transferRecord.RecordId, "Recording", "EMP-004", "Assistant of Finance Manager"));

        // 3. HA confirms -> gate is satisfied!
        await gateService.ConfirmRecordAsync(transferRecord.RecordId, "Recording", "EMP-002", "Head Accountant");

        var postCheck = await gateService.CheckGateAsync(CeoApprovalItemTypes.BudgetTransfer, "1");
        Assert.True(postCheck.IsRequired);
        Assert.True(postCheck.IsSatisfied);
    }

    [Fact]
    public async Task HeadAccountant_AttemptingBudgetApproval_ThrowsUnauthorizedAccessException()
    {
        using var context = CreateInMemoryDbContext();
        var budgetService = new BudgetService(context);

        // HA cannot approve annual budget
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            budgetService.ApproveAnnualBudgetAsync(1, "EMP-002", "Head Accountant"));

        // HA cannot approve budget override
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            budgetService.ApproveBudgetOverrideAsync(1, "EMP-002", "Head Accountant"));

        // HA cannot approve budget transfer
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            budgetService.ApproveBudgetTransferAsync(1, "EMP-002", "Head Accountant"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. CSH-09 Governance & Decision E
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Csh09_Governance_OnlyFinanceManagerCanChangeThreshold_AndLogsChange()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        // Initial threshold is unset (Decision E)
        var initial = await gateService.GetCurrentThresholdAsync();
        Assert.Null(initial.ThresholdAmount);
        Assert.Equal(0m, initial.EffectiveCeoLimit);

        // Non-FM attempt to update throws UnauthorizedAccessException
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            gateService.UpdateThresholdAsync(new UpdateCeoThresholdRequest
            {
                ThresholdAmount = 25000m,
                Reason = "Policy update"
            }, "EMP-002", "Head Accountant"));

        // FM updates threshold to 25,000
        var updated = await gateService.UpdateThresholdAsync(new UpdateCeoThresholdRequest
            {
                ThresholdAmount = 25000m,
                Reason = "Board Resolution 2026-01"
            }, "EMP-001", "Finance Manager");
        Assert.Equal(25000m, updated.ThresholdAmount);

        // Verify change log is created
        var logs = await gateService.GetThresholdHistoryAsync();
        Assert.Single(logs);
        Assert.Equal(25000m, logs[0].NewAmount);
        Assert.Equal("EMP-001", logs[0].ChangedBy);
        Assert.Equal("Board Resolution 2026-01", logs[0].Reason);
    }

    [Fact]
    public async Task DecisionE_UnsetThreshold_RequiresCeoApprovalForEveryCashOutflow()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        // Verify threshold is unset
        var threshold = await gateService.GetCurrentThresholdAsync();
        Assert.Null(threshold.ThresholdAmount);

        // Outflows of any amount (even 1.00 PHP) require CEO approval under Decision E
        var smallCheck = await gateService.CheckGateAsync(CeoApprovalItemTypes.CashOutflow, "1", amount: 1.00m);
        Assert.True(smallCheck.IsRequired);
        Assert.False(smallCheck.IsSatisfied);

        var largeCheck = await gateService.CheckGateAsync(CeoApprovalItemTypes.CashOutflow, "1", amount: 50000m);
        Assert.True(largeCheck.IsRequired);
        Assert.False(largeCheck.IsSatisfied);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5. Decision C & FIN-26 Tests
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task DecisionC_DmsRequestWithoutDepartment_AcceptedIntoNeedsDepartment_AndLockedUntilAssigned()
    {
        using var appContext = CreateInMemoryDbContext();
        using var fomsContext = CreateInMemoryFomsDbContext();

        var mockHttpFactory = new Mock<IHttpClientFactory>();
        mockHttpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        var options = Options.Create(new DmsIntegrationOptions
        {
            BaseUrl = "https://speedex-dms.azurewebsites.net",
            InboundApiKey = "test-key",
            OutboundApiKey = "test-outbound"
        });

        var dmsService = new DmsIntegrationService(
            appContext,
            fomsContext,
            mockHttpFactory.Object,
            options,
            NullLogger<DmsIntegrationService>.Instance);

        var request = new DmsCashAdvanceRequestDto
        {
            RequestId = "DMS-CA-DEC-C-01",
            DeliveryReference = "TRIP-999",
            RequesterName = "Driver Juan",
            Purpose = "Out of town logistics",
            RequestedAmount = 12000m,
            DepartmentCode = string.Empty // Decision C: No department provided
        };

        var response = await dmsService.ProcessCashAdvanceRequestAsync(request);
        Assert.True(response.IsSuccess);
        Assert.Equal("Needs department", response.Status);
        Assert.Contains("Needs department", response.Message);

        var savedAdvance = await fomsContext.CashAdvances.FirstOrDefaultAsync(c => c.AdvanceId == response.AdvanceNumber);
        Assert.NotNull(savedAdvance);
        Assert.Equal("Needs department", savedAdvance!.Status);
        Assert.Equal(string.Empty, savedAdvance.DepartmentCode);
    }

    [Fact]
    public async Task Fin26_RequesterCannotSelectLegacyDepartment_DepOps()
    {
        using var context = CreateInMemoryDbContext();
        var budgetService = new BudgetService(context);
        var deptService = new DepartmentService(context, budgetService);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            deptService.ValidateAndNormalizeDepartmentAsync("DEP-OPS", "Assistant of Finance Manager", isNewRequest: true));
        Assert.Contains("legacy department", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Fin26_FinanceManagerCanChangeDepartmentPreApproval_LogsAudit_AndLocksPostApproval()
    {
        using var context = CreateInMemoryDbContext();
        var budgetService = new BudgetService(context);
        var deptService = new DepartmentService(context, budgetService);

        // Non-FM attempt throws UnauthorizedAccessException
        var changeReq = new ChangeDepartmentRequest
        {
            RequestType = "CashAdvance",
            RequestId = "CA-2026-001",
            NewDepartmentCode = "DEP-CCS",
            Reason = "Reassigned to Customer Care dispatch"
        };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            deptService.ChangeRequestDepartmentAsync(changeReq, "EMP-004", "Assistant of Finance Manager"));

        // FM modifies department with reason
        var logDto = await deptService.ChangeRequestDepartmentAsync(changeReq, "EMP-001", "Finance Manager");
        Assert.Equal("DEP-FIN", logDto.OldDepartmentCode);
        Assert.Equal("DEP-CCS", logDto.NewDepartmentCode);
        Assert.Equal("EMP-001", logDto.ChangedBy);

        // Verify audit row stored in DepartmentChangeLogs
        var logs = await deptService.GetDepartmentChangeLogsAsync("CashAdvance", "CA-2026-001");
        Assert.Single(logs);
        Assert.Equal("DEP-CCS", logs[0].NewDepartmentCode);

        // Now simulate approval: advance becomes Approved
        var adv = await context.CashAdvances.FindAsync("CA-2026-001");
        adv!.Status = "Approved";
        await context.SaveChangesAsync();

        // After approval, changing department is locked!
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            deptService.ChangeRequestDepartmentAsync(changeReq, "EMP-001", "Finance Manager"));
        Assert.Contains("locked", ex.Message);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 6. Excel Exports: Open Generated Workbooks and Assert Headers and Rows
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Exports_CeoApprovalRecords_OpensValidWorkbook_AndAssertsHeadersAndRows()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        // Record 1: Approved annual budget with optional attachment omitted ("No attachment")
        await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.AnnualBudget,
            ItemId = "2026",
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow,
            Notes = "Approved in person"
        }, "EMP-001", "Finance Manager");

        // Record 2: Rejected direct disbursement with rejection reason
        await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.CashOutflow,
            ItemId = "1",
            Status = "Rejected",
            ApprovalDate = DateTime.UtcNow,
            RejectionReason = "Exceeds monthly reserve limit"
        }, "EMP-001", "Finance Manager");

        // Generate Excel workbook bytes
        var excelBytes = await gateService.ExportRecordsToExcelAsync();
        Assert.NotNull(excelBytes);
        Assert.True(excelBytes.Length > 0);

        // Open with ClosedXML.Excel.XLWorkbook
        using var ms = new MemoryStream(excelBytes);
        using var workbook = new XLWorkbook(ms);

        var worksheet = workbook.Worksheet("CEO Approval Records");
        Assert.NotNull(worksheet);

        // Assert Headers
        Assert.Equal("Record ID", worksheet.Cell(1, 1).GetString());
        Assert.Equal("Item Type", worksheet.Cell(1, 2).GetString());
        Assert.Equal("Status", worksheet.Cell(1, 4).GetString());
        Assert.Equal("Attachment Flag", worksheet.Cell(1, 9).GetString());
        Assert.Equal("Rejection Reason", worksheet.Cell(1, 11).GetString());

        // Assert Rows
        // Row 2: CashOutflow (Rejected with Rejection Reason)
        var row2ItemType = worksheet.Cell(2, 2).GetString();
        var row2Status = worksheet.Cell(2, 4).GetString();
        var row2Rejection = worksheet.Cell(2, 11).GetString();
        Assert.Equal(CeoApprovalItemTypes.CashOutflow, row2ItemType);
        Assert.Equal("Rejected", row2Status);
        Assert.Equal("Exceeds monthly reserve limit", row2Rejection);

        // Row 3: AnnualBudget (Approved with "No attachment" flag)
        var row3ItemType = worksheet.Cell(3, 2).GetString();
        var row3Status = worksheet.Cell(3, 4).GetString();
        var row3AttachmentFlag = worksheet.Cell(3, 9).GetString();
        Assert.Equal(CeoApprovalItemTypes.AnnualBudget, row3ItemType);
        Assert.Equal("Approved", row3Status);
        Assert.Equal("No attachment", row3AttachmentFlag);
    }

    [Fact]
    public async Task Exports_DepartmentChangeLogs_OpensValidWorkbook_AndAssertsHeadersAndRows()
    {
        using var context = CreateInMemoryDbContext();
        var budgetService = new BudgetService(context);
        var deptService = new DepartmentService(context, budgetService);

        // Change department to generate change log
        await deptService.ChangeRequestDepartmentAsync(new ChangeDepartmentRequest
        {
            RequestType = "CashAdvance",
            RequestId = "CA-2026-001",
            NewDepartmentCode = "DEP-CCS",
            Reason = "Reassigned to Customer Care"
        }, "EMP-001", "Finance Manager");

        // Generate Excel workbook bytes
        var excelBytes = await deptService.ExportDepartmentChangeLogsToExcelAsync();
        Assert.NotNull(excelBytes);
        Assert.True(excelBytes.Length > 0);

        // Open with ClosedXML.Excel.XLWorkbook
        using var ms = new MemoryStream(excelBytes);
        using var workbook = new XLWorkbook(ms);

        var worksheet = workbook.Worksheet("Department Change Logs");
        Assert.NotNull(worksheet);

        // Assert Headers
        Assert.Equal("Log ID", worksheet.Cell(1, 1).GetString());
        Assert.Equal("Request Type", worksheet.Cell(1, 2).GetString());
        Assert.Equal("Old Department Code", worksheet.Cell(1, 4).GetString());
        Assert.Equal("Old Department Name", worksheet.Cell(1, 5).GetString());
        Assert.Equal("New Department Code", worksheet.Cell(1, 6).GetString());
        Assert.Equal("New Department Name", worksheet.Cell(1, 7).GetString());
        Assert.Equal("Changed By", worksheet.Cell(1, 8).GetString());
        Assert.Equal("Change Reason", worksheet.Cell(1, 10).GetString());

        // Assert Row 2 contents
        Assert.Equal("CashAdvance", worksheet.Cell(2, 2).GetString());
        Assert.Equal("DEP-FIN", worksheet.Cell(2, 4).GetString());
        Assert.Equal("DEP-CCS", worksheet.Cell(2, 6).GetString());
        Assert.Equal("EMP-001", worksheet.Cell(2, 8).GetString());
        Assert.Equal("Reassigned to Customer Care", worksheet.Cell(2, 10).GetString());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 7. FIN-23 Adjusting Entries (Created by Finance Manager Only, Reason Required, Approved Before Posting)
    // ─────────────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("Accountant")]
    [InlineData("Head Accountant")]
    [InlineData("Assistant of Finance Manager")]
    [InlineData("Encoder")]
    [InlineData("Staff")]
    public async Task Fin23_AdjustingEntries_NonFinanceManager_CannotCreate(string role)
    {
        using var context = CreateInMemoryDbContext();
        var invoice = new DomainInvoice
        {
            Id = "INV-2026-FIN23",
            InvoiceNo = "INV-2026-FIN23",
            ClientId = "CLI-001",
            FreightCharges = 50000m
        };
        BillingComputationService.RecalculateInvoice(invoice);
        context.Invoices.Add(invoice);
        await context.SaveChangesAsync();

        var handler = new AdjustmentFeatures.CreateAdjustmentCommandHandler(context);
        var cmd = new AdjustmentFeatures.CreateAdjustmentCommand(
            InvoiceNo: "INV-2026-FIN23",
            AdjustmentType: "Credit",
            Amount: 5000m,
            Reason: "Volume rebate",
            AdjustedBy: "EMP-003",
            Role: role
        );

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.Handle(cmd, default));
        Assert.Contains("Only the Finance Manager can create adjusting entries", ex.Message);
    }

    [Fact]
    public async Task Fin23_AdjustingEntries_FinanceManager_EmptyReason_ThrowsInvalidOperationException()
    {
        using var context = CreateInMemoryDbContext();
        var invoice = new DomainInvoice
        {
            Id = "INV-2026-FIN23-2",
            InvoiceNo = "INV-2026-FIN23-2",
            ClientId = "CLI-001",
            FreightCharges = 50000m
        };
        BillingComputationService.RecalculateInvoice(invoice);
        context.Invoices.Add(invoice);
        await context.SaveChangesAsync();

        var handler = new AdjustmentFeatures.CreateAdjustmentCommandHandler(context);
        var cmd = new AdjustmentFeatures.CreateAdjustmentCommand(
            InvoiceNo: "INV-2026-FIN23-2",
            AdjustmentType: "Credit",
            Amount: 5000m,
            Reason: "", // Empty reason
            AdjustedBy: "EMP-001",
            Role: "Finance Manager"
        );

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(cmd, default));
        Assert.Contains("reason is strictly required", ex.Message);
    }

    [Fact]
    public async Task Fin23_AdjustingEntries_FinanceManagerCreates_StaysPendingUntilApprovedBeforePosting()
    {
        using var context = CreateInMemoryDbContext();
        var invoice = new DomainInvoice
        {
            Id = "INV-2026-FIN23-3",
            InvoiceNo = "INV-2026-FIN23-3",
            ClientId = "CLI-001",
            FreightCharges = 50000m
        };
        BillingComputationService.RecalculateInvoice(invoice);
        context.Invoices.Add(invoice);
        await context.SaveChangesAsync();

        var createHandler = new AdjustmentFeatures.CreateAdjustmentCommandHandler(context);
        var cmd = new AdjustmentFeatures.CreateAdjustmentCommand(
            InvoiceNo: "INV-2026-FIN23-3",
            AdjustmentType: "Credit",
            Amount: 5000m,
            Reason: "Volume tier discount correction for Q3 shipments",
            AdjustedBy: "EMP-001",
            Role: "Finance Manager"
        );

        var created = await createHandler.Handle(cmd, default);
        Assert.NotNull(created);
        Assert.Equal("Pending", created.Status);
        Assert.Equal("Volume tier discount correction for Q3 shipments", created.Reason);
        Assert.Equal("EMP-001", created.AdjustedBy);

        // In Pending status, invoice balance is NOT yet modified
        decimal initialBalance = invoice.Balance;
        var invoiceBeforeApprove = await context.Invoices.FirstAsync(i => i.InvoiceNo == "INV-2026-FIN23-3");
        Assert.Equal(initialBalance, invoiceBeforeApprove.Balance);
        Assert.Equal(0m, invoiceBeforeApprove.CreditMemoAmount);

        // Approve adjustment -> balance is applied and posted by Head Accountant (EMP-002)
        var approveHandler = new AdjustmentFeatures.ApproveAdjustmentCommandHandler(context);
        var approved = await approveHandler.Handle(new AdjustmentFeatures.ApproveAdjustmentCommand(created.Id, "EMP-002", "Head Accountant"), default);
        Assert.NotNull(approved);
        Assert.Equal("Approved", approved.Status);

        var invoiceAfterApprove = await context.Invoices.FirstAsync(i => i.InvoiceNo == "INV-2026-FIN23-3");
        Assert.Equal(initialBalance - 5000m, invoiceAfterApprove.Balance);
        Assert.Equal(5000m, invoiceAfterApprove.CreditMemoAmount);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 8. In-Person CEO Approval Verification Suite
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task CeoApproval_ApprovalDate_CannotBeLaterThanToday()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        var req = new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.AnnualBudget,
            ItemId = "2026",
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow.AddDays(2).Date // Future date
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            gateService.RecordApprovalAsync(req, "EMP-001", "Finance Manager"));
        Assert.Contains("cannot be in the future", ex.Message);
    }

    [Fact]
    public async Task CeoApproval_AttachmentTypes_GenuinePdfJpgPngOnly()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        // 1. Text payload claiming to be .pdf -> Rejected
        var fakePdfReq = new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.AnnualBudget,
            ItemId = "2026",
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow,
            AttachmentBytes = Encoding.UTF8.GetBytes("Not a real PDF stream"),
            AttachmentFileName = "fake.pdf"
        };
        var ex1 = await Assert.ThrowsAsync<ArgumentException>(() =>
            gateService.RecordApprovalAsync(fakePdfReq, "EMP-001", "Finance Manager"));
        Assert.Contains("Real file format must be genuine PDF, JPG, or PNG", ex1.Message);

        // 2. Genuine PNG magic bytes -> Accepted
        byte[] pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00 };
        var validPngReq = new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.AnnualBudget,
            ItemId = "2026",
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow,
            AttachmentBytes = pngBytes,
            AttachmentFileName = "board_signoff.png"
        };
        var record = await gateService.RecordApprovalAsync(validPngReq, "EMP-001", "Finance Manager");
        Assert.True(record.HasAttachment);
        Assert.Equal("board_signoff.png", record.AttachmentFileName);
    }

    [Fact]
    public async Task CeoApproval_Rejection_RequiresReason_AndReleasesReservation()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        // 1. Rejection without reason throws ArgumentException
        var noReasonReq = new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.CashOutflow,
            ItemId = "CA-2026-001",
            Status = "Rejected",
            ApprovalDate = DateTime.UtcNow,
            RejectionReason = "" // Missing reason
        };
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            gateService.RecordApprovalAsync(noReasonReq, "EMP-001", "Finance Manager"));
        Assert.Contains("RejectionReason is strictly mandatory", ex.Message);

        // 2. Rejection with reason releases committed reservation
        var lineBefore = await context.MonthlyBudgetLines.FirstAsync(l => l.BudgetLineId == 1);
        Assert.Equal(25000m, lineBefore.CommittedAmount);

        var validRejectionReq = new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.CashOutflow,
            ItemId = "CA-2026-001",
            Status = "Rejected",
            ApprovalDate = DateTime.UtcNow,
            RejectionReason = "Exceeds monthly cash outflow limit"
        };
        await gateService.RecordApprovalAsync(validRejectionReq, "EMP-001", "Finance Manager");

        var lineAfter = await context.MonthlyBudgetLines.FirstAsync(l => l.BudgetLineId == 1);
        Assert.Equal(0m, lineAfter.CommittedAmount);

        var advance = await context.CashAdvances.FirstAsync(a => a.AdvanceId == "CA-2026-001");
        Assert.Equal("Rejected", advance.Status);
        Assert.Equal("Exceeds monthly cash outflow limit", advance.RejectionReason);
    }

    [Fact]
    public async Task CeoApproval_NeverShowsCeoAsLoggedIn_ApproverLabelIsInPerson()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        var req = new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.AnnualBudget,
            ItemId = "2026",
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow
        };

        var record = await gateService.RecordApprovalAsync(req, "EMP-001", "Finance Manager");
        Assert.Equal("CEO/President (in person)", record.ApproverLabel);
        Assert.Equal("EMP-001", record.EnteredBy);
        Assert.NotEqual("CEO", record.EnteredBy);
        Assert.NotEqual("President", record.EnteredBy);

        // Assert CEO role does not exist in canonical FOMS roles
        Assert.DoesNotContain("CEO", FomsRoles.AllCanonicalRoles);
        Assert.DoesNotContain("President", FomsRoles.AllCanonicalRoles);
    }

    [Fact]
    public async Task UnsetThreshold_GateCheck_ReturnsClearActionableMessage()
    {
        using var context = CreateInMemoryDbContext();
        // Remove configured CSH-09 setting
        var setting = await context.CeoApprovalThresholdSettings.FirstOrDefaultAsync();
        if (setting != null)
        {
            context.CeoApprovalThresholdSettings.Remove(setting);
            await context.SaveChangesAsync();
        }

        var gateService = new CeoApprovalGateService(context);
        var result = await gateService.CheckGateAsync(CeoApprovalItemTypes.CashOutflow, "CA-2026-UNSET", 500m);

        Assert.True(result.IsRequired);
        Assert.False(result.IsSatisfied);
        Assert.Contains("Go-Live setup", result.Message);
        Assert.Contains("PO Decision E", result.Message);
        Assert.Contains("every cash outflow requires an in-person CEO/President approval record", result.Message);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 9. Block Tests for Items 2, 3, 4, 5, 7 (R5.3 Evidence)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Item2_BudgetTransfer_BlockedWithoutApproval_SucceedsWithApprovalAndConfirmation()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        // 1. Without record: gate is unsatisfied
        var unrecorded = await gateService.CheckGateAsync(CeoApprovalItemTypes.BudgetTransfer, "1");
        Assert.False(unrecorded.IsSatisfied);

        // 2. Recorded by FM, but before HA confirmation: unsatisfied
        var rec = await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.BudgetTransfer,
            ItemId = "1",
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow
        }, "EMP-001", "Finance Manager");

        var preConfirm = await gateService.CheckGateAsync(CeoApprovalItemTypes.BudgetTransfer, "1");
        Assert.False(preConfirm.IsSatisfied);
        Assert.Contains("requires Head Accountant maker-checker recording confirmation", preConfirm.Message);

        // 3. Confirmed by HA: satisfied
        await gateService.ConfirmRecordAsync(rec.RecordId, "Recording", "EMP-002", "Head Accountant");
        var postConfirm = await gateService.CheckGateAsync(CeoApprovalItemTypes.BudgetTransfer, "1");
        Assert.True(postConfirm.IsSatisfied);
    }

    [Fact]
    public async Task Item3_BudgetCarryOver_BlockedWithoutApproval_SucceedsWithApprovalAndConfirmation()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        // 1. Without record: gate is unsatisfied
        var unrecorded = await gateService.CheckGateAsync(CeoApprovalItemTypes.BudgetCarryOver, "CO-2026-01");
        Assert.False(unrecorded.IsSatisfied);

        // 2. Recorded by FM, before HA confirmation: unsatisfied
        var rec = await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.BudgetCarryOver,
            ItemId = "CO-2026-01",
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow
        }, "EMP-001", "Finance Manager");

        var preConfirm = await gateService.CheckGateAsync(CeoApprovalItemTypes.BudgetCarryOver, "CO-2026-01");
        Assert.False(preConfirm.IsSatisfied);

        // 3. Confirmed by HA: satisfied
        await gateService.ConfirmRecordAsync(rec.RecordId, "Recording", "EMP-002", "Head Accountant");
        var postConfirm = await gateService.CheckGateAsync(CeoApprovalItemTypes.BudgetCarryOver, "CO-2026-01");
        Assert.True(postConfirm.IsSatisfied);
    }

    [Fact]
    public async Task Item4_BudgetOverride_BlockedWithoutApproval_SucceedsWithApproval()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        // 1. Without record: gate is unsatisfied
        var unrecorded = await gateService.CheckGateAsync(CeoApprovalItemTypes.BudgetOverride, "1", amount: 25000m);
        Assert.False(unrecorded.IsSatisfied);

        // 2. With record: gate is satisfied
        await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.BudgetOverride,
            ItemId = "1",
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow
        }, "EMP-001", "Finance Manager");

        var recorded = await gateService.CheckGateAsync(CeoApprovalItemTypes.BudgetOverride, "1", amount: 25000m);
        Assert.True(recorded.IsSatisfied);
    }

    [Fact]
    public async Task Item5_Reimbursement_AboveThreshold_BlockedWithoutApproval_SucceedsWithApproval()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        // 1. Above threshold without record: gate is unsatisfied
        var unrecorded = await gateService.CheckGateAsync(CeoApprovalItemTypes.Reimbursement, "1", amount: 25000m);
        Assert.False(unrecorded.IsSatisfied);

        // 2. With record: gate is satisfied
        await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.Reimbursement,
            ItemId = "1",
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow
        }, "EMP-001", "Finance Manager");

        var recorded = await gateService.CheckGateAsync(CeoApprovalItemTypes.Reimbursement, "1", amount: 25000m);
        Assert.True(recorded.IsSatisfied);
    }

    [Fact]
    public async Task Item7_InvoiceDiscount_BlockedWithoutApproval_SucceedsWithApproval()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        // 1. Without record: gate is unsatisfied
        var unrecorded = await gateService.CheckGateAsync(CeoApprovalItemTypes.InvoiceDiscount, "INV-2026-DISC", amount: 5000m);
        Assert.False(unrecorded.IsSatisfied);

        // 2. With record: gate is satisfied
        await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.InvoiceDiscount,
            ItemId = "INV-2026-DISC",
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow
        }, "EMP-001", "Finance Manager");

        var recorded = await gateService.CheckGateAsync(CeoApprovalItemTypes.InvoiceDiscount, "INV-2026-DISC", amount: 5000m);
        Assert.True(recorded.IsSatisfied);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 10. FIN-23 Adjusting Entries: Closed Period, Delete Block, Approver Role
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Fin23_ClosedPeriod_BlocksCreateAndApprove()
    {
        using var context = CreateInMemoryDbContext();
        var today = DateTime.UtcNow.Date;

        var invoice = new DomainInvoice
        {
            Id = "INV-PERIOD-01",
            InvoiceNo = "INV-PERIOD-01",
            ClientId = "CLI-001",
            FreightCharges = 20000m
        };
        BillingComputationService.RecalculateInvoice(invoice);
        context.Invoices.Add(invoice);

        // Add closed accounting period covering today
        context.AccountingPeriodCloses.Add(new FOMS.Domain.Entities.AccountingPeriodClose
        {
            PeriodType = "Monthly",
            PeriodStart = today.AddDays(-15),
            PeriodEnd = today.AddDays(15),
            ClosedBy = "EMP-001"
        });
        await context.SaveChangesAsync();

        var createHandler = new AdjustmentFeatures.CreateAdjustmentCommandHandler(context);
        var createCmd = new AdjustmentFeatures.CreateAdjustmentCommand(
            InvoiceNo: "INV-PERIOD-01",
            AdjustmentType: "Credit",
            Amount: 1000m,
            Reason: "Correction in closed period",
            AdjustedBy: "EMP-001",
            Role: "Finance Manager"
        );

        // Creation is blocked in closed period
        var exCreate = await Assert.ThrowsAsync<InvalidOperationException>(() => createHandler.Handle(createCmd, default));
        Assert.Contains("closed accounting period", exCreate.Message);
    }

    [Fact]
    public async Task Fin23_ClosedPeriod_BlocksApprove_WhenClosedAfterCreation()
    {
        using var context = CreateInMemoryDbContext();
        var today = DateTime.UtcNow.Date;

        var invoice = new DomainInvoice
        {
            Id = "INV-PERIOD-02",
            InvoiceNo = "INV-PERIOD-02",
            ClientId = "CLI-001",
            FreightCharges = 20000m
        };
        BillingComputationService.RecalculateInvoice(invoice);
        context.Invoices.Add(invoice);
        await context.SaveChangesAsync();

        // 1. Create adjustment in open period
        var createHandler = new AdjustmentFeatures.CreateAdjustmentCommandHandler(context);
        var created = await createHandler.Handle(new AdjustmentFeatures.CreateAdjustmentCommand(
            InvoiceNo: "INV-PERIOD-02",
            AdjustmentType: "Credit",
            Amount: 1000m,
            Reason: "Valid reason in open period",
            AdjustedBy: "EMP-001",
            Role: "Finance Manager"
        ), default);
        Assert.NotNull(created);

        // 2. Period gets closed
        context.AccountingPeriodCloses.Add(new FOMS.Domain.Entities.AccountingPeriodClose
        {
            PeriodType = "Monthly",
            PeriodStart = today.AddDays(-10),
            PeriodEnd = today.AddDays(10),
            ClosedBy = "EMP-001"
        });
        await context.SaveChangesAsync();

        // 3. Approval is now blocked
        var approveHandler = new AdjustmentFeatures.ApproveAdjustmentCommandHandler(context);
        var exApprove = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            approveHandler.Handle(new AdjustmentFeatures.ApproveAdjustmentCommand(created.Id, "EMP-002", "Head Accountant"), default));
        Assert.Contains("closed accounting period", exApprove.Message);
    }

    [Fact]
    public async Task Fin23_ApprovedAdjustment_CannotBeDeleted()
    {
        using var context = CreateInMemoryDbContext();

        var invoice = new DomainInvoice
        {
            Id = "INV-DEL-01",
            InvoiceNo = "INV-DEL-01",
            ClientId = "CLI-001",
            FreightCharges = 10000m
        };
        BillingComputationService.RecalculateInvoice(invoice);
        context.Invoices.Add(invoice);
        await context.SaveChangesAsync();

        var createHandler = new AdjustmentFeatures.CreateAdjustmentCommandHandler(context);
        var created = await createHandler.Handle(new AdjustmentFeatures.CreateAdjustmentCommand(
            InvoiceNo: "INV-DEL-01",
            AdjustmentType: "Credit",
            Amount: 1000m,
            Reason: "Delete guard test",
            AdjustedBy: "EMP-001",
            Role: "Finance Manager"
        ), default);
        Assert.NotNull(created);

        // Approve adjustment -> status is Approved
        var approveHandler = new AdjustmentFeatures.ApproveAdjustmentCommandHandler(context);
        await approveHandler.Handle(new AdjustmentFeatures.ApproveAdjustmentCommand(created.Id, "EMP-002", "Head Accountant"), default);

        // Attempt deletion of Approved adjustment -> rejected
        var deleteHandler = new AdjustmentFeatures.DeleteAdjustmentCommandHandler(context);
        var deleted = await deleteHandler.Handle(new AdjustmentFeatures.DeleteAdjustmentCommand(created.Id, "EMP-001"), default);
        Assert.False(deleted);

        // Verify still exists in DB
        var exists = await context.PaymentAdjustments.AnyAsync(a => a.Id == created.Id);
        Assert.True(exists);
    }

    [Fact]
    public async Task Fin23_ApproverRole_HeadAccountantOnly_AndCreatorCannotApprove()
    {
        using var context = CreateInMemoryDbContext();

        var invoice = new DomainInvoice
        {
            Id = "INV-SOD-01",
            InvoiceNo = "INV-SOD-01",
            ClientId = "CLI-001",
            FreightCharges = 10000m
        };
        BillingComputationService.RecalculateInvoice(invoice);
        context.Invoices.Add(invoice);
        await context.SaveChangesAsync();

        var createHandler = new AdjustmentFeatures.CreateAdjustmentCommandHandler(context);
        var created = await createHandler.Handle(new AdjustmentFeatures.CreateAdjustmentCommand(
            InvoiceNo: "INV-SOD-01",
            AdjustmentType: "Credit",
            Amount: 1000m,
            Reason: "SoD test",
            AdjustedBy: "EMP-001",
            Role: "Finance Manager"
        ), default);
        Assert.NotNull(created);

        var approveHandler = new AdjustmentFeatures.ApproveAdjustmentCommandHandler(context);

        // 1. Creator self-approval blocked (SoD)
        var exSelf = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            approveHandler.Handle(new AdjustmentFeatures.ApproveAdjustmentCommand(created.Id, "EMP-001", "Head Accountant"), default));
        Assert.Contains("creator of an adjusting entry cannot approve their own entry", exSelf.Message);

        // 2. Non-HA role blocked
        var exRole = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            approveHandler.Handle(new AdjustmentFeatures.ApproveAdjustmentCommand(created.Id, "EMP-003", "Accountant"), default));
        Assert.Contains("Head Accountant approval before posting", exRole.Message);

        // 3. Head Accountant (different from creator) succeeds
        var approved = await approveHandler.Handle(new AdjustmentFeatures.ApproveAdjustmentCommand(created.Id, "EMP-002", "Head Accountant"), default);
        Assert.NotNull(approved);
        Assert.Equal("Approved", approved.Status);
        Assert.Equal("EMP-002", approved.ApprovedBy);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 11. Manila Date Boundary & Request Date Threshold Tests
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task CeoApproval_ManilaDateBoundary_ClockInjection_AcceptsToday_RejectsTomorrow()
    {
        using var context = CreateInMemoryDbContext();
        context.AnnualBudgets.AddRange(
            new AnnualBudget { AnnualBudgetId = 2, FiscalYear = 2027, Status = "Draft", TotalBudget = 1000000m, PreparedBy = "EMP-001" },
            new AnnualBudget { AnnualBudgetId = 3, FiscalYear = 2028, Status = "Draft", TotalBudget = 1000000m, PreparedBy = "EMP-001" },
            new AnnualBudget { AnnualBudgetId = 4, FiscalYear = 2029, Status = "Draft", TotalBudget = 1000000m, PreparedBy = "EMP-001" }
        );
        await context.SaveChangesAsync();

        // Boundary A: 2026-11-11 15:59 UTC -> Manila 2026-11-11 23:59 (Today is Nov 11)
        var clockA = new DateTime(2026, 11, 11, 15, 59, 0, DateTimeKind.Utc);
        var gateServiceA = new CeoApprovalGateService(context, () => clockA);

        // Nov 11 is Manila today -> accepted
        var rec11A = await gateServiceA.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.AnnualBudget,
            ItemId = "2026",
            Status = "Approved",
            ApprovalDate = new DateTime(2026, 11, 11)
        }, "EMP-001", "Finance Manager");
        Assert.NotNull(rec11A);

        // Nov 12 is tomorrow in Manila at 23:59 -> rejected
        var exA = await Assert.ThrowsAsync<ArgumentException>(() =>
            gateServiceA.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
            {
                ItemType = CeoApprovalItemTypes.AnnualBudget,
                ItemId = "2027",
                Status = "Approved",
                ApprovalDate = new DateTime(2026, 11, 12)
            }, "EMP-001", "Finance Manager"));
        Assert.Contains("cannot be in the future", exA.Message);

        // Boundary B: 2026-11-11 16:00 UTC -> Manila 2026-11-12 00:00 (Today is Nov 12)
        var clockB = new DateTime(2026, 11, 11, 16, 0, 0, DateTimeKind.Utc);
        var gateServiceB = new CeoApprovalGateService(context, () => clockB);

        // Nov 12 is now Manila today -> accepted
        var rec12B = await gateServiceB.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.AnnualBudget,
            ItemId = "2028",
            Status = "Approved",
            ApprovalDate = new DateTime(2026, 11, 12)
        }, "EMP-001", "Finance Manager");
        Assert.NotNull(rec12B);

        // Nov 13 is tomorrow in Manila on Nov 12 -> rejected
        var exB = await Assert.ThrowsAsync<ArgumentException>(() =>
            gateServiceB.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
            {
                ItemType = CeoApprovalItemTypes.AnnualBudget,
                ItemId = "2029",
                Status = "Approved",
                ApprovalDate = new DateTime(2026, 11, 13)
            }, "EMP-001", "Finance Manager"));
        Assert.Contains("cannot be in the future", exB.Message);
    }

    [Fact]
    public async Task CeoApproval_ThresholdEvaluatedAgainstRequestCreationDate_WhenThresholdChangesLater()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        var requestCreationDate = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var thresholdChangeDate = new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc);

        // On Oct 1: threshold is 50,000
        await gateService.UpdateThresholdAsync(new UpdateCeoThresholdRequest
        {
            ThresholdAmount = 50000m,
            EffectiveFrom = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            Reason = "Initial high threshold"
        }, "EMP-001", "Finance Manager");

        // On Oct 15: threshold changed down to 10,000
        await gateService.UpdateThresholdAsync(new UpdateCeoThresholdRequest
        {
            ThresholdAmount = 10000m,
            EffectiveFrom = thresholdChangeDate,
            Reason = "Reduced threshold"
        }, "EMP-001", "Finance Manager");

        // Request for 25,000 created on Oct 1 (evaluated against Oct 1 threshold of 50,000):
        // 25,000 <= 50,000 -> CEO approval is NOT required!
        var pastRequestCheck = await gateService.CheckGateAsync(
            CeoApprovalItemTypes.CashOutflow,
            "CA-2026-PAST",
            amount: 25000m,
            requestDate: requestCreationDate);

        Assert.False(pastRequestCheck.IsRequired);
        Assert.True(pastRequestCheck.IsSatisfied);
        Assert.Equal(50000m, pastRequestCheck.ThresholdInForce);

        // Same amount requested on Oct 20 (evaluated against Oct 15 threshold of 10,000):
        // 25,000 > 10,000 -> CEO approval IS required!
        var newRequestCheck = await gateService.CheckGateAsync(
            CeoApprovalItemTypes.CashOutflow,
            "CA-2026-NEW",
            amount: 25000m,
            requestDate: new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc));

        Assert.True(newRequestCheck.IsRequired);
        Assert.False(newRequestCheck.IsSatisfied);
        Assert.Equal(10000m, newRequestCheck.ThresholdInForce);
    }

    [Fact]
    public async Task Fin23_AdjustingEntries_ClosedMonthDate_WhileTodayInOpenMonth_BlockedOnCreateAndApprove()
    {
        using var context = CreateInMemoryDbContext();

        // Close October 2026 (2026-10-01 to 2026-10-31)
        context.AccountingPeriodCloses.Add(new DomainPeriodClose
        {
            Id = "CLOSE-2026-10",
            PeriodType = "Month",
            PeriodStart = new DateTime(2026, 10, 1),
            PeriodEnd = new DateTime(2026, 10, 31),
            ClosedBy = "EMP-001",
            ClosedAt = new DateTime(2026, 11, 2)
        });

        // Seed invoice
        var invoice = new DomainInvoice
        {
            Id = "INV-CLOSED-1",
            InvoiceNo = "INV-CLOSED-1",
            ClientId = "CLI-001",
            FreightCharges = 10000m
        };
        BillingComputationService.RecalculateInvoice(invoice);
        context.Invoices.Add(invoice);
        await context.SaveChangesAsync();

        var createHandler = new AdjustmentFeatures.CreateAdjustmentCommandHandler(context);
        var approveHandler = new AdjustmentFeatures.ApproveAdjustmentCommandHandler(context);

        // 1. Create with AdjustmentDate in closed October while today is open November
        var closedDateReq = new AdjustmentFeatures.CreateAdjustmentCommand(
            InvoiceNo: "INV-CLOSED-1",
            AdjustmentType: "Credit",
            Amount: 1000m,
            Reason: "Correction for October delivery",
            AdjustedBy: "EMP-001",
            Role: "Finance Manager",
            AdjustmentDate: new DateTime(2026, 10, 15)
        );

        var exCreate = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            createHandler.Handle(closedDateReq, default));
        Assert.Contains("closed accounting period", exCreate.Message);

        // 2. Pre-existing Pending adjustment with DateRequested in October cannot be approved
        var pendingOctAdj = new DomainAdjustment
        {
            Id = "ADJ-OCT-01",
            InvoiceNo = "INV-CLOSED-1",
            AdjustmentType = "Credit",
            Amount = 1000m,
            Reason = "Prior unapproved adjustment",
            AdjustedBy = "EMP-001",
            DateRequested = "2026-10-20",
            Status = "Pending"
        };
        context.PaymentAdjustments.Add(pendingOctAdj);
        await context.SaveChangesAsync();

        var exApprove = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            approveHandler.Handle(new AdjustmentFeatures.ApproveAdjustmentCommand("ADJ-OCT-01", "EMP-002", "Head Accountant"), default));
        Assert.Contains("closed accounting period", exApprove.Message);
    }

    [Fact]
    public async Task Reimbursement_RealService_CreationDateThreshold_SucceedsPreChange_FailsPostChange()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);
        var disbService = new DisbursementAndReimbursementService(context, gateService);

        var oct1 = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var oct15 = new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc);
        var oct20 = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);

        // Threshold on Oct 1 is 50,000
        await gateService.UpdateThresholdAsync(new UpdateCeoThresholdRequest
        {
            ThresholdAmount = 50000m,
            EffectiveFrom = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            Reason = "High threshold"
        }, "EMP-001", "Finance Manager");

        // Threshold on Oct 15 reduced to 10,000
        await gateService.UpdateThresholdAsync(new UpdateCeoThresholdRequest
        {
            ThresholdAmount = 10000m,
            EffectiveFrom = oct15,
            Reason = "Lower threshold"
        }, "EMP-001", "Finance Manager");

        // Seed monthly budget line for DEP-FIN Supplies in month 10
        context.MonthlyBudgetLines.Add(new MonthlyBudgetLine
        {
            BudgetLineId = 1001,
            FiscalYear = 2026,
            DepartmentCode = "DEP-FIN",
            Category = "Supplies",
            Month = 10,
            BudgetAmount = 100000m,
            CommittedAmount = 0m,
            ActualAmount = 0m
        });

        // Reimbursement 1: submitted Oct 1 for 25,000 (status Validated)
        var reim1 = new Reimbursement
        {
            ReimbursementId = 101,
            ReferenceNumber = "REIM-OCT-01",
            EmployeeId = "EMP-003",
            Amount = 25000m,
            DepartmentCode = "DEP-FIN",
            Category = "Supplies",
            FiscalYear = 2026,
            Month = 10,
            Status = "Validated",
            SubmittedAt = oct1
        };

        // Reimbursement 2: submitted Oct 20 for 25,000 (status Validated)
        var reim2 = new Reimbursement
        {
            ReimbursementId = 102,
            ReferenceNumber = "REIM-OCT-02",
            EmployeeId = "EMP-003",
            Amount = 25000m,
            DepartmentCode = "DEP-FIN",
            Category = "Supplies",
            FiscalYear = 2026,
            Month = 10,
            Status = "Validated",
            SubmittedAt = oct20
        };

        context.Reimbursements.AddRange(reim1, reim2);
        await context.SaveChangesAsync();

        // Approve Reimbursement 1 (evaluated against Oct 1 threshold of 50k: 25k <= 50k -> succeeds!)
        var approved1 = await disbService.ApproveReimbursementAsync(101, "EMP-001", "Finance Manager");
        Assert.NotNull(approved1);
        Assert.Equal("Approved", approved1.Status);

        // Approve Reimbursement 2 (evaluated against Oct 15 threshold of 10k: 25k > 10k -> fails without CEO approval!)
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            disbService.ApproveReimbursementAsync(102, "EMP-001", "Finance Manager"));
        Assert.Contains("requires CEO/President approval record", ex.Message);

        // Once CEO approval is recorded for Reimbursement 2 -> approval succeeds!
        await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.Reimbursement,
            ItemId = "102",
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow
        }, "EMP-001", "Finance Manager");

        var approved2 = await disbService.ApproveReimbursementAsync(102, "EMP-001", "Finance Manager");
        Assert.NotNull(approved2);
        Assert.Equal("Approved", approved2.Status);
    }

    [Fact]
    public async Task BudgetTransfer_RealService_ApproveBudgetTransferAsync_BlockedWithoutCeoRecord_SucceedsWithRecord()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);
        var budgetService = new BudgetService(context, gateService);

        context.MonthlyBudgetLines.AddRange(
            new MonthlyBudgetLine { BudgetLineId = 2001, FiscalYear = 2026, DepartmentCode = "DEP-FIN", Category = "Supplies", Month = 1, BudgetAmount = 50000m },
            new MonthlyBudgetLine { BudgetLineId = 2002, FiscalYear = 2026, DepartmentCode = "DEP-DSP", Category = "Fuel", Month = 1, BudgetAmount = 50000m }
        );

        var transfer = new BudgetTransfer
        {
            TransferId = 201,
            FiscalYear = 2026,
            FromDepartmentCode = "DEP-FIN",
            FromCategory = "Supplies",
            FromMonth = 1,
            ToDepartmentCode = "DEP-DSP",
            ToCategory = "Fuel",
            ToMonth = 1,
            Amount = 10000m,
            TransferType = "Transfer",
            RequestedBy = "EMP-003",
            RequestedAt = DateTime.UtcNow,
            Status = "Pending"
        };
        context.BudgetTransfers.Add(transfer);
        await context.SaveChangesAsync();

        // 1. Real service call fails without CEO approval record
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            budgetService.ApproveBudgetTransferAsync(201, "EMP-001", "Finance Manager"));
        Assert.Contains("CEO/President approval record", ex.Message);

        // 2. Record CEO approval
        var rec = await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.BudgetTransfer,
            ItemId = "201",
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow
        }, "EMP-001", "Finance Manager");

        await gateService.ConfirmRecordAsync(rec.RecordId, "Recording", "EMP-002", "Head Accountant");

        // 3. Real service call succeeds!
        var approved = await budgetService.ApproveBudgetTransferAsync(201, "EMP-001", "Finance Manager");
        Assert.NotNull(approved);
        Assert.Equal("Approved", approved.Status);
    }

    [Fact]
    public async Task BudgetOverride_RealService_ApproveBudgetOverrideAsync_BlockedWithoutCeoRecord_SucceedsWithRecord()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);
        var budgetService = new BudgetService(context, gateService);

        context.MonthlyBudgetLines.Add(
            new MonthlyBudgetLine { BudgetLineId = 3001, FiscalYear = 2026, DepartmentCode = "DEP-FIN", Category = "Supplies", Month = 1, BudgetAmount = 50000m, CommittedAmount = 50000m }
        );

        var bOverride = new BudgetOverride
        {
            OverrideId = 301,
            BudgetLineId = 3001,
            RequestedAmount = 70000m,
            OverBudgetAmount = 20000m,
            Reason = "Emergency bulk order",
            RequestedBy = "EMP-003",
            RequestedAt = DateTime.UtcNow,
            RequiresCeoApproval = true,
            Status = "Pending"
        };
        context.BudgetOverrides.Add(bOverride);
        await context.SaveChangesAsync();

        // 1. Real service call fails without CEO approval record
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            budgetService.ApproveBudgetOverrideAsync(301, "EMP-001", "Finance Manager"));
        Assert.Contains("CEO/President approval record", ex.Message);

        // 2. Record CEO approval
        await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.BudgetOverride,
            ItemId = "301",
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow
        }, "EMP-001", "Finance Manager");

        // 3. Real service call succeeds!
        var approved = await budgetService.ApproveBudgetOverrideAsync(301, "EMP-001", "Finance Manager");
        Assert.NotNull(approved);
        Assert.Equal("Approved", approved.Status);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 8. R5.11 Item 1: Maker-Checker on Recorded CEO/President Approvals
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Transfer")]
    [InlineData("CarryOver")]
    public async Task R511_Item1_Scenario1_NoApprovalRecord_Blocked(string transferType)
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);
        var budgetService = new BudgetService(context, gateService);

        int transferId = transferType == "Transfer" ? 1201 : 1202;
        context.MonthlyBudgetLines.AddRange(
            new MonthlyBudgetLine { BudgetLineId = transferId * 10 + 1, FiscalYear = 2026, DepartmentCode = "DEP-FIN", Category = "Supplies", Month = 1, BudgetAmount = 50000m },
            new MonthlyBudgetLine { BudgetLineId = transferId * 10 + 2, FiscalYear = 2026, DepartmentCode = "DEP-DSP", Category = "Fuel", Month = 1, BudgetAmount = 50000m }
        );
        context.BudgetTransfers.Add(new BudgetTransfer
        {
            TransferId = transferId,
            FiscalYear = 2026,
            FromDepartmentCode = "DEP-FIN",
            FromCategory = "Supplies",
            FromMonth = 1,
            ToDepartmentCode = "DEP-DSP",
            ToCategory = "Fuel",
            ToMonth = 1,
            Amount = 10000m,
            TransferType = transferType,
            RequestedBy = "EMP-003",
            RequestedAt = DateTime.UtcNow,
            Status = "Pending"
        });
        await context.SaveChangesAsync();

        // 1. No approval record: blocked
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            budgetService.ApproveBudgetTransferAsync(transferId, "EMP-001", "Finance Manager"));
        Assert.Contains("CEO/President approval record", ex.Message);
    }

    [Theory]
    [InlineData("Transfer")]
    [InlineData("CarryOver")]
    public async Task R511_Item1_Scenario2_ApprovalRecorded_NotConfirmed_Blocked_WithItem1Message(string transferType)
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);
        var budgetService = new BudgetService(context, gateService);

        int transferId = transferType == "Transfer" ? 1203 : 1204;
        string gateItemType = transferType == "CarryOver" ? CeoApprovalItemTypes.BudgetCarryOver : CeoApprovalItemTypes.BudgetTransfer;

        context.MonthlyBudgetLines.AddRange(
            new MonthlyBudgetLine { BudgetLineId = transferId * 10 + 1, FiscalYear = 2026, DepartmentCode = "DEP-FIN", Category = "Supplies", Month = 1, BudgetAmount = 50000m },
            new MonthlyBudgetLine { BudgetLineId = transferId * 10 + 2, FiscalYear = 2026, DepartmentCode = "DEP-DSP", Category = "Fuel", Month = 1, BudgetAmount = 50000m }
        );
        context.BudgetTransfers.Add(new BudgetTransfer
        {
            TransferId = transferId,
            FiscalYear = 2026,
            FromDepartmentCode = "DEP-FIN",
            FromCategory = "Supplies",
            FromMonth = 1,
            ToDepartmentCode = "DEP-DSP",
            ToCategory = "Fuel",
            ToMonth = 1,
            Amount = 10000m,
            TransferType = transferType,
            RequestedBy = "EMP-003",
            RequestedAt = DateTime.UtcNow,
            Status = "Pending"
        });
        await context.SaveChangesAsync();

        await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = gateItemType,
            ItemId = transferId.ToString(),
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow
        }, "EMP-001", "Finance Manager");

        // 2. Approval recorded, not confirmed: blocked with item 1 message
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            budgetService.ApproveBudgetTransferAsync(transferId, "EMP-001", "Finance Manager"));
        Assert.Contains("requires Head Accountant maker-checker recording confirmation before budget can take effect (R5.11 item 1)", ex.Message);
    }

    [Theory]
    [InlineData("Transfer")]
    [InlineData("CarryOver")]
    public async Task R511_Item1_Scenario3_ApprovalRecorded_AndConfirmedByHA_Succeeds_BudgetAmountsChangeOnce(string transferType)
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);
        var budgetService = new BudgetService(context, gateService);

        int transferId = transferType == "Transfer" ? 1205 : 1206;
        string gateItemType = transferType == "CarryOver" ? CeoApprovalItemTypes.BudgetCarryOver : CeoApprovalItemTypes.BudgetTransfer;

        var fromLine = new MonthlyBudgetLine { BudgetLineId = transferId * 10 + 1, FiscalYear = 2026, DepartmentCode = "DEP-FIN", Category = "Supplies", Month = 1, BudgetAmount = 50000m };
        var toLine = new MonthlyBudgetLine { BudgetLineId = transferId * 10 + 2, FiscalYear = 2026, DepartmentCode = "DEP-DSP", Category = "Fuel", Month = 1, BudgetAmount = 50000m };
        context.MonthlyBudgetLines.AddRange(fromLine, toLine);

        context.BudgetTransfers.Add(new BudgetTransfer
        {
            TransferId = transferId,
            FiscalYear = 2026,
            FromDepartmentCode = "DEP-FIN",
            FromCategory = "Supplies",
            FromMonth = 1,
            ToDepartmentCode = "DEP-DSP",
            ToCategory = "Fuel",
            ToMonth = 1,
            Amount = 10000m,
            TransferType = transferType,
            RequestedBy = "EMP-003",
            RequestedAt = DateTime.UtcNow,
            Status = "Pending"
        });
        await context.SaveChangesAsync();

        var rec = await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = gateItemType,
            ItemId = transferId.ToString(),
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow
        }, "EMP-001", "Finance Manager");

        // Head Accountant confirms recording
        var conf = await gateService.ConfirmRecordAsync(rec.RecordId, "Recording", "EMP-002", "Head Accountant", "Confirmed against board resolution");
        Assert.NotNull(conf);
        Assert.Equal("EMP-002", conf.ConfirmedBy);

        // 3. Approval recorded and confirmed by HA: succeeds
        var approved = await budgetService.ApproveBudgetTransferAsync(transferId, "EMP-001", "Finance Manager");
        Assert.NotNull(approved);
        Assert.Equal("Approved", approved.Status);

        // Budget amounts changed once
        Assert.Equal(40000m, fromLine.BudgetAmount);
        Assert.Equal(60000m, toLine.BudgetAmount);

        // Calling again throws InvalidOperationException because transfer is already Approved
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            budgetService.ApproveBudgetTransferAsync(transferId, "EMP-001", "Finance Manager"));
        // Amounts unchanged
        Assert.Equal(40000m, fromLine.BudgetAmount);
        Assert.Equal(60000m, toLine.BudgetAmount);
    }

    [Theory]
    [InlineData("Transfer")]
    [InlineData("CarryOver")]
    public async Task R511_Item1_Scenario4_ConfirmationByFM_ACC_AFM_Unauthorized(string transferType)
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        int transferId = transferType == "Transfer" ? 1207 : 1208;
        string gateItemType = transferType == "CarryOver" ? CeoApprovalItemTypes.BudgetCarryOver : CeoApprovalItemTypes.BudgetTransfer;

        context.BudgetTransfers.Add(new BudgetTransfer
        {
            TransferId = transferId,
            FiscalYear = 2026,
            FromDepartmentCode = "DEP-FIN",
            FromCategory = "Supplies",
            FromMonth = 1,
            ToDepartmentCode = "DEP-DSP",
            ToCategory = "Fuel",
            ToMonth = 1,
            Amount = 10000m,
            TransferType = transferType,
            RequestedBy = "EMP-003",
            RequestedAt = DateTime.UtcNow,
            Status = "Pending"
        });
        await context.SaveChangesAsync();

        var rec = await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = gateItemType,
            ItemId = transferId.ToString(),
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow
        }, "EMP-001", "Finance Manager");

        // 4. Confirmation by FM, ACC, AFM throws UnauthorizedAccessException
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            gateService.ConfirmRecordAsync(rec.RecordId, "Recording", "EMP-005", "Finance Manager"));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            gateService.ConfirmRecordAsync(rec.RecordId, "Recording", "EMP-003", "Accountant"));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            gateService.ConfirmRecordAsync(rec.RecordId, "Recording", "EMP-004", "Assistant of Finance Manager"));
    }

    [Theory]
    [InlineData("Transfer")]
    [InlineData("CarryOver")]
    public async Task R511_Item1_Scenario5_ConfirmationByEnteredBy_OrRequester_Rejected(string transferType)
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        int transferId = transferType == "Transfer" ? 1209 : 1210;
        string gateItemType = transferType == "CarryOver" ? CeoApprovalItemTypes.BudgetCarryOver : CeoApprovalItemTypes.BudgetTransfer;

        context.BudgetTransfers.Add(new BudgetTransfer
        {
            TransferId = transferId,
            FiscalYear = 2026,
            FromDepartmentCode = "DEP-FIN",
            FromCategory = "Supplies",
            FromMonth = 1,
            ToDepartmentCode = "DEP-DSP",
            ToCategory = "Fuel",
            ToMonth = 1,
            Amount = 10000m,
            TransferType = transferType,
            RequestedBy = "EMP-002", // Requested by EMP-002 (Head Accountant)
            RequestedAt = DateTime.UtcNow,
            Status = "Pending"
        });
        await context.SaveChangesAsync();

        var rec = await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = gateItemType,
            ItemId = transferId.ToString(),
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow
        }, "EMP-001", "Finance Manager");

        // Confirmer is requester: rejected
        var exReq = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gateService.ConfirmRecordAsync(rec.RecordId, "Recording", "EMP-002", "Head Accountant"));
        Assert.Contains("cannot be the employee who requested it", exReq.Message);

        // Confirmer is EnteredBy: rejected (even if role is HA)
        var exEntered = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gateService.ConfirmRecordAsync(rec.RecordId, "Recording", "EMP-001", "Head Accountant"));
        Assert.Contains("must strictly differ from the person who entered", exEntered.Message);
    }

    [Fact]
    public async Task R511_Item1_Scenario6_SecondConfirmationOfSameRecord_Rejected()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        context.BudgetTransfers.Add(new BudgetTransfer
        {
            TransferId = 1211,
            FiscalYear = 2026,
            FromDepartmentCode = "DEP-FIN",
            FromCategory = "Supplies",
            FromMonth = 1,
            ToDepartmentCode = "DEP-DSP",
            ToCategory = "Fuel",
            ToMonth = 1,
            Amount = 10000m,
            TransferType = "Transfer",
            RequestedBy = "EMP-003",
            RequestedAt = DateTime.UtcNow,
            Status = "Pending"
        });
        await context.SaveChangesAsync();

        var rec = await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.BudgetTransfer,
            ItemId = "1211",
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow
        }, "EMP-001", "Finance Manager");

        // First confirmation succeeds
        await gateService.ConfirmRecordAsync(rec.RecordId, "Recording", "EMP-002", "Head Accountant");

        // Second confirmation rejected
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gateService.ConfirmRecordAsync(rec.RecordId, "Recording", "EMP-002", "Head Accountant"));
        Assert.Contains("already been recorded", ex.Message);
    }

    [Theory]
    [InlineData("Transfer")]
    [InlineData("CarryOver")]
    public async Task R511_Item1_Scenario7_RejectedApprovalRecord_NoConfirmationNeeded_TransferStillBlocked(string transferType)
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);
        var budgetService = new BudgetService(context, gateService);

        int transferId = transferType == "Transfer" ? 1212 : 1213;
        string gateItemType = transferType == "CarryOver" ? CeoApprovalItemTypes.BudgetCarryOver : CeoApprovalItemTypes.BudgetTransfer;

        context.MonthlyBudgetLines.AddRange(
            new MonthlyBudgetLine { BudgetLineId = transferId * 10 + 1, FiscalYear = 2026, DepartmentCode = "DEP-FIN", Category = "Supplies", Month = 1, BudgetAmount = 50000m },
            new MonthlyBudgetLine { BudgetLineId = transferId * 10 + 2, FiscalYear = 2026, DepartmentCode = "DEP-DSP", Category = "Fuel", Month = 1, BudgetAmount = 50000m }
        );
        context.BudgetTransfers.Add(new BudgetTransfer
        {
            TransferId = transferId,
            FiscalYear = 2026,
            FromDepartmentCode = "DEP-FIN",
            FromCategory = "Supplies",
            FromMonth = 1,
            ToDepartmentCode = "DEP-DSP",
            ToCategory = "Fuel",
            ToMonth = 1,
            Amount = 10000m,
            TransferType = transferType,
            RequestedBy = "EMP-003",
            RequestedAt = DateTime.UtcNow,
            Status = "Pending"
        });
        await context.SaveChangesAsync();

        var rec = await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = gateItemType,
            ItemId = transferId.ToString(),
            Status = "Rejected",
            RejectionReason = "Budget cut mandated by board",
            ApprovalDate = DateTime.UtcNow
        }, "EMP-001", "Finance Manager");

        // Confirmation cannot even be performed on a Rejected record
        var confEx = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gateService.ConfirmRecordAsync(rec.RecordId, "Recording", "EMP-002", "Head Accountant"));
        Assert.Contains("Only 'Approved' CEO approval records can be confirmed", confEx.Message);

        // Real service call is still blocked
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            budgetService.ApproveBudgetTransferAsync(transferId, "EMP-001", "Finance Manager"));
        Assert.Contains("CEO/President rejected", ex.Message);
    }

    [Fact]
    public async Task R511_Item1_Scenario8_ConfirmationRowWritten_AuditLogWritten_ExcelExportContainsConfirmation()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        context.BudgetTransfers.Add(new BudgetTransfer
        {
            TransferId = 1214,
            FiscalYear = 2026,
            FromDepartmentCode = "DEP-FIN",
            FromCategory = "Supplies",
            FromMonth = 1,
            ToDepartmentCode = "DEP-DSP",
            ToCategory = "Fuel",
            ToMonth = 1,
            Amount = 10000m,
            TransferType = "Transfer",
            RequestedBy = "EMP-003",
            RequestedAt = DateTime.UtcNow,
            Status = "Pending"
        });
        await context.SaveChangesAsync();

        var rec = await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.BudgetTransfer,
            ItemId = "1214",
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow
        }, "EMP-001", "Finance Manager");

        // Confirm with note
        await gateService.ConfirmRecordAsync(rec.RecordId, "Recording", "EMP-002", "Head Accountant", "Confirmed with Board Minute #45");

        // 1. Confirmation row written
        var confRow = await context.CeoApprovalConfirmations.FirstOrDefaultAsync(c => c.RecordId == rec.RecordId);
        Assert.NotNull(confRow);
        Assert.Equal("EMP-002", confRow.ConfirmedBy);
        Assert.Equal("Recording", confRow.Kind);

        // 2. Audit log entry written
        var audit = await context.AuditLogs.FirstOrDefaultAsync(a => a.EntityName == "CeoApprovalRecord" && a.EntityId == rec.RecordId.ToString());
        Assert.NotNull(audit);
        Assert.Equal("EMP-002", audit.UserId);
        Assert.Equal("Confirm CEO Approval Recording", audit.Action);
        Assert.Contains("Confirmed with Board Minute #45", audit.Details);

        // 3. Excel export contains confirmation
        var excelBytes = await gateService.ExportRecordsToExcelAsync();
        Assert.NotNull(excelBytes);
        using var ms = new MemoryStream(excelBytes);
        using var workbook = new XLWorkbook(ms);
        var worksheet = workbook.Worksheet("CEO Approval Records");
        Assert.NotNull(worksheet);

        // Check header columns 16 & 17
        Assert.Equal("Confirmed By", worksheet.Cell(1, 16).GetString());
        Assert.Equal("Confirmed At (UTC)", worksheet.Cell(1, 17).GetString());

        // Check row has confirmation info
        var row2ConfirmedBy = worksheet.Cell(2, 16).GetString();
        Assert.Equal("EMP-002", row2ConfirmedBy);
        Assert.False(string.IsNullOrEmpty(worksheet.Cell(2, 17).GetString()));
    }

    [Fact]
    public async Task R511_Item1_Scenario9_Regression_OtherItemTypes_DoNotNeedConfirmation()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        var itemsToTest = new (string ItemType, string ItemId)[]
        {
            (CeoApprovalItemTypes.AnnualBudget, "2026"),
            (CeoApprovalItemTypes.BudgetOverride, "1"),
            (CeoApprovalItemTypes.Reimbursement, "1"),
            (CeoApprovalItemTypes.CashOutflow, "CA-2026-001"),
            (CeoApprovalItemTypes.InvoiceDiscount, "INV-2026-001"),
            (CeoApprovalItemTypes.AnnualFinancialStatement, "2026")
        };

        foreach (var (itemType, itemId) in itemsToTest)
        {
            var rec = await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
            {
                ItemType = itemType,
                ItemId = itemId,
                Status = "Approved",
                ApprovalDate = DateTime.UtcNow
            }, "EMP-001", "Finance Manager");

            var check = await gateService.CheckGateAsync(itemType, itemId, 50000m);
            Assert.True(check.IsSatisfied, $"Item type {itemType} should be satisfied immediately without confirmation.");
            Assert.NotNull(check.EffectiveRecord);
            Assert.True(check.EffectiveRecord.IsEffective);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 12. R5.11 Item 1 Correction Record Maker-Checker Tests (Bypass Fix)
    // ─────────────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("Transfer")]
    [InlineData("CarryOver")]
    public async Task R511_Item1_CorrectionRecord_BudgetTransferAndCarryOver_RequiresRecordingConfirmationByHeadAccountant(string transferType)
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);
        var budgetService = new BudgetService(context, gateService);

        int transferId = transferType == "Transfer" ? 2201 : 2202;
        string gateItemType = transferType == "CarryOver"
            ? CeoApprovalItemTypes.BudgetCarryOver
            : CeoApprovalItemTypes.BudgetTransfer;

        context.MonthlyBudgetLines.AddRange(
            new MonthlyBudgetLine { BudgetLineId = transferId * 10 + 1, FiscalYear = 2026, DepartmentCode = "DEP-FIN", Category = "Supplies", Month = 1, BudgetAmount = 50000m },
            new MonthlyBudgetLine { BudgetLineId = transferId * 10 + 2, FiscalYear = 2026, DepartmentCode = "DEP-DSP", Category = "Fuel", Month = 1, BudgetAmount = 50000m }
        );
        context.BudgetTransfers.Add(new BudgetTransfer
        {
            TransferId = transferId,
            FiscalYear = 2026,
            FromDepartmentCode = "DEP-FIN",
            FromCategory = "Supplies",
            FromMonth = 1,
            ToDepartmentCode = "DEP-DSP",
            ToCategory = "Fuel",
            ToMonth = 1,
            Amount = 10000m,
            TransferType = transferType,
            RequestedBy = "EMP-003", // Accountant requested
            RequestedAt = DateTime.UtcNow,
            Status = "Pending"
        });
        await context.SaveChangesAsync();

        // 1. Initial CEO approval recorded by FM (EMP-001) and confirmed by HA (EMP-002)
        var rec1 = await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = gateItemType,
            ItemId = transferId.ToString(),
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow
        }, "EMP-001", "Finance Manager");

        await gateService.ConfirmRecordAsync(rec1.RecordId, "Recording", "EMP-002", "Head Accountant");

        // 2. Finance Manager records correction
        var correctionRec = await gateService.RecordCorrectionAsync(new CreateCeoApprovalCorrectionRequest
        {
            OriginalRecordId = rec1.RecordId,
            ItemType = gateItemType,
            ItemId = transferId.ToString(),
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow,
            CorrectionReason = "Corrected resolution date and revised budget note"
        }, "EMP-001", "Finance Manager");

        // (a) Correction record does NOT inherit confirmation -> transfer is blocked
        var checkBefore = await gateService.CheckGateAsync(gateItemType, transferId.ToString());
        Assert.False(checkBefore.IsSatisfied);
        Assert.Contains("requires Head Accountant maker-checker recording confirmation before budget can take effect (R5.11 item 1)", checkBefore.Message);

        var exBlock = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            budgetService.ApproveBudgetTransferAsync(transferId, "EMP-001", "Finance Manager"));
        Assert.Contains("requires Head Accountant maker-checker recording confirmation before budget can take effect (R5.11 item 1)", exBlock.Message);

        // (b) ConfirmRecordAsync must REJECT Kind "Correction" for transfers and carry-overs
        var rolesToTest = new[]
        {
            ("EMP-001", "Finance Manager"),
            ("EMP-003", "Accountant"),
            ("EMP-004", "Assistant of Finance Manager"),
            ("EMP-002", "Head Accountant")
        };
        foreach (var (empId, role) in rolesToTest)
        {
            var exKind = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                gateService.ConfirmRecordAsync(correctionRec.RecordId, "Correction", empId, role));
            Assert.Contains("R5.11 item 1", exKind.Message);
            Assert.Contains("Recording", exKind.Message);
        }

        // (d) The Head Accountant cannot confirm a correction they entered or a transfer they requested
        // Case d1: HA entered the correction -> cannot confirm it
        int d1TransferId = transferType == "Transfer" ? 2205 : 2206;
        context.BudgetTransfers.Add(new BudgetTransfer
        {
            TransferId = d1TransferId,
            FiscalYear = 2026,
            FromDepartmentCode = "DEP-FIN",
            FromCategory = "Supplies",
            FromMonth = 1,
            ToDepartmentCode = "DEP-DSP",
            ToCategory = "Fuel",
            ToMonth = 1,
            Amount = 5000m,
            TransferType = transferType,
            RequestedBy = "EMP-003",
            RequestedAt = DateTime.UtcNow,
            Status = "Pending"
        });
        await context.SaveChangesAsync();

        var recD1Orig = await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = gateItemType,
            ItemId = d1TransferId.ToString(),
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow
        }, "EMP-001", "Finance Manager");

        var haEnteredCorrection = await gateService.RecordCorrectionAsync(new CreateCeoApprovalCorrectionRequest
        {
            OriginalRecordId = recD1Orig.RecordId,
            ItemType = gateItemType,
            ItemId = d1TransferId.ToString(),
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow,
            CorrectionReason = "Entered by EMP-002 during FM delegation"
        }, "EMP-002", "Finance Manager");

        var exSelf = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gateService.ConfirmRecordAsync(haEnteredCorrection.RecordId, "Recording", "EMP-002", "Head Accountant"));
        Assert.Contains("The confirmer must strictly differ from the person who entered the approval record", exSelf.Message);

        // Case d2: HA requested the transfer -> cannot confirm it
        int haTransferId = transferType == "Transfer" ? 2203 : 2204;
        context.BudgetTransfers.Add(new BudgetTransfer
        {
            TransferId = haTransferId,
            FiscalYear = 2026,
            FromDepartmentCode = "DEP-FIN",
            FromCategory = "Supplies",
            FromMonth = 1,
            ToDepartmentCode = "DEP-DSP",
            ToCategory = "Fuel",
            ToMonth = 1,
            Amount = 5000m,
            TransferType = transferType,
            RequestedBy = "EMP-002", // Head Accountant requested
            RequestedAt = DateTime.UtcNow,
            Status = "Pending"
        });
        await context.SaveChangesAsync();

        var recHaTransfer = await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = gateItemType,
            ItemId = haTransferId.ToString(),
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow
        }, "EMP-001", "Finance Manager");

        var exRequester = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gateService.ConfirmRecordAsync(recHaTransfer.RecordId, "Recording", "EMP-002", "Head Accountant"));
        Assert.Contains("The Head Accountant confirming the transfer cannot be the employee who requested it", exRequester.Message);

        // (c) Head Accountant confirms with Kind "Recording" on the FM-entered correction record -> gate satisfied
        var conf = await gateService.ConfirmRecordAsync(
            correctionRec.RecordId, "Recording", "EMP-002", "Head Accountant", "Confirmed corrected resolution by Head Accountant");
        Assert.NotNull(conf);
        Assert.Equal("Recording", conf.Kind);
        Assert.Equal("EMP-002", conf.ConfirmedBy);

        var checkAfter = await gateService.CheckGateAsync(gateItemType, transferId.ToString());
        Assert.True(checkAfter.IsSatisfied);

        var approved = await budgetService.ApproveBudgetTransferAsync(transferId, "EMP-001", "Finance Manager");
        Assert.NotNull(approved);
        Assert.Equal("Approved", approved.Status);

        // Amounts update once
        var fromLine = await context.MonthlyBudgetLines.FirstAsync(l => l.BudgetLineId == transferId * 10 + 1);
        var toLine = await context.MonthlyBudgetLines.FirstAsync(l => l.BudgetLineId == transferId * 10 + 2);
        Assert.Equal(40000m, fromLine.BudgetAmount);
        Assert.Equal(60000m, toLine.BudgetAmount);
    }

    [Fact]
    public async Task R511_Item1_CorrectionRecord_OtherItemTypes_StillFollowsCorrectionKindFlow()
    {
        using var context = CreateInMemoryDbContext();
        var gateService = new CeoApprovalGateService(context);

        // Seed Reimbursement item 550 so VerifyItemExistsAsync succeeds
        context.Reimbursements.Add(new Reimbursement
        {
            ReimbursementId = 550,
            ReferenceNumber = "REIM-550",
            EmployeeId = "EMP-003",
            Amount = 25000m,
            DepartmentCode = "DEP-OPS",
            Category = "Supplies",
            FiscalYear = 2026,
            Month = 1,
            Status = "Validated"
        });
        await context.SaveChangesAsync();

        // Item 5: Reimbursement
        var orig = await gateService.RecordApprovalAsync(new CreateCeoApprovalRecordRequest
        {
            ItemType = CeoApprovalItemTypes.Reimbursement,
            ItemId = "550",
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow
        }, "EMP-001", "Finance Manager");

        // Record correction
        var corr = await gateService.RecordCorrectionAsync(new CreateCeoApprovalCorrectionRequest
        {
            OriginalRecordId = orig.RecordId,
            ItemType = CeoApprovalItemTypes.Reimbursement,
            ItemId = "550",
            Status = "Approved",
            ApprovalDate = DateTime.UtcNow,
            CorrectionReason = "Adjusted memo reference"
        }, "EMP-001", "Finance Manager");

        // Before confirmation -> blocked
        var checkBefore = await gateService.CheckGateAsync(CeoApprovalItemTypes.Reimbursement, "550", 25000m);
        Assert.False(checkBefore.IsSatisfied);
        Assert.Contains("requires confirmation by a second authorized employee", checkBefore.Message);

        // Non-Head Accountant (Accountant EMP-003) confirms with Kind "Correction" -> valid for other item types
        var conf = await gateService.ConfirmRecordAsync(corr.RecordId, "Correction", "EMP-003", "Accountant");
        Assert.NotNull(conf);
        Assert.Equal("Correction", conf.Kind);

        // Gate satisfied
        var checkAfter = await gateService.CheckGateAsync(CeoApprovalItemTypes.Reimbursement, "550", 25000m);
        Assert.True(checkAfter.IsSatisfied);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PART 2 — Adjusting Entries Governance (R4.11 q4 / FIN-23)
    // Handler: AdjustmentFeatures.ApproveAdjustmentCommand
    // ─────────────────────────────────────────────────────────────────────────

    private ApplicationDbContext CreateAdjustmentDbContext(
        string invoiceId       = "INV-ADJ-001",
        decimal invoiceBalance = 10000m,
        bool    invoicePaid    = false,
        string  adjId          = "ADJ-T001",
        decimal adjAmount      = 500m,
        string  adjType        = "Credit",
        string  adjBy          = "EMP-001",          // Finance Manager
        string? adjDate        = null,
        bool    closedPeriod   = false)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var ctx = new ApplicationDbContext(options);

        ctx.Invoices.Add(new DomainInvoice
        {
            Id           = invoiceId,
            InvoiceNo    = invoiceId,
            ClientId     = "CLI-ADJ",
            TotalAmount  = invoicePaid ? 10000m : 10000m,
            Balance      = invoiceBalance,
            PaymentStatus = invoicePaid ? "Paid" : "Unpaid",
            DocumentStatus = InvoiceDocumentStatus.ForReview
        });

        ctx.PaymentAdjustments.Add(new DomainAdjustment
        {
            Id             = adjId,
            InvoiceNo      = invoiceId,
            AdjustmentType = adjType,
            Amount         = adjAmount,
            Reason         = "Test adjustment reason",
            AdjustedBy     = adjBy,
            ApprovedBy     = string.Empty,
            DateRequested  = adjDate ?? "2026-11-05",
            DateApproved   = string.Empty,
            Status         = "Pending"
        });

        if (closedPeriod)
        {
            ctx.AccountingPeriodCloses.Add(new DomainPeriodClose
            {
                PeriodStart = new DateTime(2026, 11, 1),
                PeriodEnd   = new DateTime(2026, 11, 30),
                ClosedAt    = DateTime.UtcNow,
                ClosedBy    = "EMP-001"
            });
        }

        ctx.SaveChanges();
        return ctx;
    }

    // TC-ADJ-001: Finance Manager trying to approve → UnauthorizedAccessException
    [Fact]
    public async Task TCADJ001_FinanceManager_ApproveAdjustment_IsRejected()
    {
        using var ctx = CreateAdjustmentDbContext();
        var handler = new AdjustmentFeatures.ApproveAdjustmentCommandHandler(ctx);

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new AdjustmentFeatures.ApproveAdjustmentCommand("ADJ-T001", "EMP-001", "Finance Manager"), default));

        Assert.Contains("Head Accountant", ex.Message);
    }

    // TC-ADJ-002: Accountant trying to approve → UnauthorizedAccessException
    [Fact]
    public async Task TCADJ002_Accountant_ApproveAdjustment_IsRejected()
    {
        using var ctx = CreateAdjustmentDbContext();
        var handler = new AdjustmentFeatures.ApproveAdjustmentCommandHandler(ctx);

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new AdjustmentFeatures.ApproveAdjustmentCommand("ADJ-T001", "EMP-003", "Accountant"), default));

        Assert.Contains("Head Accountant", ex.Message);
    }

    // TC-ADJ-003: Assistant of Finance Manager trying to approve → UnauthorizedAccessException
    [Fact]
    public async Task TCADJ003_AssistantFM_ApproveAdjustment_IsRejected()
    {
        using var ctx = CreateAdjustmentDbContext();
        var handler = new AdjustmentFeatures.ApproveAdjustmentCommandHandler(ctx);

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new AdjustmentFeatures.ApproveAdjustmentCommand("ADJ-T001", "EMP-004", "Assistant of Finance Manager"), default));

        Assert.Contains("Head Accountant", ex.Message);
    }

    // TC-ADJ-004: Head Accountant approves FM's adjustment → succeeds; DateApproved is a valid date;
    //             invoice balance decreases by the adjustment amount
    [Fact]
    public async Task TCADJ004_HeadAccountant_ApproveAdjustment_Succeeds_BalanceDecreases()
    {
        using var ctx = CreateAdjustmentDbContext(invoiceBalance: 10000m, adjAmount: 500m, adjType: "Credit", adjBy: "EMP-001");
        var handler = new AdjustmentFeatures.ApproveAdjustmentCommandHandler(ctx);

        var result = await handler.Handle(
            new AdjustmentFeatures.ApproveAdjustmentCommand("ADJ-T001", "EMP-002", "Head Accountant"), default);

        Assert.NotNull(result);
        Assert.Equal("Approved", result!.Status);
        Assert.Equal("EMP-002", result.ApprovedBy);
        Assert.False(string.IsNullOrEmpty(result.DateApproved), "DateApproved must be set on approval");
        Assert.True(DateTime.TryParse(result.DateApproved, out _), "DateApproved must be a parseable date");

        // Invoice balance should decrease: credit is applied
        var inv = await ctx.Invoices.FirstAsync(i => i.InvoiceNo == "INV-ADJ-001");
        // CreditMemoAmount increased → RecalculateInvoice reduces Balance
        Assert.True(inv.Balance < 10000m, $"Balance should decrease from 10000; got {inv.Balance}");
    }

    // TC-ADJ-005: Separation of Duties — HA approves their own adjustment → InvalidOperationException
    [Fact]
    public async Task TCADJ005_HeadAccountant_ApprovesOwnAdjustment_IsRejected()
    {
        // adjBy = EMP-002 (HA), approver = EMP-002 (HA) — same person
        using var ctx = CreateAdjustmentDbContext(adjBy: "EMP-002");
        var handler = new AdjustmentFeatures.ApproveAdjustmentCommandHandler(ctx);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new AdjustmentFeatures.ApproveAdjustmentCommand("ADJ-T001", "EMP-002", "Head Accountant"), default));

        Assert.Contains("cannot approve their own", ex.Message);
    }

    // TC-ADJ-006: Creating an adjustment in a closed period → InvalidOperationException
    [Fact]
    public async Task TCADJ006_Create_AdjustmentInClosedPeriod_IsRejected()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var ctx = new ApplicationDbContext(options);

        ctx.Invoices.Add(new DomainInvoice
        {
            Id = "INV-CLOSED", InvoiceNo = "INV-CLOSED", ClientId = "CLI-C", TotalAmount = 5000m,
            Balance = 5000m, DocumentStatus = InvoiceDocumentStatus.ForReview
        });
        ctx.AccountingPeriodCloses.Add(new DomainPeriodClose
        {
            PeriodStart = new DateTime(2026, 10, 1),
            PeriodEnd   = new DateTime(2026, 10, 31),
            ClosedAt    = DateTime.UtcNow,
            ClosedBy    = "EMP-001"
        });
        await ctx.SaveChangesAsync();

        var handler = new AdjustmentFeatures.CreateAdjustmentCommandHandler(ctx);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new AdjustmentFeatures.CreateAdjustmentCommand(
                InvoiceNo:      "INV-CLOSED",
                AdjustmentType: "Credit",
                Amount:         200m,
                Reason:         "Test closed-period block",
                AdjustedBy:     "EMP-001",
                ApprovedBy:     string.Empty,
                Role:           "Finance Manager",
                AdjustmentDate: new DateTime(2026, 10, 15)   // ← inside closed period
            ), default));

        Assert.Contains("closed accounting period", ex.Message);
    }

    // TC-ADJ-007: Approving an adjustment whose date is in a closed period → InvalidOperationException
    [Fact]
    public async Task TCADJ007_Approve_AdjustmentInClosedPeriod_IsRejected()
    {
        // adjDate = Nov 10 is inside the closed Nov period
        using var ctx = CreateAdjustmentDbContext(adjDate: "2026-11-10", closedPeriod: true);
        var handler = new AdjustmentFeatures.ApproveAdjustmentCommandHandler(ctx);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new AdjustmentFeatures.ApproveAdjustmentCommand("ADJ-T001", "EMP-002", "Head Accountant"), default));

        Assert.Contains("closed accounting period", ex.Message);
    }

    // TC-ADJ-008: Credit memo on paid invoice posts balanced journal entry (debit = credit = amount)
    [Fact]
    public async Task TCADJ008_PaidInvoiceCreditMemo_PostsBalancedJournalEntry()
    {
        // balance = 0 (paid invoice) triggers PostCreditMemoJournalEntryAsync
        // Use a BI- invoice number so SyncBillingInvoiceAndReceivableAsync skips the
        // "create billing mirror" branch (invoice.InvoiceNo == billingInvoiceNo → guard is false).
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        await using var ctx = new ApplicationDbContext(options);
        ctx.Clients.Add(new FOMS.Domain.Entities.Client
        {
            Id = "CLI-ADJ",
            Name = "Test Client ADJ",
            Status = "Active",
            IsActive = true
        });
        ctx.Invoices.Add(new DomainInvoice
        {
            Id            = "BI-ADJ-001",
            InvoiceNo     = "BI-ADJ-001",
            ClientId      = "CLI-ADJ",
            TotalAmount   = 10000m,
            Balance       = 0m,            // paid — triggers credit memo JE path
            PaymentStatus = "Paid",
            DocumentStatus = InvoiceDocumentStatus.ForReview
        });
        ctx.PaymentAdjustments.Add(new DomainAdjustment
        {
            Id             = "ADJ-T001",
            InvoiceNo      = "BI-ADJ-001",
            AdjustmentType = "Credit",
            Amount         = 300m,
            Reason         = "Paid invoice credit memo test",
            AdjustedBy     = "EMP-001",
            ApprovedBy     = string.Empty,
            DateRequested  = "2026-11-05",
            DateApproved   = string.Empty,
            Status         = "Pending"
        });
        await ctx.SaveChangesAsync();
        var handler = new AdjustmentFeatures.ApproveAdjustmentCommandHandler(ctx);

        await handler.Handle(
            new AdjustmentFeatures.ApproveAdjustmentCommand("ADJ-T001", "EMP-002", "Head Accountant"), default);

        var entries = await ctx.GeneralLedgerEntries.ToListAsync();
        Assert.Equal(2, entries.Count);

        var totalDebit  = entries.Sum(e => e.Debit);
        var totalCredit = entries.Sum(e => e.Credit);
        Assert.Equal(300m, totalDebit);
        Assert.Equal(300m, totalCredit);
    }
}
