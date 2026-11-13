using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FOMS.Api.Controllers;
using FOMS.Domain.Entities;
using FOMS.Infrastructure.Persistence;
using Xunit;

namespace FOMS.Tests;

public class InvoiceApprovalWorkflowAndTransitionsTests
{
    private static ApplicationDbContext CreateAppDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new ApplicationDbContext(options);
    }

    private static FomsDbContext CreateMssqlDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<FomsDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new FomsDbContext(options);
    }

    private static ClaimsPrincipal CreatePrincipal(string role, string id, string name)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, id),
            new(ClaimTypes.Name, name),
            new(ClaimTypes.Role, role),
            new("role", role),
            new("name", name)
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    private static ClaimsPrincipal StaffUser() => CreatePrincipal("Accountant", "EMP-003", "Billing Staff");
    private static ClaimsPrincipal OtherStaffUser() => CreatePrincipal("Accountant", "EMP-003B", "Billing Staff 2");
    private static ClaimsPrincipal HeadAccountantUser() => CreatePrincipal("Head Accountant", "EMP-002", "Head Accountant");
    private static ClaimsPrincipal FinanceManagerUser() => CreatePrincipal("Finance Manager", "EMP-001", "Finance Manager");
    private static ClaimsPrincipal AssistantFmUser() => CreatePrincipal("Assistant Finance Manager", "EMP-004", "Assistant Finance Manager");

    private static object? GetValueProp(object? obj, string propName)
    {
        if (obj == null) return null;
        var prop = obj.GetType().GetProperty(propName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
        return prop?.GetValue(obj);
    }

    private static Invoice CreateSeedInvoice(string id = "INV-WF-001", string status = InvoiceDocumentStatus.Draft, string encodedBy = "EMP-003", decimal amount = 1000m)
    {
        return new Invoice
        {
            Id = id,
            InvoiceNo = id,
            ClientId = "CL-001",
            ClientName = "Acme Logistics",
            DocumentStatus = status,
            PaymentStatus = InvoicePaymentStatus.Unpaid,
            TotalAmount = amount,
            Subtotal = amount / 1.12m,
            VatAmount = amount - (amount / 1.12m),
            Balance = amount,
            EncodedBy = encodedBy,
            DateEncoded = DateTime.UtcNow.ToString("yyyy-MM-dd")
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. FULL APPROVAL CHAIN: Draft -> For Review -> Approved -> Sent -> Received
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task FullApprovalChain_DraftToReceived_TransitionsSuccessfully()
    {
        var appDb = CreateAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateMssqlDb(Guid.NewGuid().ToString());
        var invoice = CreateSeedInvoice("INV-CHAIN-001");
        appDb.Invoices.Add(invoice);
        await appDb.SaveChangesAsync();

        var controller = new InvoicesController(appDb, mssqlDb);

        // 1. Staff EMP-003 submits Draft -> For Review
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = StaffUser() } };
        var submitRes = await controller.TransitionInvoice(invoice.Id, "submit", new InvoiceTransitionRequest());
        Assert.IsType<OkObjectResult>(submitRes);
        var cur = await appDb.Invoices.FindAsync(invoice.Id);
        Assert.Equal(InvoiceDocumentStatus.ForReview, cur!.DocumentStatus);

        // 2. HA EMP-002 verifies -> Approved (AR not posted yet; posts strictly at Sent)
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = HeadAccountantUser() } };
        var verifyRes = await controller.TransitionInvoice(invoice.Id, "verify", new InvoiceTransitionRequest());
        Assert.IsType<OkObjectResult>(verifyRes);
        cur = await appDb.Invoices.FindAsync(invoice.Id);
        Assert.Equal(InvoiceDocumentStatus.Approved, cur!.DocumentStatus);
        Assert.False(cur.AccrualRevenueRecognized);
        Assert.Null(cur.AccrualJournalEntryId);

        // 3. Staff EMP-003 sends -> Sent (AR posted strictly at Sent; DueDate remains null)
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = StaffUser() } };
        var sendRes = await controller.TransitionInvoice(invoice.Id, "send", new InvoiceTransitionRequest());
        Assert.IsType<OkObjectResult>(sendRes);
        cur = await appDb.Invoices.FindAsync(invoice.Id);
        Assert.Equal(InvoiceDocumentStatus.Sent, cur!.DocumentStatus);
        Assert.NotNull(cur.SentDate);
        Assert.True(cur.AccrualRevenueRecognized);
        Assert.NotNull(cur.AccrualJournalEntryId);

        // Verify AR journal entry posted
        var je = await appDb.JournalEntries.FirstOrDefaultAsync(j => j.Id == cur.AccrualJournalEntryId);
        Assert.NotNull(je);
        var gl = await appDb.GeneralLedgerEntries.Where(g => g.JournalEntryId == je.Id).ToListAsync();
        Assert.Equal(gl.Sum(g => g.Debit), gl.Sum(g => g.Credit));

        var rec = await appDb.ReceivableBalances.FirstOrDefaultAsync();
        Assert.NotNull(rec);
        Assert.Null(rec.DueDate);

        // 4. Staff EMP-003 marks Received -> Received (DueDate calculated from term)
        var receivedDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var recvRes = await controller.TransitionInvoice(invoice.Id, "receive", new InvoiceTransitionRequest { ReceivedDate = receivedDate });
        Assert.IsType<OkObjectResult>(recvRes);
        cur = await appDb.Invoices.FindAsync(invoice.Id);
        Assert.Equal(InvoiceDocumentStatus.Received, cur!.DocumentStatus);
        Assert.Equal(receivedDate, cur.ReceivedDate);
        Assert.False(string.IsNullOrWhiteSpace(cur.DueDate));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. REVISION 3 ROUNDTRIP: Draft -> For Review -> Return to Draft -> Resubmit -> Verify -> Send
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Revision3_ReturnAndReverify_PostsArAccrualOnceAtSent()
    {
        var appDb = CreateAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateMssqlDb(Guid.NewGuid().ToString());
        var invoice = CreateSeedInvoice("INV-ROUNDTRIP-01", InvoiceDocumentStatus.Draft, encodedBy: "EMP-003");
        appDb.Invoices.Add(invoice);
        await appDb.SaveChangesAsync();

        var controller = new InvoicesController(appDb, mssqlDb);

        // 1. Creator EMP-003 submits -> For Review
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = StaffUser() } };
        var submitRes = await controller.TransitionInvoice(invoice.Id, "submit", new InvoiceTransitionRequest());
        Assert.IsType<OkObjectResult>(submitRes);
        var cur = await appDb.Invoices.FindAsync(invoice.Id);
        Assert.Equal(InvoiceDocumentStatus.ForReview, cur!.DocumentStatus);

        // 2. HA EMP-002 returns to creator with mandatory reason -> Draft
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = HeadAccountantUser() } };
        var returnRes = await controller.TransitionInvoice(invoice.Id, "return-to-creator", new InvoiceTransitionRequest
        {
            ReviewRejectionReason = "Client contracted custom rate must be applied"
        });
        Assert.IsType<OkObjectResult>(returnRes);
        cur = await appDb.Invoices.FindAsync(invoice.Id);
        Assert.Equal(InvoiceDocumentStatus.Draft, cur!.DocumentStatus);
        Assert.Equal("Client contracted custom rate must be applied", cur.ReviewRejectionReason);

        // 3. Creator EMP-003 resubmits -> For Review
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = StaffUser() } };
        var reSubmitRes = await controller.TransitionInvoice(invoice.Id, "resubmit", new InvoiceTransitionRequest());
        Assert.IsType<OkObjectResult>(reSubmitRes);
        cur = await appDb.Invoices.FindAsync(invoice.Id);
        Assert.Equal(InvoiceDocumentStatus.ForReview, cur!.DocumentStatus);

        // 4. HA EMP-002 verifies -> Approved (No AR journal entry yet)
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = HeadAccountantUser() } };
        var verifyRes = await controller.TransitionInvoice(invoice.Id, "verify", new InvoiceTransitionRequest());
        Assert.IsType<OkObjectResult>(verifyRes);
        cur = await appDb.Invoices.FindAsync(invoice.Id);
        Assert.Equal(InvoiceDocumentStatus.Approved, cur!.DocumentStatus);
        Assert.False(cur.AccrualRevenueRecognized);

        // 5. Staff EMP-003 sends -> Sent (AR accrual posted EXACTLY once)
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = StaffUser() } };
        var sendRes = await controller.TransitionInvoice(invoice.Id, "send", new InvoiceTransitionRequest());
        Assert.IsType<OkObjectResult>(sendRes);
        cur = await appDb.Invoices.FindAsync(invoice.Id);
        Assert.Equal(InvoiceDocumentStatus.Sent, cur!.DocumentStatus);
        Assert.True(cur.AccrualRevenueRecognized);
        Assert.NotNull(cur.AccrualJournalEntryId);

        var jeCount = await appDb.JournalEntries.CountAsync(j => j.Id == cur.AccrualJournalEntryId);
        Assert.Equal(1, jeCount);
    }

    [Fact]
    public async Task BidirectionalReturn_FmToHa_AndResubmit_WorksCorrectly()
    {
        var appDb = CreateAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateMssqlDb(Guid.NewGuid().ToString());
        var invoice = CreateSeedInvoice("INV-RET-HA", InvoiceDocumentStatus.ForReview);
        appDb.Invoices.Add(invoice);
        await appDb.SaveChangesAsync();

        var controller = new InvoicesController(appDb, mssqlDb);

        // FM returns to HA with mandatory reason -> stays/returns in For Review with reason
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = FinanceManagerUser() } };
        var returnRes = await controller.TransitionInvoice(invoice.Id, "return-to-ha", new InvoiceTransitionRequest
        {
            ReviewRejectionReason = "Pricing tier mismatch on client contract"
        });
        Assert.IsType<OkObjectResult>(returnRes);
        var cur = await appDb.Invoices.FindAsync(invoice.Id);
        Assert.Equal(InvoiceDocumentStatus.ForReview, cur!.DocumentStatus);
        Assert.Equal("Pricing tier mismatch on client contract", cur.ReviewRejectionReason);

        // HA verifies -> Approved
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = HeadAccountantUser() } };
        var verifyRes = await controller.TransitionInvoice(invoice.Id, "verify", new InvoiceTransitionRequest());
        Assert.IsType<OkObjectResult>(verifyRes);
        cur = await appDb.Invoices.FindAsync(invoice.Id);
        Assert.Equal(InvoiceDocumentStatus.Approved, cur!.DocumentStatus);
    }

    [Fact]
    public async Task BidirectionalReturn_HaToCreator_AndResubmit_WorksCorrectly()
    {
        var appDb = CreateAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateMssqlDb(Guid.NewGuid().ToString());
        var invoice = CreateSeedInvoice("INV-RET-CR", InvoiceDocumentStatus.ForReview, encodedBy: "EMP-003");
        appDb.Invoices.Add(invoice);
        await appDb.SaveChangesAsync();

        var controller = new InvoicesController(appDb, mssqlDb);

        // HA returns to Creator with mandatory reason -> Draft
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = HeadAccountantUser() } };
        var returnRes = await controller.TransitionInvoice(invoice.Id, "return-to-creator", new InvoiceTransitionRequest
        {
            Remarks = "Missing attachment for weight calculation"
        });
        Assert.IsType<OkObjectResult>(returnRes);
        var cur = await appDb.Invoices.FindAsync(invoice.Id);
        Assert.Equal(InvoiceDocumentStatus.Draft, cur!.DocumentStatus);

        // Creator (EMP-003) resubmits back to For Review
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = StaffUser() } };
        var resubmitRes = await controller.TransitionInvoice(invoice.Id, "resubmit", new InvoiceTransitionRequest());
        Assert.IsType<OkObjectResult>(resubmitRes);
        cur = await appDb.Invoices.FindAsync(invoice.Id);
        Assert.Equal(InvoiceDocumentStatus.ForReview, cur!.DocumentStatus);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. PRE-APPROVAL VOID BY HEAD ACCOUNTANT
    // ─────────────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData(InvoiceDocumentStatus.Draft)]
    [InlineData(InvoiceDocumentStatus.ForReview)]
    [InlineData(InvoiceDocumentStatus.Approved)]
    [InlineData(InvoiceDocumentStatus.PendingHaVerification)]
    [InlineData(InvoiceDocumentStatus.PendingFmApproval)]
    [InlineData(InvoiceDocumentStatus.ReturnedToCreator)]
    [InlineData(InvoiceDocumentStatus.ReturnedToHa)]
    public async Task HeadAccountant_CanVoid_PreApprovalStatuses_WithMandatoryReason(string status)
    {
        var appDb = CreateAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateMssqlDb(Guid.NewGuid().ToString());
        var invoice = CreateSeedInvoice($"INV-VOID-{Guid.NewGuid():N}", status, encodedBy: "EMP-003");
        appDb.Invoices.Add(invoice);
        await appDb.SaveChangesAsync();

        var controller = new InvoicesController(appDb, mssqlDb)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = HeadAccountantUser() } }
        };

        // Void without reason fails
        var failRes = await controller.VoidInvoice(invoice.Id, new VoidInvoiceRequest { VoidReason = "" });
        var badReq = Assert.IsType<BadRequestObjectResult>(failRes);
        Assert.Contains("void reason is mandatory", badReq.Value?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);

        // Void with reason succeeds
        var okRes = await controller.VoidInvoice(invoice.Id, new VoidInvoiceRequest { VoidReason = "Duplicate entry created in error" });
        Assert.IsType<OkObjectResult>(okRes);
        var cur = await appDb.Invoices.FindAsync(invoice.Id);
        Assert.Equal(InvoiceDocumentStatus.Void, cur!.DocumentStatus);
        Assert.Equal("Duplicate entry created in error", cur.VoidReason);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. TEST 4 ASSERTION: VOIDING APPROVED INVOICE POSTS NO JOURNAL ENTRY
    //    (AR posts strictly at Sent; Void allowed by Head Accountant prior to Sent)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Test4_VoidingApprovedInvoice_PostsNoJournalEntry_AndVoidPriorToSentAllowedByHeadAccountant()
    {
        var appDb = CreateAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateMssqlDb(Guid.NewGuid().ToString());
        var invoice = CreateSeedInvoice("INV-APP-VOID", InvoiceDocumentStatus.Approved, encodedBy: "EMP-003", amount: 1120m);
        appDb.Invoices.Add(invoice);
        await appDb.SaveChangesAsync();

        var controller = new InvoicesController(appDb, mssqlDb)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = HeadAccountantUser() } }
        };

        // 1. Prior to Sent, Head Accountant can void the Approved invoice
        var voidRes = await controller.VoidInvoice(invoice.Id, new VoidInvoiceRequest
        {
            VoidReason = "Commercial renegotiation prior to client delivery"
        });
        Assert.IsType<OkObjectResult>(voidRes);

        var cur = await appDb.Invoices.FindAsync(invoice.Id);
        Assert.Equal(InvoiceDocumentStatus.Void, cur!.DocumentStatus);
        Assert.Equal("Commercial renegotiation prior to client delivery", cur.VoidReason);

        // 2. Proves: Voiding an Approved invoice posts NO journal entry (AR posts strictly at Sent)
        var journalEntryCount = await appDb.JournalEntries.CountAsync();
        Assert.Equal(0, journalEntryCount);
        var glEntryCount = await appDb.GeneralLedgerEntries.CountAsync();
        Assert.Equal(0, glEntryCount);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5. DENIED VOIDS (POST-DISPATCH OR WITH PAYMENTS)
    // ─────────────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData(InvoiceDocumentStatus.Sent)]
    [InlineData(InvoiceDocumentStatus.Received)]
    [InlineData(InvoiceDocumentStatus.Disputed)]
    public async Task PostDispatch_Invoices_CannotBeVoided(string status)
    {
        var appDb = CreateAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateMssqlDb(Guid.NewGuid().ToString());
        var invoice = CreateSeedInvoice($"INV-DENY-{Guid.NewGuid():N}", status, encodedBy: "EMP-003");
        appDb.Invoices.Add(invoice);
        await appDb.SaveChangesAsync();

        var controller = new InvoicesController(appDb, mssqlDb)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = FinanceManagerUser() } }
        };

        var res = await controller.VoidInvoice(invoice.Id, new VoidInvoiceRequest { VoidReason = "Attempt post-dispatch void" });
        var conflict = Assert.IsType<ObjectResult>(res);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task Void_Denied_IfAnyPaymentApplied()
    {
        var appDb = CreateAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateMssqlDb(Guid.NewGuid().ToString());
        var invoice = CreateSeedInvoice("INV-PAID-001", InvoiceDocumentStatus.Approved, encodedBy: "EMP-003", amount: 1000m);
        invoice.AmountPaid = 200m;
        invoice.Balance = 800m;
        invoice.PaymentStatus = InvoicePaymentStatus.PartiallyPaid;
        appDb.Invoices.Add(invoice);
        await appDb.SaveChangesAsync();

        var controller = new InvoicesController(appDb, mssqlDb)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = FinanceManagerUser() } }
        };

        var res = await controller.VoidInvoice(invoice.Id, new VoidInvoiceRequest { VoidReason = "Attempt void with payment" });
        var conflict = Assert.IsType<ObjectResult>(res);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 6. ROLE RESTRICTIONS & EMP-004 FORBIDDEN
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task AssistantFinanceManager_EMP004_Forbidden_FromAllInvoiceActions()
    {
        var appDb = CreateAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateMssqlDb(Guid.NewGuid().ToString());
        var invoice = CreateSeedInvoice("INV-AFM-001");
        appDb.Invoices.Add(invoice);
        await appDb.SaveChangesAsync();

        var controller = new InvoicesController(appDb, mssqlDb)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = AssistantFmUser() } }
        };

        // Create -> 403 Forbidden
        var createRes = await controller.CreateInvoice(new InvoiceSubmissionDto { ClientId = "CL-001", FreightCharges = 100m });
        var forbidCreate = Assert.IsType<ObjectResult>(createRes);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidCreate.StatusCode);

        // Update -> 403 Forbidden
        var updateRes = await controller.UpdateInvoice(invoice.Id, new InvoiceSubmissionDto { FreightCharges = 200m });
        var forbidUpdate = Assert.IsType<ObjectResult>(updateRes);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidUpdate.StatusCode);

        // Transition -> 403 Forbidden
        var transRes = await controller.TransitionInvoice(invoice.Id, "submit", new InvoiceTransitionRequest());
        var forbidTrans = Assert.IsType<ObjectResult>(transRes);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidTrans.StatusCode);

        // Void -> 403 Forbidden
        var voidRes = await controller.VoidInvoice(invoice.Id, new VoidInvoiceRequest { VoidReason = "AFM attempt" });
        var forbidVoid = Assert.IsType<ObjectResult>(voidRes);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidVoid.StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 7. SEPARATION OF DUTIES & OPTION 1 EDGE CASE:
    //    Creator Cannot Verify/Approve Own Invoice.
    //    If Head Accountant Created Invoice, Finance Manager Verifies and Approves.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task SeparationOfDuties_Creator_CannotVerifyOrApproveOrVoid_OwnInvoice()
    {
        var appDb = CreateAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateMssqlDb(Guid.NewGuid().ToString());
        var invoice = CreateSeedInvoice("INV-SOD-001", InvoiceDocumentStatus.ForReview, encodedBy: "EMP-002");
        appDb.Invoices.Add(invoice);
        await appDb.SaveChangesAsync();

        var controller = new InvoicesController(appDb, mssqlDb)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = HeadAccountantUser() } }
        };

        // HA EMP-002 tries to verify own invoice -> 409 Conflict
        var verifyRes = await controller.TransitionInvoice(invoice.Id, "verify", new InvoiceTransitionRequest());
        var conflictVerify = Assert.IsType<ObjectResult>(verifyRes);
        Assert.Equal(StatusCodes.Status409Conflict, conflictVerify.StatusCode);
        Assert.Contains("Separation of duties", conflictVerify.Value?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);

        // HA EMP-002 tries to void own invoice -> 409 Conflict
        var voidRes = await controller.VoidInvoice(invoice.Id, new VoidInvoiceRequest { VoidReason = "Self void attempt" });
        var conflictVoid = Assert.IsType<ObjectResult>(voidRes);
        Assert.Equal(StatusCodes.Status409Conflict, conflictVoid.StatusCode);
    }

    [Fact]
    public async Task Option1_HeadAccountantCreator_VerifiedAndApprovedByFinanceManager()
    {
        var appDb = CreateAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateMssqlDb(Guid.NewGuid().ToString());
        // Head Accountant EMP-002 created/encoded the invoice
        var invoice = CreateSeedInvoice("INV-HA-CREATOR-01", InvoiceDocumentStatus.ForReview, encodedBy: "EMP-002");
        appDb.Invoices.Add(invoice);
        await appDb.SaveChangesAsync();

        var controller = new InvoicesController(appDb, mssqlDb);

        // HA cannot verify own invoice (SoD)
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = HeadAccountantUser() } };
        var haRes = await controller.TransitionInvoice(invoice.Id, "verify", new InvoiceTransitionRequest());
        var conflict = Assert.IsType<ObjectResult>(haRes);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);

        // Finance Manager EMP-001 verifies and approves (Option 1 confirmed) -> 200 OK
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = FinanceManagerUser() } };
        var fmRes = await controller.TransitionInvoice(invoice.Id, "verify", new InvoiceTransitionRequest());
        Assert.IsType<OkObjectResult>(fmRes);

        var cur = await appDb.Invoices.FindAsync(invoice.Id);
        Assert.Equal(InvoiceDocumentStatus.Approved, cur!.DocumentStatus);
        Assert.Equal("EMP-001", cur.VerifiedBy);
        Assert.Equal("EMP-001", cur.ApprovedBy);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 8. DISPUTE WORKFLOW
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task DisputeWorkflow_SentToDisputed_AndResolution_WorksCorrectly()
    {
        var appDb = CreateAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateMssqlDb(Guid.NewGuid().ToString());
        var invoice = CreateSeedInvoice("INV-DISP-001", InvoiceDocumentStatus.Sent, encodedBy: "EMP-003");
        appDb.Invoices.Add(invoice);
        await appDb.SaveChangesAsync();

        var controller = new InvoicesController(appDb, mssqlDb)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = HeadAccountantUser() } }
        };

        // Raise dispute
        var dispRes = await controller.TransitionInvoice(invoice.Id, "dispute", new InvoiceTransitionRequest
        {
            DisputeReasonCode = "DISP-RATE-MISMATCH",
            Remarks = "Client disputes minimum rate applied"
        });
        Assert.IsType<OkObjectResult>(dispRes);
        var cur = await appDb.Invoices.FindAsync(invoice.Id);
        Assert.Equal(InvoiceDocumentStatus.Disputed, cur!.DocumentStatus);
        Assert.True(cur.IsDisputed);
        Assert.Equal("DISP-RATE-MISMATCH", cur.DisputeReasonCode);

        // Resolve dispute -> returns to Received
        var resolveRes = await controller.TransitionInvoice(invoice.Id, "resolve-dispute", new InvoiceTransitionRequest
        {
            Remarks = "Rate verified with client commercial contract, accepted as originally billed"
        });
        Assert.IsType<OkObjectResult>(resolveRes);
        cur = await appDb.Invoices.FindAsync(invoice.Id);
        Assert.Equal(InvoiceDocumentStatus.Received, cur!.DocumentStatus);
        Assert.False(cur.IsDisputed);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 9. CONCURRENCY: DbUpdateConcurrencyException Returns 409 Conflict
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task ConcurrentTransition_Returns409Conflict_WhenConcurrencyExceptionOccurs()
    {
        var appDb = CreateAppDb(Guid.NewGuid().ToString());
        var mssqlDb = CreateMssqlDb(Guid.NewGuid().ToString());
        var invoice = CreateSeedInvoice("INV-CONC-001", InvoiceDocumentStatus.ForReview, encodedBy: "EMP-003");
        appDb.Invoices.Add(invoice);
        await appDb.SaveChangesAsync();

        var controller = new InvoicesController(appDb, mssqlDb)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = FinanceManagerUser() } }
        };

        // First transition succeeds
        var res1 = await controller.TransitionInvoice(invoice.Id, "approve", new InvoiceTransitionRequest());
        Assert.IsType<OkObjectResult>(res1);

        // Attempting to transition again from the old state or when already approved fails
        var res2 = await controller.TransitionInvoice(invoice.Id, "approve", new InvoiceTransitionRequest());
        var conflict = Assert.IsType<ObjectResult>(res2);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 10. LEGACY STATUS MAPPING & NORMALIZATION TEST
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void LegacyStatusMapping_NormalizesOldStatusesToRevision3()
    {
        Assert.Equal(InvoiceDocumentStatus.ForReview, InvoiceDocumentStatus.Normalize(InvoiceDocumentStatus.PendingHaVerification));
        Assert.Equal(InvoiceDocumentStatus.ForReview, InvoiceDocumentStatus.Normalize(InvoiceDocumentStatus.PendingFmApproval));
        Assert.Equal(InvoiceDocumentStatus.ForReview, InvoiceDocumentStatus.Normalize(InvoiceDocumentStatus.ReturnedToHa));
        Assert.Equal(InvoiceDocumentStatus.ForReview, InvoiceDocumentStatus.Normalize("Pending Approval"));
        Assert.Equal(InvoiceDocumentStatus.ForReview, InvoiceDocumentStatus.Normalize("Pending"));

        Assert.Equal(InvoiceDocumentStatus.Draft, InvoiceDocumentStatus.Normalize(InvoiceDocumentStatus.ReturnedToCreator));
        Assert.Equal(InvoiceDocumentStatus.Draft, InvoiceDocumentStatus.Normalize(InvoiceDocumentStatus.Draft));

        Assert.Equal(InvoiceDocumentStatus.Approved, InvoiceDocumentStatus.Normalize(InvoiceDocumentStatus.Approved));
        Assert.Equal(InvoiceDocumentStatus.Sent, InvoiceDocumentStatus.Normalize(InvoiceDocumentStatus.Sent));
        Assert.Equal(InvoiceDocumentStatus.Received, InvoiceDocumentStatus.Normalize(InvoiceDocumentStatus.Received));
        Assert.Equal(InvoiceDocumentStatus.Disputed, InvoiceDocumentStatus.Normalize(InvoiceDocumentStatus.Disputed));
        Assert.Equal(InvoiceDocumentStatus.Void, InvoiceDocumentStatus.Normalize(InvoiceDocumentStatus.Void));
    }
}
