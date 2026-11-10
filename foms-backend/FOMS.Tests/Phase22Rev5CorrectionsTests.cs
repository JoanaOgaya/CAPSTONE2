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
    public async Task G1_Matrix_Item8_Afs_WithoutAttachment_ThrowsArgumentException()
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

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            gateService.RecordApprovalAsync(req, "EMP-001", "Finance Manager"));
        Assert.Contains("strictly requires an attachment", ex.Message);
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
}
