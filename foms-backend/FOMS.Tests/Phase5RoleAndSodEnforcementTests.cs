using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using FOMS.Api.Controllers;
using FOMS.Application.Configuration;
using FOMS.Application.Contracts;
using FOMS.Application.Interfaces;
using FOMS.Domain.Entities.SqlServer;
using FOMS.Infrastructure.Persistence;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FOMS.Tests;

public class Phase5RoleAndSodEnforcementTests
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

    private static void SetUserContext(ControllerBase controller, string role, string username, string empId)
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
        httpContext.Request.Headers["X-User-Role"] = role;
        httpContext.Request.Headers["X-User-Username"] = username;

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. STARS IntegrationController Role Enforcement (403 Tests)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StarsIntegration_AfmIsBarredWith403_FromAllReadAndWriteEndpoints()
    {
        using var fomsDb = CreateFomsDb(nameof(StarsIntegration_AfmIsBarredWith403_FromAllReadAndWriteEndpoints));
        using var appDb = CreateAppDb(nameof(StarsIntegration_AfmIsBarredWith403_FromAllReadAndWriteEndpoints));
        var starsMock = new Mock<IStarsIntegrationService>();
        var userMock = new Mock<IUserService>();
        var opts = Options.Create(new StarsIntegrationOptions { InboundApiKey = "valid-stars-inbound-key-12345" });

        var controller = new IntegrationController(starsMock.Object, userMock.Object, appDb, fomsDb, opts);
        SetUserContext(controller, "Assistant of Finance Manager", "EMP-004", "EMP-004");

        var readSummary = await controller.GetStarsArSummary();
        var readTasks = await controller.GetCollectionTasks(null);
        var readLink = await controller.GetEndorsementLink("INV-001");
        var writeEndorse = await controller.TriggerEndorsement("INV-001", null);
        var writeTask = await controller.CreateCollectionTask(new StarsCollectionTaskRequestDto { InvoiceNumber = "INV-001" });

        Assert.Equal(403, Assert.IsType<ObjectResult>(readSummary).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(readTasks).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(readLink).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(writeEndorse).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(writeTask).StatusCode);
    }

    [Fact]
    public async Task StarsIntegration_AccountantCanRead_ButGets403OnWriteEndorseAndTasks()
    {
        using var fomsDb = CreateFomsDb(nameof(StarsIntegration_AccountantCanRead_ButGets403OnWriteEndorseAndTasks));
        using var appDb = CreateAppDb(nameof(StarsIntegration_AccountantCanRead_ButGets403OnWriteEndorseAndTasks));
        var starsMock = new Mock<IStarsIntegrationService>();
        var userMock = new Mock<IUserService>();
        var opts = Options.Create(new StarsIntegrationOptions { InboundApiKey = "valid-stars-inbound-key-12345" });

        var controller = new IntegrationController(starsMock.Object, userMock.Object, appDb, fomsDb, opts);
        SetUserContext(controller, "Accountant", "EMP-003", "EMP-003");

        // Read allowed (200 OK)
        var readSummary = await controller.GetStarsArSummary();
        Assert.IsType<OkObjectResult>(readSummary);

        // Write/sync/task endpoints barred (403 Forbidden)
        var writeEndorse = await controller.TriggerEndorsement("INV-001", null);
        var writeSync = await controller.TriggerStatusSync("INV-001");
        var writeOverdue = await controller.TriggerOverdueAlert("INV-001");
        var writeTask = await controller.CreateCollectionTask(new StarsCollectionTaskRequestDto { InvoiceNumber = "INV-001" });
        var execTask = await controller.ExecuteCollectionTask(1, new StarsCollectionExecutionDto());

        Assert.Equal(403, Assert.IsType<ObjectResult>(writeEndorse).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(writeSync).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(writeOverdue).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(writeTask).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(execTask).StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. DMS DmsIntegrationController Role Enforcement (403 Tests)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DmsIntegration_AfmIsBarredWith403_FromAllDmsEndpoints()
    {
        using var fomsDb = CreateFomsDb(nameof(DmsIntegration_AfmIsBarredWith403_FromAllDmsEndpoints));
        var dmsMock = new Mock<IDmsIntegrationService>();
        var opts = Options.Create(new DmsIntegrationOptions { InboundApiKey = "valid-dms-inbound-key-12345" });

        var controller = new DmsIntegrationController(
            dmsMock.Object,
            opts,
            NullLogger<DmsIntegrationController>.Instance,
            fomsDb);

        SetUserContext(controller, "Assistant of Finance Manager", "EMP-004", "EMP-004");

        var r1 = await controller.WaybillCreated(new DmsWaybillCreatedDto { WaybillNumber = "WB-001" });
        var r2 = await controller.DeliveryCompleted(new DmsDeliveryCompletedRequest { WaybillNumber = "WB-001" });
        var r3 = await controller.DeliveryFailed(new DmsDeliveryFailedRequest { WaybillNumber = "WB-001" });
        var r4 = await controller.GetWaybillStatus("WB-001");

        Assert.Equal(403, Assert.IsType<ObjectResult>(r1).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(r2).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(r3).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(r4).StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. OCR & Duplicate Scan (403 for FM & HA) + Duplicate Review (403 for ACC & AFM)
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Finance Manager", "EMP-001")]
    [InlineData("Head Accountant", "EMP-002")]
    public async Task AiDuplicatesAndOcr_FmAndHaReceive403_OnAllScanAndDecideEndpoints(string role, string empId)
    {
        using var fomsDb = CreateFomsDb($"{nameof(AiDuplicatesAndOcr_FmAndHaReceive403_OnAllScanAndDecideEndpoints)}_{empId}");
        using var appDb = CreateAppDb($"{nameof(AiDuplicatesAndOcr_FmAndHaReceive403_OnAllScanAndDecideEndpoints)}_{empId}");
        var httpFactory = new Mock<IHttpClientFactory>();
        var config = new ConfigurationBuilder().Build();

        var dupController = new AiDuplicatesController(appDb, fomsDb, httpFactory.Object, config);
        SetUserContext(dupController, role, empId, empId);

        var rValidate = await dupController.ValidateDocument(null, null);
        var rScan = await dupController.ScanDocument(null, null, null);
        var rDupScan = await dupController.DuplicateScanEndpoint(null, null);
        var rDecide = await dupController.Decide("1", new DecideDto { Decision = "DUPLICATE", Remarks = "Test" });
        var rSubmitAlert = await dupController.SubmitAlertForReview("1", new AlertSubmitDto());
        var rConfirmUnique = await dupController.ConfirmUniqueDocumentEndpoint(new ConfirmUniqueDto { DocumentNo = "DOC-1" });

        Assert.Equal(403, Assert.IsType<ObjectResult>(rValidate).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(rScan).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(rDupScan).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(rDecide).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(rSubmitAlert).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(rConfirmUnique).StatusCode);

        var afmController = new AssistantFinanceManagerController(fomsDb);
        SetUserContext(afmController, role, empId, empId);

        var rAfmScan = await afmController.DuplicateScan(null);
        var rAfmOcr = await afmController.DocumentExtraction(null);

        Assert.Equal(403, Assert.IsType<ObjectResult>(rAfmScan).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(rAfmOcr).StatusCode);

        var featController = new AiFeaturesController(fomsDb, httpFactory.Object, config);
        SetUserContext(featController, role, empId, empId);
        var rFeatUpload = await featController.UploadDocument(null, null);
        Assert.Equal(403, Assert.IsType<ObjectResult>(rFeatUpload).StatusCode);
    }

    [Theory]
    [InlineData("Accountant", "EMP-003")]
    [InlineData("Assistant of Finance Manager", "EMP-004")]
    public async Task DuplicateReview_AccountantAndAfmReceive403_OnSupervisorReviewEndpoints(string role, string empId)
    {
        using var fomsDb = CreateFomsDb($"{nameof(DuplicateReview_AccountantAndAfmReceive403_OnSupervisorReviewEndpoints)}_{empId}");
        using var appDb = CreateAppDb($"{nameof(DuplicateReview_AccountantAndAfmReceive403_OnSupervisorReviewEndpoints)}_{empId}");
        var httpFactory = new Mock<IHttpClientFactory>();
        var config = new ConfigurationBuilder().Build();

        var dupController = new AiDuplicatesController(appDb, fomsDb, httpFactory.Object, config);
        SetUserContext(dupController, role, empId, empId);

        var rReview = await dupController.Review("1", new ReviewDto { Action = "APPROVE" });
        var rAlertDec = await dupController.SubmitAlertDecision("1", new AlertDecisionDto { Decision = "DUPLICATE", Remarks = "Test" });

        Assert.Equal(403, Assert.IsType<ObjectResult>(rReview).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(rAlertDec).StatusCode);

        var haController = new HeadAccountantController(fomsDb);
        SetUserContext(haController, role, empId, empId);

        var rHaApprove = await haController.ApproveForReview(1);
        var rHaReject = await haController.RejectForReview(1);

        Assert.Equal(403, Assert.IsType<ObjectResult>(rHaApprove).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(rHaReject).StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. Liquidations, Cash Advances & Finance Payments Role Enforcement
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Liquidations_AccountantReceives403_WhileHaPreExistingBehaviorIsRestored()
    {
        using var fomsDb = CreateFomsDb(nameof(Liquidations_AccountantReceives403_WhileHaPreExistingBehaviorIsRestored));
        var controller = new LiquidationsController(fomsDb);

        // Accountant (EMP-003) receives 403 on Create, Update, and Act
        SetUserContext(controller, "Accountant", "EMP-003", "EMP-003");
        var rCreate = await controller.Create(new LiquidationDto { Purpose = "Test" });
        var rUpdate = await controller.Update(1, new LiquidationDto { Status = "Endorsed" });
        var rAct = await controller.Act(1, "approve", null);

        Assert.Equal(403, Assert.IsType<ObjectResult>(rCreate).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(rUpdate).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(rAct).StatusCode);

        // Head Accountant (EMP-002) pre-existing behavior is restored (allowed to create, update, and act on another submitter's liquidation)
        SetUserContext(controller, "Assistant of Finance Manager", "EMP-004", "EMP-004");
        var afmCreate = await controller.Create(new LiquidationDto { Purpose = "AFM Liquidation", SubmittedByName = "EMP-004", SubmittedByEmployeeId = "EMP-004", TotalAmount = 500m });
        SetUserContext(controller, "Head Accountant", "EMP-002", "EMP-002");
        Assert.IsType<CreatedAtActionResult>(afmCreate);

        var createdLiq = await fomsDb.Liquidations.FirstAsync();
        var haAct = await controller.Act(createdLiq.LiquidationId, "validate", null);
        Assert.IsType<OkObjectResult>(haAct);
    }

    [Theory]
    [InlineData("Accountant", "EMP-003")]
    [InlineData("Head Accountant", "EMP-002")]
    [InlineData("Assistant of Finance Manager", "EMP-004")]
    public async Task FinancePayments_NonFmRolesReceive403_OnValidatePayment(string role, string empId)
    {
        using var fomsDb = CreateFomsDb($"{nameof(FinancePayments_NonFmRolesReceive403_OnValidatePayment)}_{empId}");
        using var appDb = CreateAppDb($"{nameof(FinancePayments_NonFmRolesReceive403_OnValidatePayment)}_{empId}");

        var financePaymentsController = new FinancePaymentsController(appDb, fomsDb, new Mock<IMediator>().Object);
        SetUserContext(financePaymentsController, role, empId, empId);

        var res = await financePaymentsController.ValidatePayment("PAY-001", new FinancePaymentsController.ValidationActionRequest("Approve"));
        Assert.Equal(403, Assert.IsType<ObjectResult>(res).StatusCode);
    }

    [Fact]
    public async Task CashAdvances_NonStaffReceives403OnCreate_AndAccountantReceives403OnApprove()
    {
        using var fomsDb = CreateFomsDb(nameof(CashAdvances_NonStaffReceives403OnCreate_AndAccountantReceives403OnApprove));
        var caController = new CashAdvancesController(fomsDb, NullLogger<CashAdvancesController>.Instance);

        // Non-staff ("Client") receives 403 on Create
        SetUserContext(caController, "Client", "CLI-001", "CLI-001");
        var rClientCreate = await caController.Create(new CashAdvanceCreateDto { Amount = 1000m, Purpose = "Fuel" });
        Assert.Equal(403, Assert.IsType<ObjectResult>(rClientCreate).StatusCode);

        // Head Accountant ("EMP-002") is in canRequestCashAdvance and succeeds on Create
        SetUserContext(caController, "Head Accountant", "EMP-002", "EMP-002");
        var rHaCreate = await caController.Create(new CashAdvanceCreateDto { Amount = 1500m, Purpose = "Field Audit", DepartmentCode = "DEP-FIN" });
        Assert.IsType<OkObjectResult>(rHaCreate);

        var createdCa = await fomsDb.CashAdvances.FirstAsync();

        // Accountant ("EMP-003") receives 403 on Approve
        SetUserContext(caController, "Accountant", "EMP-003", "EMP-003");
        var rAccApprove = await caController.Approve(createdCa.AdvanceId, null);
        Assert.Equal(403, Assert.IsType<ObjectResult>(rAccApprove).StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5. Separation of Duties (submitter != approver -> 403) Across All Flows
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sod_DuplicateReview_BlocksSelfApprovalWith403()
    {
        using var fomsDb = CreateFomsDb(nameof(Sod_DuplicateReview_BlocksSelfApprovalWith403));
        using var appDb = CreateAppDb(nameof(Sod_DuplicateReview_BlocksSelfApprovalWith403));
        var httpFactory = new Mock<IHttpClientFactory>();
        var config = new ConfigurationBuilder().Build();

        fomsDb.AIReviewHistories.Add(new AIReviewHistory
        {
            ReviewId = 501,
            DocumentNumber = "INV-SOD-501",
            Reviewer = "EMP-002",
            FinalDecision = "PENDING_REVIEW",
            DecisionReason = "[DUPLICATE] Marked by EMP-002",
            ReviewedDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        });
        await fomsDb.SaveChangesAsync();

        var dupController = new AiDuplicatesController(appDb, fomsDb, httpFactory.Object, config);
        SetUserContext(dupController, "Head Accountant", "EMP-002", "EMP-002");

        var res1 = await dupController.Review("501", new ReviewDto { Action = "APPROVE" });
        Assert.Equal(403, Assert.IsType<ObjectResult>(res1).StatusCode);

        var haController = new HeadAccountantController(fomsDb);
        SetUserContext(haController, "Head Accountant", "EMP-002", "EMP-002");

        var res2 = await haController.ApproveForReview(501);
        Assert.Equal(403, Assert.IsType<ObjectResult>(res2).StatusCode);
    }

    [Fact]
    public async Task DuplicateAlertStateMachine_WalksFlaggedToPendingReviewToReturnedApprovedAndCleared_RejectsIllegalTransition_AndRejectsSelfApproval()
    {
        using var fomsDb = CreateFomsDb(nameof(DuplicateAlertStateMachine_WalksFlaggedToPendingReviewToReturnedApprovedAndCleared_RejectsIllegalTransition_AndRejectsSelfApproval));
        using var appDb = CreateAppDb(nameof(DuplicateAlertStateMachine_WalksFlaggedToPendingReviewToReturnedApprovedAndCleared_RejectsIllegalTransition_AndRejectsSelfApproval));
        var httpFactory = new Mock<IHttpClientFactory>();
        var config = new ConfigurationBuilder().Build();

        // 1. Seed initial alert in FLAGGED state
        fomsDb.AIReviewHistories.Add(new AIReviewHistory
        {
            ReviewId = 601,
            DocumentNumber = "INV-DUP-601",
            Reviewer = "AI Engine",
            FinalDecision = "FLAGGED",
            DecisionReason = "98% similarity match",
            ReviewedDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        });
        await fomsDb.SaveChangesAsync();

        var dupController = new AiDuplicatesController(appDb, fomsDb, httpFactory.Object, config);

        // 2. Illegal transition: Level 2 Review directly from FLAGGED (before Level 1 decision) -> 400 BadRequest
        SetUserContext(dupController, "Head Accountant", "EMP-002", "EMP-002");
        var illegalFromFlagged = await dupController.Review("601", new ReviewDto { Action = "APPROVE", Decision = "DUPLICATE" });
        Assert.IsType<BadRequestObjectResult>(illegalFromFlagged);

        // 3. Valid transition: Level 1 Accountant (EMP-003) decides DUPLICATE -> FLAGGED -> PENDING_REVIEW
        SetUserContext(dupController, "Accountant", "EMP-003", "EMP-003");
        var decideRes1 = await dupController.Decide("601", new DecideDto { Decision = "DUPLICATE", Remarks = "Verified duplicate invoice number" });
        Assert.IsType<OkObjectResult>(decideRes1);
        Assert.Equal("PENDING_REVIEW", (await fomsDb.AIReviewHistories.FindAsync(601))!.FinalDecision);

        // 4. Illegal transition: Level 1 Decide again while already in PENDING_REVIEW -> 400 BadRequest
        var illegalDoubleDecide = await dupController.Decide("601", new DecideDto { Decision = "NOT_DUPLICATE", Remarks = "Changed mind before return" });
        Assert.IsType<BadRequestObjectResult>(illegalDoubleDecide);

        // 5. Valid transition: Level 2 Head Accountant (EMP-002) returns alert -> PENDING_REVIEW -> RETURNED
        SetUserContext(dupController, "Head Accountant", "EMP-002", "EMP-002");
        var returnRes = await dupController.Review("601", new ReviewDto { Action = "RETURN", Remarks = "Please re-verify billing period" });
        Assert.IsType<OkObjectResult>(returnRes);
        Assert.Equal("RETURNED", (await fomsDb.AIReviewHistories.FindAsync(601))!.FinalDecision);

        // 6. Valid transition from RETURNED: Level 1 Accountant (EMP-003) re-submits as NOT_DUPLICATE -> RETURNED -> PENDING_REVIEW
        SetUserContext(dupController, "Accountant", "EMP-003", "EMP-003");
        var decideRes2 = await dupController.Decide("601", new DecideDto { Decision = "NOT_DUPLICATE", Remarks = "Different billing month confirmed" });
        Assert.IsType<OkObjectResult>(decideRes2);
        Assert.Equal("PENDING_REVIEW", (await fomsDb.AIReviewHistories.FindAsync(601))!.FinalDecision);

        // 7. Separation of Duties: Submitter ("EMP-003") attempting Level 2 Review (even if role were elevated) -> 403 Forbidden
        SetUserContext(dupController, "Head Accountant", "EMP-003", "EMP-003");
        var selfReviewRes = await dupController.Review("601", new ReviewDto { Action = "APPROVE", Decision = "NOT_DUPLICATE" });
        Assert.Equal(403, Assert.IsType<ObjectResult>(selfReviewRes).StatusCode);

        // 8. Valid transition: Level 2 Head Accountant (EMP-002) approves NOT_DUPLICATE -> PENDING_REVIEW -> CLEARED_NOT_DUPLICATE
        SetUserContext(dupController, "Head Accountant", "EMP-002", "EMP-002");
        var clearRes = await dupController.Review("601", new ReviewDto { Action = "APPROVE", Decision = "NOT_DUPLICATE", Remarks = "Cleared as legitimate" });
        Assert.IsType<OkObjectResult>(clearRes);
        Assert.Equal("CLEARED_NOT_DUPLICATE", (await fomsDb.AIReviewHistories.FindAsync(601))!.FinalDecision);

        // 9. Illegal transition from terminal CLEARED_NOT_DUPLICATE -> 400 BadRequest
        var illegalAfterTerminal = await dupController.Review("601", new ReviewDto { Action = "RETURN", Remarks = "Cannot re-open terminal alert" });
        Assert.IsType<BadRequestObjectResult>(illegalAfterTerminal);

        // 10. Also verify PENDING_REVIEW -> APPROVED_DUPLICATE terminal path on alert 602
        fomsDb.AIReviewHistories.Add(new AIReviewHistory
        {
            ReviewId = 602,
            DocumentNumber = "INV-DUP-602",
            Reviewer = "AI Engine",
            FinalDecision = "FLAGGED",
            DecisionReason = "Exact reference match",
            ReviewedDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        });
        await fomsDb.SaveChangesAsync();

        SetUserContext(dupController, "Accountant", "EMP-003", "EMP-003");
        await dupController.Decide("602", new DecideDto { Decision = "DUPLICATE", Remarks = "Exact duplicate" });

        SetUserContext(dupController, "Finance Manager", "EMP-001", "EMP-001");
        var approveDupRes = await dupController.Review("602", new ReviewDto { Action = "APPROVE", Decision = "DUPLICATE", Remarks = "Confirmed duplicate" });
        Assert.IsType<OkObjectResult>(approveDupRes);
        Assert.Equal("APPROVED_DUPLICATE", (await fomsDb.AIReviewHistories.FindAsync(602))!.FinalDecision);
    }

    [Fact]
    public async Task Sod_Payments_BlocksRecorderFromValidatingOrApprovingOwnPaymentWith403()
    {
        using var fomsDb = CreateFomsDb(nameof(Sod_Payments_BlocksRecorderFromValidatingOrApprovingOwnPaymentWith403));
        using var appDb = CreateAppDb(nameof(Sod_Payments_BlocksRecorderFromValidatingOrApprovingOwnPaymentWith403));

        appDb.Payments.Add(new FOMS.Domain.Entities.Payment
        {
            Id = "PAY-SOD-001",
            InvoiceId = "INV-001",
            ClientId = "CLI-001",
            Amount = 5000m,
            PaymentMethod = "Bank Transfer",
            ReferenceNumber = "REF-SOD-001",
            PaymentStatus = "Pending Validation",
            RecordedBy = "EMP-001",
            SubmittedAt = DateTime.UtcNow
        });
        await appDb.SaveChangesAsync();

        fomsDb.Payments.Add(new Payment
        {
            PaymentId = 701,
            InvoiceId = 1,
            Amount = 5000m,
            PaymentMethod = "Bank Transfer",
            ReferenceNumber = "REF-SOD-001",
            PaymentStatus = "Pending Validation",
            Remarks = "RecordedBy:EMP-002",
            SubmittedAt = DateTime.UtcNow
        });
        await fomsDb.SaveChangesAsync();

        var paymentsController = new PaymentsController(appDb, fomsDb, NullLogger<PaymentsController>.Instance);
        SetUserContext(paymentsController, "Finance Manager", "EMP-001", "EMP-001");

        var payRes = await paymentsController.ActionOnPayment("PAY-SOD-001", "approve", null);
        Assert.Equal(403, Assert.IsType<ObjectResult>(payRes).StatusCode);

        var financePaymentsController = new FinancePaymentsController(appDb, fomsDb, new Mock<IMediator>().Object);
        SetUserContext(financePaymentsController, "Finance Manager", "EMP-001", "EMP-001");

        var finPayRes = await financePaymentsController.ValidatePayment("PAY-SOD-001", new FinancePaymentsController.ValidationActionRequest("Approve"));
        Assert.Equal(403, Assert.IsType<ObjectResult>(finPayRes).StatusCode);

        var haController = new HeadAccountantController(fomsDb);
        SetUserContext(haController, "Head Accountant", "EMP-002", "EMP-002");

        var haPayRes = await haController.ValidatePayment(701);
        Assert.Equal(403, Assert.IsType<ObjectResult>(haPayRes).StatusCode);
    }

    [Fact]
    public async Task Sod_LiquidationsAndCashAdvances_BlocksSubmitterFromEndorsingOrApprovingOwnRequestWith403()
    {
        using var fomsDb = CreateFomsDb(nameof(Sod_LiquidationsAndCashAdvances_BlocksSubmitterFromEndorsingOrApprovingOwnRequestWith403));

        fomsDb.Liquidations.Add(new Liquidation
        {
            LiquidationId = 801,
            ReferenceNumber = "LIQ-SOD-801",
            SubmittedByName = "EMP-004",
            TotalAmount = 1200m,
            Status = "Pending Endorsement",
            CreatedAt = DateTime.UtcNow
        });

        fomsDb.CashOutflowApprovalRequests.Add(new CashOutflowApprovalRequest
        {
            ApprovalRequestId = 901,
            RequestGuid = "REQ-SOD-901",
            CashOutflowId = "CA-SOD-901",
            TransactionReference = "CA-SOD-901",
            Amount = 3000m,
            Status = "Pending Assistant Review",
            SubmittedBy = "EMP-004",
            SubmittedById = "EMP-004",
            SubmittedAt = DateTime.UtcNow
        });

        fomsDb.CashAdvances.Add(new CashAdvance
        {
            AdvanceId = "CA-SOD-902",
            CourierName = "EMP-001",
            ReleasedBy = "EMP-001",
            AmountGiven = 4000m,
            Status = "Submitted"
        });

        await fomsDb.SaveChangesAsync();

        // 1. AssistantFinanceManagerController.EndorseLiquidation self-endorsement -> 403
        var afmController = new AssistantFinanceManagerController(fomsDb);
        SetUserContext(afmController, "Assistant of Finance Manager", "EMP-004", "EMP-004");
        var endorseRes = await afmController.EndorseLiquidation(801);
        Assert.Equal(403, Assert.IsType<ObjectResult>(endorseRes).StatusCode);

        // 2. LiquidationsController.Act self-approval -> 403
        var liqController = new LiquidationsController(fomsDb);
        SetUserContext(liqController, "Assistant of Finance Manager", "EMP-004", "EMP-004");
        var liqActRes = await liqController.Act(801, "endorse", null);
        Assert.Equal(403, Assert.IsType<ObjectResult>(liqActRes).StatusCode);

        // 3. ApprovalsController.Approve self-approval -> 403
        var approvalsController = new ApprovalsController(fomsDb, NullLogger<ApprovalsController>.Instance);
        SetUserContext(approvalsController, "Assistant of Finance Manager", "EMP-004", "EMP-004");
        var apprRes = await approvalsController.Approve("REQ-SOD-901");
        Assert.Equal(403, Assert.IsType<ObjectResult>(apprRes).StatusCode);

        // 4. CashAdvancesController.Approve self-approval -> 403
        var caController = new CashAdvancesController(fomsDb, NullLogger<CashAdvancesController>.Instance);
        SetUserContext(caController, "Finance Manager", "EMP-001", "EMP-001");
        var caRes = await caController.Approve("CA-SOD-902", null);
        Assert.Equal(403, Assert.IsType<ObjectResult>(caRes).StatusCode);
    }
}
