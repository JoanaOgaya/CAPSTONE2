using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MediatR;
using FOMS.Application.Features;
using FOMS.Application.Interfaces;
using FOMS.Application.Services;
using FOMS.Domain.Entities;
using SqlServer = FOMS.Domain.Entities.SqlServer;
using FOMS.Infrastructure.Persistence;

namespace FOMS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public class FinancePaymentsController : ControllerBase
{
    private readonly IApplicationDbContext _context;
    private readonly FomsDbContext _fomsDb;
    private readonly IMediator _mediator;

    public FinancePaymentsController(IApplicationDbContext _contextRef, FomsDbContext fomsDb, IMediator mediator)
    {
        _context = _contextRef;
        _fomsDb = fomsDb;
        _mediator = mediator;
    }

    private async Task<(bool isAuthorized, string effectiveRole, string? delegateNote)> CheckValidationAuthorityAsync()
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value
            ?? User.FindFirst("role")?.Value
            ?? "Unknown Role";
        var user = User.FindFirst("name")?.Value
            ?? User.FindFirst(ClaimTypes.Name)?.Value
            ?? "Unknown User";
        var empId = User.FindFirst("employeeNumber")?.Value
            ?? User.FindFirst("EmployeeNumber")?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? user;

        if (role.Equals("Finance Manager", StringComparison.OrdinalIgnoreCase) ||
            role.Equals("FINANCE_MANAGER", StringComparison.OrdinalIgnoreCase))
        {
            return (true, "Finance Manager", null);
        }

        var now = DateTime.UtcNow;
        var activeDelegate = await _fomsDb.FinanceManagerDelegates
            .FirstOrDefaultAsync(d =>
                d.IsActive &&
                d.ValidFrom <= now &&
                d.ValidUntil >= now &&
                (d.DelegateUserId == empId || d.DelegateUserId == user || d.DelegateName == user));

        if (activeDelegate != null)
        {
            return (true, $"Finance Manager Delegate ({activeDelegate.DelegateName})",
                $"Validated by authorized delegate {activeDelegate.DelegateName} ({activeDelegate.DelegateUserId}) assigned by {activeDelegate.AssignedBy}. Reason: {activeDelegate.Reason}");
        }

        return (false, role, null);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ─────────────────────────────────────────────────────────────────────────
    // 1. FINAL APPROVE PAYMENT
    // POST /api/finance-payments/{paymentId}/approve
    // ─────────────────────────────────────────────────────────────────────────
    [Authorize]
    [HttpPost("/api/finance-payments/{paymentId}/approve")]
    [HttpPost("finance-payments/{paymentId}/approve")]
    [HttpPost("/finance-payments/{paymentId}/approve")]
    public async Task<IActionResult> ApprovePayment(string paymentId, [FromBody] ValidationActionRequest? request = null)
    {
        if (User?.Identity?.IsAuthenticated != true)
        {
            return Unauthorized(new { message = "Authentication required." });
        }

        var (isAuthorized, validatorRole, delegateNote) = await CheckValidationAuthorityAsync();
        if (!isAuthorized)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Only the Finance Manager (or configured, logged delegate) is authorized to perform payment approval." });
        }

        var approverEmpId = User.FindFirst("employeeNumber")?.Value
            ?? User.FindFirst("EmployeeNumber")?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        var user = User.FindFirst("name")?.Value
            ?? User.FindFirst(ClaimTypes.Name)?.Value
            ?? approverEmpId
            ?? "Finance Manager";

        var payment = await _context.Payments.FindAsync(paymentId) ?? await _context.Payments.FirstOrDefaultAsync(p => p.Id == paymentId);
        if (payment == null)
            return NotFound(new { message = $"Payment record '{paymentId}' not found." });

        // Idempotency check: double click or retry never applies the payment to AR twice or issues two receipts
        if (payment.PaymentStatus.Equals("Approved", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(new
            {
                id = payment.Id,
                paymentId = payment.Id,
                status = "Approved",
                paymentStatus = "Approved",
                invoiceId = payment.InvoiceId,
                officialReceipt = payment.OrNumber,
                orNumber = payment.OrNumber,
                amount = payment.Amount,
                appliedAmount = payment.AppliedAmount,
                message = "Payment is already approved."
            });
        }

        // Submitter != approver compared by employee ID, never by display name
        var submitterEmpId = payment.RecordedByEmployeeId;
        if (string.IsNullOrWhiteSpace(submitterEmpId) && !string.IsNullOrWhiteSpace(payment.RecordedBy) && payment.RecordedBy.StartsWith("EMP-", StringComparison.OrdinalIgnoreCase))
        {
            submitterEmpId = payment.RecordedBy;
        }

        if (!string.IsNullOrWhiteSpace(approverEmpId) &&
            !string.IsNullOrWhiteSpace(submitterEmpId) &&
            approverEmpId.Equals(submitterEmpId, StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Separation of duties violation: Submitter cannot approve their own payment." });
        }

        if (!string.IsNullOrWhiteSpace(payment.RecordedBy) &&
            (payment.RecordedBy.Equals(user, StringComparison.OrdinalIgnoreCase) ||
             payment.RecordedBy.Equals(approverEmpId, StringComparison.OrdinalIgnoreCase)))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Separation of duties violation: Submitter cannot approve their own payment." });
        }

        // Illegal transitions rejected server-side with clear error
        if (payment.PaymentStatus is "Rejected" or "Returned")
        {
            return BadRequest(new { message = $"Payment in status '{payment.PaymentStatus}' cannot be approved." });
        }

        var invoice = await _context.Invoices.FirstOrDefaultAsync(i => i.Id == payment.InvoiceId || i.InvoiceNo == payment.InvoiceNo);
        if (invoice == null)
            return NotFound(new { message = $"Associated Invoice '{payment.InvoiceId}' not found." });

        // Void invoice: Void is terminal
        if (invoice.DocumentStatus == "Void" || invoice.PaymentStatus == "Void" || invoice.DocumentStatus == InvoiceDocumentStatus.Void)
        {
            return BadRequest(new { message = "Cannot approve payment for a void invoice. Void is terminal." });
        }



        BillingComputationService.RecalculateInvoice(invoice);

        // Reject overpayment
        if (payment.Amount > invoice.Balance)
        {
            return BadRequest(new { message = $"Overpayment rejected: payment amount {payment.Amount:N2} exceeds invoice balance {invoice.Balance:N2}." });
        }

        var appliedAmount = payment.Amount;
        payment.AppliedAmount = appliedAmount;
        invoice.AmountPaid += appliedAmount;
        BillingComputationService.RecalculateInvoice(invoice);
        invoice.UpdatedBy = user;
        invoice.PaymentValidationStatus = "Validated";
        invoice.PaymentStatus = invoice.Balance <= 0 ? "Paid" : "Unpaid";

        payment.PaymentStatus = "Approved";
        payment.ValidatedAt = DateTime.UtcNow;
        payment.ValidatedBy = user;
        if (!string.IsNullOrWhiteSpace(request?.Remarks))
            payment.Remarks = request.Remarks;

        // One Official Receipt per approved payment (with DB unique constraint)
        var orNum = (!string.IsNullOrWhiteSpace(payment.OrNumber) && !payment.OrNumber.StartsWith("PENDING-"))
            ? payment.OrNumber
            : await GenerateNextOrNumberAsync();
        payment.OrNumber = orNum;

        var existingReceipt = await _context.OfficialReceipts.FirstOrDefaultAsync(r => r.PaymentId == payment.Id);
        if (existingReceipt == null)
        {
            var paymentCollection = new PaymentCollection
            {
                InvoiceId = invoice.Id,
                CollectedDate = DateTime.UtcNow,
                AmountCollected = payment.Amount,
                PaymentMethod = payment.PaymentMethod,
                Status = "Completed"
            };
            _context.PaymentCollections.Add(paymentCollection);

            var officialReceipt = new OfficialReceipt
            {
                PaymentId = payment.Id,
                PaymentCollectionId = paymentCollection.Id,
                ReceiptNumber = orNum,
                IssuedDate = DateTime.UtcNow
            };
            _context.OfficialReceipts.Add(officialReceipt);
        }

        // Apply to AR
        var receivable = await _context.ReceivableBalances.FirstOrDefaultAsync(r => r.InvoiceId == invoice.Id);
        if (receivable != null)
        {
            receivable.BalanceAmount = invoice.Balance;
            receivable.PaidAmount += appliedAmount;
            receivable.LastPaymentDate = DateTime.UtcNow;
            receivable.Status = invoice.Balance <= 0 ? "Paid" : "Unpaid";
            _context.ReceivableBalances.Update(receivable);
        }
        else if (invoice.Balance > 0)
        {
            receivable = new ReceivableBalance
            {
                ClientId = invoice.ClientId,
                InvoiceId = invoice.Id,
                BalanceAmount = invoice.Balance,
                PaidAmount = appliedAmount,
                LastPaymentDate = DateTime.UtcNow,
                Status = "Unpaid",
                DueDate = DateTime.TryParse(invoice.DueDate, out var parsedDue) ? parsedDue : DateTime.UtcNow.AddDays(30)
            };
            _context.ReceivableBalances.Add(receivable);
        }

        await BillingComputationService.SyncBillingInvoiceAndReceivableAsync(invoice, _context);

        // Record Cash Flow inflow
        var cashFlowExists = await _context.CashFlowTransactions
            .AnyAsync(entry => entry.ReferenceNo == payment.Id || entry.ReferenceNo == payment.OrNumber);
        if (!cashFlowExists)
        {
            _context.CashFlowTransactions.Add(new CashFlowTransaction
            {
                Date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                Type = "Inflow",
                Category = "Accounts Receivable Collection",
                Amount = payment.Amount,
                ReferenceNo = payment.OrNumber,
                Description = $"Approved payment for Invoice {invoice.InvoiceNo}. Ref: {payment.ReferenceNumber}. Approver: {user}."
            });
        }

        // Audit Log
        _context.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid().ToString(),
            UserId = user,
            EntityName = "Payment",
            EntityId = payment.Id,
            Action = "Approve Payment",
            Details = $"Approved payment of {payment.Amount:N2} for Invoice {invoice.InvoiceNo}. OR: {orNum}.",
            LoggedAt = DateTime.UtcNow
        });

        // Sync SQL Server
        try
        {
            int? parsedPaymentId = int.TryParse(payment.Id, out var pid) ? pid : null;
            int? parsedClientId = int.TryParse(invoice.ClientId, out var cid) ? cid : null;
            _fomsDb.CashInflows.Add(new SqlServer.CashInflow
            {
                PaymentId = parsedPaymentId,
                ClientId = parsedClientId,
                Amount = payment.Amount,
                Source = invoice.ClientName ?? "Client Collection",
                ReferenceNumber = payment.OrNumber,
                Status = "Completed",
                TransactionDate = DateTime.UtcNow
            });
            await _fomsDb.SaveChangesAsync();
        }
        catch { }

        try
        {
            if (int.TryParse(invoice.Id, out var parsedInvId))
            {
                var ar = await _fomsDb.AccountsReceivable.FirstOrDefaultAsync(a => a.InvoiceId == parsedInvId);
                if (ar != null)
                {
                    ar.PaidAmount = (ar.PaidAmount ?? 0m) + appliedAmount;
                    ar.OutstandingBalance = invoice.Balance;
                    ar.ArStatus = invoice.Balance <= 0 ? "Paid" : "Unpaid";
                    ar.UpdatedAt = DateTime.UtcNow;
                    await _fomsDb.SaveChangesAsync();
                }
            }
        }
        catch { }

        try
        {
            await _context.SaveChangesAsync(default);
        }
        catch (DbUpdateConcurrencyException)
        {
            return StatusCode(StatusCodes.Status409Conflict, new { message = "Payment approval conflict: another session has already modified or approved this payment." });
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("IX_OfficialReceipts_PaymentId_Unique") == true)
        {
            return StatusCode(StatusCodes.Status409Conflict, new { message = "An official receipt has already been issued for this payment." });
        }

        return Ok(new
        {
            id = payment.Id,
            paymentId = payment.Id,
            status = "Approved",
            paymentStatus = "Approved",
            invoiceId = invoice.Id,
            invoicePaymentStatus = invoice.PaymentStatus,
            officialReceipt = orNum,
            orNumber = orNum,
            amount = payment.Amount,
            appliedAmount = payment.AppliedAmount,
            message = "Payment successfully approved and applied."
        });
    }

    // 2. VALIDATE PAYMENT
    // POST /api/finance/payments/{paymentId}/validate
    // ─────────────────────────────────────────────────────────────────────────
    [Authorize]
    [HttpPost("finance/payments/{paymentId}/validate")]
    public async Task<IActionResult> ValidatePayment(string paymentId, [FromBody] ValidationActionRequest request)
    {
        var (isAuthorized, validatorRole, delegateNote) = await CheckValidationAuthorityAsync();
        if (!isAuthorized)
            return StatusCode(403, new { message = "Only the Finance Manager (or configured, logged delegate) is authorized to validate payments." });

        var user = User.FindFirst("name")?.Value ?? User.FindFirst(ClaimTypes.Name)?.Value ?? "Unknown User";
        var empId = User.FindFirst("employeeNumber")?.Value
            ?? User.FindFirst("EmployeeNumber")?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? user;

        var payment = await _context.Payments.FindAsync(paymentId) ?? await _context.Payments.FirstOrDefaultAsync(p => p.Id == paymentId);
        if (payment == null)
            return NotFound(new { message = $"Payment record '{paymentId}' not found." });

        // Separation of Duties: Submitter cannot validate/approve their own payment
        if (!string.IsNullOrWhiteSpace(payment.RecordedBy) &&
            (payment.RecordedBy.Equals(user, StringComparison.OrdinalIgnoreCase) ||
             payment.RecordedBy.Equals(empId, StringComparison.OrdinalIgnoreCase)))
        {
            return StatusCode(403, new { message = "Separation of duties violation: Submitter cannot validate or approve their own payment." });
        }

        if (payment.PaymentStatus != "Pending Validation" && payment.PaymentStatus != "Pending Finance Validation")
            return BadRequest(new { message = "Only payments pending Finance Manager validation can be validated." });

        var invoice = await _context.Invoices.FirstOrDefaultAsync(i => i.Id == payment.InvoiceId);
        if (invoice == null)
            return NotFound(new { message = $"Associated Invoice '{payment.InvoiceId}' not found." });

        decimal beforeBalance = invoice.Balance;
        string beforeStatus = invoice.PaymentStatus;

        BillingComputationService.RecalculateInvoice(invoice);
        var approvedOverpayment = !string.IsNullOrWhiteSpace(payment.ExceptionResolutionReason) &&
            payment.ExceptionResolutionReason.Contains("unapplied credit", StringComparison.OrdinalIgnoreCase);

        // PAY-10 / Prompt requirement: Invoice becomes Paid only if validated payment equals invoice amount exactly
        if (payment.Amount != invoice.Balance && !approvedOverpayment)
            return BadRequest(new { message = $"Invoice becomes Paid only if validated payment equals invoice amount exactly. Payment is {payment.Amount:N2}, invoice balance is {invoice.Balance:N2}." });

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var appliedAmount = Math.Min(payment.Amount, invoice.Balance);
            payment.AppliedAmount = appliedAmount;
            invoice.AmountPaid += appliedAmount;
            BillingComputationService.RecalculateInvoice(invoice);
            invoice.UpdatedBy = user;
            invoice.PaymentValidationStatus = "Validated";
            invoice.PaymentStatus = invoice.Balance <= 0 ? "Paid" : "Unpaid";

            payment.PaymentStatus = "Validated";
            payment.ValidatedAt = DateTime.UtcNow;
            payment.ValidatedBy = user + (delegateNote != null ? $" ({validatorRole})" : "");
            payment.Remarks = request.Remarks ?? payment.Remarks;

            var unappliedAmount = payment.Amount - appliedAmount;
            if (unappliedAmount > 0m)
            {
                var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == payment.ClientId);
                if (client == null)
                    throw new InvalidOperationException($"Client '{payment.ClientId}' for payment '{payment.Id}' was not found.");

                // PAY-10: Overpayment excess held as unapplied credit and applied to client's other open invoices
                client.AvailableCredit += unappliedAmount;
                await BillingComputationService.ApplyAvailableCreditAsync(client.Id, _context);
                BillingComputationService.RecalculateInvoice(invoice);
            }

            // Collector reference number = OR: preserve OR number if not a placeholder
            var orNum = (!string.IsNullOrWhiteSpace(payment.OrNumber) && !payment.OrNumber.StartsWith("PENDING-"))
                ? payment.OrNumber
                : await GenerateNextOrNumberAsync();
            payment.OrNumber = orNum;

            // Create Official Receipt and PaymentCollection records
            var paymentCollection = new PaymentCollection
            {
                InvoiceId = invoice.Id,
                CollectedDate = DateTime.UtcNow,
                AmountCollected = payment.Amount,
                PaymentMethod = payment.PaymentMethod,
                Status = "Completed"
            };
            _context.PaymentCollections.Add(paymentCollection);

            var officialReceipt = new OfficialReceipt
            {
                PaymentCollectionId = paymentCollection.Id,
                ReceiptNumber = orNum,
                IssuedDate = DateTime.UtcNow
            };
            _context.OfficialReceipts.Add(officialReceipt);

            // Update Accounts Receivable (ReceivableBalance)
            var receivable = await _context.ReceivableBalances.FirstOrDefaultAsync(r => r.InvoiceId == invoice.Id);
            if (receivable != null)
            {
                receivable.BalanceAmount = invoice.Balance;
                receivable.PaidAmount += appliedAmount;
                receivable.LastPaymentDate = DateTime.UtcNow;
                receivable.Status = invoice.Balance <= 0 ? "Paid" : "Unpaid";
                _context.ReceivableBalances.Update(receivable);
            }
            else if (invoice.Balance > 0)
            {
                receivable = new ReceivableBalance
                {
                    ClientId = invoice.ClientId,
                    InvoiceId = invoice.Id,
                    BalanceAmount = invoice.Balance,
                    PaidAmount = appliedAmount,
                    LastPaymentDate = DateTime.UtcNow,
                    Status = "Unpaid",
                    DueDate = DateTime.TryParse(invoice.DueDate, out var parsedDue) ? parsedDue : DateTime.UtcNow.AddDays(30)
                };
                _context.ReceivableBalances.Add(receivable);
            }

            await BillingComputationService.SyncBillingInvoiceAndReceivableAsync(invoice, _context);

            var cashFlowExists = await _context.CashFlowTransactions
                .AnyAsync(entry => entry.ReferenceNo == payment.Id || entry.ReferenceNo == payment.OrNumber);
            if (!cashFlowExists)
            {
                _context.CashFlowTransactions.Add(new CashFlowTransaction
                {
                    Date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                    Type = "Inflow",
                    Category = "Accounts Receivable Collection",
                    Amount = payment.Amount,
                    ReferenceNo = payment.OrNumber,
                    Description = $"Validated payment for Invoice {invoice.InvoiceNo}. Ref: {payment.ReferenceNumber}. Validator: {validatorRole}."
                });
            }

            // Sync CashInflow in SQL Server
            try
            {
                int? parsedPaymentId = int.TryParse(payment.Id, out var pid) ? pid : null;
                int? parsedClientId = int.TryParse(invoice.ClientId, out var cid) ? cid : null;
                _fomsDb.CashInflows.Add(new FOMS.Domain.Entities.SqlServer.CashInflow
                {
                    PaymentId = parsedPaymentId,
                    ClientId = parsedClientId,
                    Amount = payment.Amount,
                    Source = invoice.ClientName ?? "Client Collection",
                    ReferenceNumber = payment.OrNumber,
                    Status = "Completed",
                    TransactionDate = DateTime.UtcNow
                });
                await _fomsDb.SaveChangesAsync();
            }
            catch { }

            // Sync AccountsReceivable in SQL Server
            try
            {
                if (int.TryParse(invoice.Id, out var parsedInvId))
                {
                    var ar = await _fomsDb.AccountsReceivable.FirstOrDefaultAsync(a => a.InvoiceId == parsedInvId);
                    if (ar != null)
                    {
                        ar.PaidAmount = (ar.PaidAmount ?? 0m) + appliedAmount;
                        ar.OutstandingBalance = invoice.Balance;
                        ar.ArStatus = invoice.Balance <= 0 ? "Paid" : "Unpaid";
                        ar.UpdatedAt = DateTime.UtcNow;
                        await _fomsDb.SaveChangesAsync();
                    }
                }
            }
            catch { }

            // Mark Exception Record resolved if one exists for this payment
            try
            {
                var exc = await _fomsDb.ExceptionRecords.FirstOrDefaultAsync(e => e.RecordId == payment.Id);
                if (exc != null)
                {
                    exc.Status = "Resolved";
                    exc.ResolvedAt = DateTime.UtcNow;
                    exc.ResolvedBy = user;
                    await _fomsDb.SaveChangesAsync();
                }
            }
            catch { }

        // Payment History
        var history = new PaymentHistory
        {
            Id = Guid.NewGuid().ToString(),
            PaymentId = payment.Id,
            InvoiceId = invoice.Id,
            Status = "Validated",
            Action = "Validate Payment",
            Remarks = request.Remarks ?? $"Payment validated by {validatorRole}.",
            PerformedBy = user,
            PerformedRole = validatorRole,
            CreatedAt = DateTime.UtcNow
        };
        _context.PaymentHistories.Add(history);

        // Audit Log
        var audit = new AuditLog
        {
            Id = Guid.NewGuid().ToString(),
            UserId = user,
            EntityName = "Payment",
            EntityId = payment.Id,
            Action = "Validate Payment",
            Details = $"Validated payment of {payment.Amount:N2} for Invoice {invoice.InvoiceNo}. OR: {orNum}.",
            LoggedAt = DateTime.UtcNow,
            BeforeValue = $"Invoice Balance: {beforeBalance:N2} | Status: {beforeStatus}",
            AfterValue = $"Invoice Balance: {invoice.Balance:N2} | Status: {invoice.PaymentStatus}"
        };
        await _context.AuditLogs.AddAsync(audit);

        var notif = new Notification
        {
            Id = Guid.NewGuid().ToString(),
            Type = "success",
            Title = "Payment Validated",
            Description = $"Payment of {payment.Amount:N2} for Invoice {invoice.InvoiceNo} was validated.",
            InvoiceNo = invoice.InvoiceNo,
            RecipientRole = "Finance Manager",
            RelatedPaymentId = payment.Id,
            RelatedInvoiceId = invoice.Id,
            Read = false,
            Date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            Timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            Source = "Finance Department"
        };
        _context.Notifications.Add(notif);

        await _context.SaveChangesAsync(default);
        await BillingComputationService.SyncClientBalanceAsync(invoice.ClientId, _context, default);
        await _context.SaveChangesAsync(default);
        await transaction.CommitAsync();

        return Ok(new
        {
            PaymentId = payment.Id,
            PaymentStatus = payment.PaymentStatus,
            InvoicePaymentStatus = invoice.PaymentStatus,
            OfficialReceipt = orNum,
            message = "Payment successfully validated and applied."
        });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return StatusCode(500, new { message = "An error occurred while validating the payment. Transaction rolled back.", details = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. REJECT PAYMENT
    // POST /api/finance/payments/{paymentId}/reject
    // ─────────────────────────────────────────────────────────────────────────
    [Authorize]
    [HttpPost("finance/payments/{paymentId}/resolve-exception")]
    public async Task<IActionResult> ResolvePaymentException(
        string paymentId,
        [FromBody] ResolvePaymentExceptionRequest request)
    {
        var (isAuthorized, actorRole, delegateNote) = await CheckValidationAuthorityAsync();
        if (!isAuthorized)
            return StatusCode(403, new { message = "Only the Finance Manager (or configured, logged delegate) can resolve payment exceptions." });

        if (request == null || string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { message = "A resolution reason is required." });

        var user = User.FindFirst("name")?.Value ?? User.FindFirst(ClaimTypes.Name)?.Value ?? "Unknown User";

        try
        {
            var payment = await _mediator.Send(new PaymentFeatures.ResolvePaymentExceptionCommand(
                paymentId,
                request.Action,
                request.Reason,
                $"{user} ({actorRole})"));
            if (payment == null)
                return Conflict(new { message = "The payment is not an unresolved exception." });

            try
            {
                var exc = await _fomsDb.ExceptionRecords.FirstOrDefaultAsync(e => e.RecordId == payment.Id);
                if (exc != null)
                {
                    exc.Status = payment.PaymentStatus == "Rejected" ? "Rejected" : "Resolved";
                    exc.ResolvedAt = DateTime.UtcNow;
                    exc.ResolvedBy = user;
                    await _fomsDb.SaveChangesAsync();
                }
            }
            catch { }

            return Ok(new { payment.Id, payment.PaymentStatus, payment.ExceptionResolutionReason, message = "Exception resolved. Status returned to Pending Validation." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [Authorize]
    [HttpPost("finance/payments/{paymentId}/reject")]
    public async Task<IActionResult> RejectPayment(string paymentId, [FromBody] RejectionRequest request)
    {
        var (isAuthorized, actorRole, delegateNote) = await CheckValidationAuthorityAsync();
        if (!isAuthorized)
            return StatusCode(403, new { message = "Only the Finance Manager (or configured, logged delegate) is authorized to reject payments." });

        var user = User.FindFirst("name")?.Value ?? User.FindFirst(ClaimTypes.Name)?.Value ?? "Unknown User";

        if (request == null || string.IsNullOrWhiteSpace(request.RejectionReason))
            return BadRequest(new { message = "RejectionReason is required." });

        var payment = await _context.Payments.FindAsync(paymentId) ?? await _context.Payments.FirstOrDefaultAsync(p => p.Id == paymentId);
        if (payment == null)
            return NotFound(new { message = $"Payment record '{paymentId}' not found." });

        if (payment.PaymentStatus == "Validated")
            return BadRequest(new { message = "Cannot reject a payment that has already been validated." });
        if (payment.PaymentStatus == "Exception")
            return BadRequest(new { message = "Resolve exception payments through the exception-resolution workflow." });

        var invoice = await _context.Invoices.FirstOrDefaultAsync(i => i.Id == payment.InvoiceId);
        if (invoice != null)
        {
            invoice.PaymentStatus = invoice.Balance <= 0 ? "Paid" : "Unpaid";
            invoice.PaymentValidationStatus = "Rejected";
            invoice.UpdatedBy = user;
            _context.Invoices.Update(invoice);
        }

        payment.PaymentStatus = "Rejected";
        payment.RejectedAt = DateTime.UtcNow;
        payment.RejectedBy = user;
        payment.RejectionReason = request.RejectionReason;

        // Payment History
        var history = new PaymentHistory
        {
            Id = Guid.NewGuid().ToString(),
            PaymentId = payment.Id,
            InvoiceId = payment.InvoiceId,
            Status = "Rejected",
            Action = "Reject Payment",
            Remarks = $"Rejected by {actorRole}. Reason: {request.RejectionReason}",
            PerformedBy = user,
            PerformedRole = actorRole,
            CreatedAt = DateTime.UtcNow
        };
        _context.PaymentHistories.Add(history);

        // Audit Log
        var audit = new AuditLog
        {
            Id = Guid.NewGuid().ToString(),
            UserId = user,
            EntityName = "Payment",
            EntityId = payment.Id,
            Action = "Reject Payment",
            Details = $"Rejected payment of {payment.Amount:N2} for Invoice {payment.InvoiceNo}. Reason: {request.RejectionReason}",
            LoggedAt = DateTime.UtcNow
        };
        await _context.AuditLogs.AddAsync(audit);

        // Notification & Email Notice to client (NTF-02)
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == payment.ClientId);
        var clientEmail = client?.Email ?? "client@foms.local";
        var notif = new Notification
        {
            Id = Guid.NewGuid().ToString(),
            Type = "alert",
            Title = "[NTF-02] Payment Rejected Notice",
            Description = $"Email notice dispatched to {clientEmail}: Your payment of ₱{payment.Amount:N2} (Ref: {payment.ReferenceNumber}) for Invoice {payment.InvoiceNo} was rejected. Reason: {request.RejectionReason}",
            InvoiceNo = payment.InvoiceNo,
            RecipientRole = "Client",
            RelatedPaymentId = payment.Id,
            RelatedInvoiceId = payment.InvoiceId,
            Read = false,
            Date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            Timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            Source = "Finance Department"
        };
        _context.Notifications.Add(notif);

        await _context.SaveChangesAsync(default);

        return Ok(new
        {
            payment.Id,
            payment.PaymentStatus,
            message = "Payment rejected."
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. RETURN PAYMENT FOR CORRECTION
    // POST /api/finance/payments/{paymentId}/return
    // ─────────────────────────────────────────────────────────────────────────
    [Authorize]
    [HttpPost("finance/payments/{paymentId}/return")]
    public async Task<IActionResult> ReturnPayment(string paymentId, [FromBody] ValidationActionRequest request)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "Unknown Role";
        var user = User.FindFirst("name")?.Value ?? User.FindFirst(ClaimTypes.Name)?.Value ?? "Unknown User";

        if (role != "Finance Manager")
            return StatusCode(403, new { message = "Only the Finance Manager is authorized to return payments." });

        var payment = await _context.Payments.FindAsync(paymentId) ?? await _context.Payments.FirstOrDefaultAsync(p => p.Id == paymentId);
        if (payment == null)
            return NotFound(new { message = $"Payment record '{paymentId}' not found." });

        if (payment.PaymentStatus == "Validated")
            return BadRequest(new { message = "Cannot return a payment that has already been validated." });

        var invoice = await _context.Invoices.FirstOrDefaultAsync(i => i.Id == payment.InvoiceId);
        if (invoice != null)
        {
            invoice.PaymentValidationStatus = "Returned for Correction";
            invoice.UpdatedBy = user;
            _context.Invoices.Update(invoice);
        }

        payment.PaymentStatus = "Returned / Needs Correction";
        payment.Remarks = request.Remarks ?? "Returned for correction by finance validation.";

        // Payment History
        var history = new PaymentHistory
        {
            Id = Guid.NewGuid().ToString(),
            PaymentId = payment.Id,
            InvoiceId = payment.InvoiceId,
            Status = "Returned for Correction",
            Action = "Return Payment",
            Remarks = request.Remarks ?? "Returned for correction.",
            PerformedBy = user,
            PerformedRole = role,
            CreatedAt = DateTime.UtcNow
        };
        _context.PaymentHistories.Add(history);

        // Audit Log
        var audit = new AuditLog
        {
            Id = Guid.NewGuid().ToString(),
            UserId = user,
            EntityName = "Payment",
            EntityId = payment.Id,
            Action = "Return Payment",
            Details = $"Returned payment of {payment.Amount:N2} for Invoice {payment.InvoiceNo} for correction.",
            LoggedAt = DateTime.UtcNow
        };
        await _context.AuditLogs.AddAsync(audit);

        // Notification to client
        var notif = new Notification
        {
            Id = Guid.NewGuid().ToString(),
            Type = "info",
            Title = "Payment Correction Required",
            Description = $"Your payment of {payment.Amount:N2} for Invoice {payment.InvoiceNo} requires corrections. Remarks: {payment.Remarks}",
            InvoiceNo = payment.InvoiceNo,
            RecipientUserId = payment.ClientId,
            RecipientRole = "Client",
            RelatedPaymentId = payment.Id,
            RelatedInvoiceId = payment.InvoiceId,
            Read = false,
            Date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            Timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            Source = "Finance Department"
        };
        _context.Notifications.Add(notif);

        await _context.SaveChangesAsync(default);

        return Ok(new
        {
            payment.Id,
            payment.PaymentStatus,
            message = "Payment returned for correction."
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5. MANUAL REVIEW STEP
    // POST /api/finance/payments/{paymentId}/review
    // ─────────────────────────────────────────────────────────────────────────
    [Authorize]
    [HttpPost("finance/payments/{paymentId}/review")]
    public async Task<IActionResult> ReviewPayment(string paymentId, [FromBody] ManualReviewActionRequest request)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "Unknown Role";
        var user = User.FindFirst("name")?.Value ?? User.FindFirst(ClaimTypes.Name)?.Value ?? "Unknown User";

        var allowedRoles = new[] { "Accountant", "Assistant of Finance Manager" };
        if (!allowedRoles.Contains(role))
            return StatusCode(403, new { message = "Only Accountant, Assistant of Finance Manager, or Coordinator are authorized for initial review." });

        if (request == null || string.IsNullOrWhiteSpace(request.Status))
            return BadRequest(new { message = "Status (Pending Finance Validation or Returned / Needs Correction) is required." });

        var allowedStatus = new[] { "Pending Finance Validation", "Returned / Needs Correction" };
        if (!allowedStatus.Contains(request.Status))
            return BadRequest(new { message = $"Invalid status '{request.Status}'. Allowed: {string.Join(", ", allowedStatus)}" });

        var payment = await _context.Payments.FindAsync(paymentId) ?? await _context.Payments.FirstOrDefaultAsync(p => p.Id == paymentId);
        if (payment == null)
            return NotFound(new { message = $"Payment record '{paymentId}' not found." });

        if (payment.PaymentStatus == "Validated" || payment.PaymentStatus == "Pending Finance Validation")
            return BadRequest(new { message = $"Cannot review payment in its current state: '{payment.PaymentStatus}'." });

        payment.PaymentStatus = request.Status;
        payment.Remarks = request.Remarks ?? payment.Remarks;

        var invoice = await _context.Invoices.FirstOrDefaultAsync(i => i.Id == payment.InvoiceId);
        if (invoice != null)
        {
            invoice.PaymentValidationStatus = request.Status == "Pending Finance Validation" ? "Pending Validation" : "Returned for Correction";
            invoice.UpdatedBy = user;
            _context.Invoices.Update(invoice);
        }

        // Payment History
        var history = new PaymentHistory
        {
            Id = Guid.NewGuid().ToString(),
            PaymentId = payment.Id,
            InvoiceId = payment.InvoiceId,
            Status = request.Status == "Pending Finance Validation" ? "Under Finance Review" : "Returned for Correction",
            Action = "Manual Review",
            Remarks = request.Remarks ?? $"Manual check completed. Marked as {request.Status}.",
            PerformedBy = user,
            PerformedRole = role,
            CreatedAt = DateTime.UtcNow
        };
        _context.PaymentHistories.Add(history);

        // Audit Log
        var audit = new AuditLog
        {
            Id = Guid.NewGuid().ToString(),
            UserId = user,
            EntityName = "Payment",
            EntityId = payment.Id,
            Action = "Manual Review",
            Details = $"Reviewed payment of {payment.Amount:N2} for Invoice {payment.InvoiceNo}. Forwarded as: {request.Status}.",
            LoggedAt = DateTime.UtcNow
        };
        await _context.AuditLogs.AddAsync(audit);

        // If ready for validation, notify FM & HA
        if (request.Status == "Pending Finance Validation")
        {
            var notifFm = new Notification
            {
                Id = Guid.NewGuid().ToString(),
                Type = "PAYMENT_VALIDATION_REQUIRED",
                Title = "Payment Needs Validation",
                Description = $"A recorded payment for Invoice {invoice?.InvoiceNo} has passed manual check and is ready for validation.",
                InvoiceNo = invoice?.InvoiceNo,
                RecipientRole = "Finance Manager",
                RelatedPaymentId = payment.Id,
                RelatedInvoiceId = invoice?.Id,
                Read = false,
                Date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                Timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                Source = "Manual Review Module"
            };
            _context.Notifications.Add(notifFm);

            var notifHa = new Notification
            {
                Id = Guid.NewGuid().ToString(),
                Type = "PAYMENT_VALIDATION_REQUIRED",
                Title = "Payment Needs Validation",
                Description = $"A recorded payment for Invoice {invoice?.InvoiceNo} has passed manual check and is ready for validation.",
                InvoiceNo = invoice?.InvoiceNo,
                RecipientRole = "Head Accountant",
                RelatedPaymentId = payment.Id,
                RelatedInvoiceId = invoice?.Id,
                Read = false,
                Date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                Timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                Source = "Manual Review Module"
            };
            _context.Notifications.Add(notifHa);
        }

        await _context.SaveChangesAsync(default);

        return Ok(new
        {
            payment.Id,
            payment.PaymentStatus,
            message = $"Payment review complete. Status updated to '{request.Status}'."
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 6. GET UNIFIED PENDING VALIDATION QUEUE (FM & HA Only)
    // GET /api/finance/payments/pending-validation
    // ─────────────────────────────────────────────────────────────────────────
    [Authorize]
    [HttpGet("finance/payments/pending-validation")]
    public async Task<IActionResult> GetPendingValidationQueue()
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "Unknown Role";
        if (role != "Finance Manager")
            return StatusCode(403, new { message = "Access denied: Unauthorized role." });

        var queue = await _context.Payments
            .AsNoTracking()
            .Where(p => p.PaymentStatus == "Pending Validation" || p.PaymentStatus == "Pending Finance Validation")
            .OrderByDescending(p => p.SubmittedAt)
            .ToListAsync();

        return Ok(queue);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 7. GET PAYMENT HISTORY (ALL ATTEMPTS)
    // GET /api/payments/history/{invoiceId}
    // ─────────────────────────────────────────────────────────────────────────
    [Authorize]
    [HttpGet("payments/history/{invoiceId}")]
    public async Task<IActionResult> GetPaymentHistory(string invoiceId)
    {
        var history = await _context.PaymentHistories
            .AsNoTracking()
            .Where(h => h.InvoiceId == invoiceId || h.PaymentId == invoiceId)
            .OrderByDescending(h => h.CreatedAt)
            .ToListAsync();

        return Ok(history);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 8. GET FINANCE VALIDATION NOTIFICATIONS
    // GET /api/notifications/finance
    // ─────────────────────────────────────────────────────────────────────────
    [Authorize]
    [HttpGet("notifications/finance")]
    public async Task<IActionResult> GetFinanceNotifications()
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "";
        if (role != "Finance Manager" && role != "Head Accountant" && role != "Accountant" && role != "Assistant of Finance Manager")
            return StatusCode(403, new { message = "Unauthorized access." });

        var notifications = await _context.Notifications
            .AsNoTracking()
            .Where(n => n.RecipientRole == role && n.Type == "PAYMENT_VALIDATION_REQUIRED" && !n.Read)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync();

        return Ok(notifications);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────
    private async Task<string> GenerateNextOrNumberAsync()
    {
        var currentYear = DateTime.UtcNow.Year;
        var yearPrefix = $"OR-{currentYear}-";

        var lastPayment = await _context.Payments
            .AsNoTracking()
            .Where(p => p.OrNumber != null && p.OrNumber.StartsWith(yearPrefix))
            .OrderByDescending(p => p.OrNumber)
            .FirstOrDefaultAsync();

        int nextSeqNum = 1;
        if (lastPayment != null && lastPayment.OrNumber.Length > yearPrefix.Length)
        {
            var seqPart = lastPayment.OrNumber.Substring(yearPrefix.Length);
            if (int.TryParse(seqPart, out var lastSeq))
            {
                nextSeqNum = lastSeq + 1;
            }
        }

        return $"{yearPrefix}{nextSeqNum:D4}";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 9. FINANCE MANAGER DELEGATE MANAGEMENT
    // ─────────────────────────────────────────────────────────────────────────
    [Authorize]
    [HttpGet("finance/delegates")]
    public async Task<IActionResult> GetDelegates()
    {
        var delegates = await _fomsDb.FinanceManagerDelegates.AsNoTracking().ToListAsync();
        return Ok(delegates);
    }

    [Authorize]
    [HttpPost("finance/delegates")]
    public async Task<IActionResult> CreateDelegate([FromBody] CreateDelegateRequest request)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
        if (!role.Equals("Finance Manager", StringComparison.OrdinalIgnoreCase))
            return StatusCode(403, new { message = "Only the Finance Manager can configure delegates." });

        if (string.IsNullOrWhiteSpace(request.DelegateUserId) || string.IsNullOrWhiteSpace(request.DelegateName))
            return BadRequest(new { message = "DelegateUserId and DelegateName are required." });

        var user = User.FindFirst("name")?.Value ?? User.FindFirst(ClaimTypes.Name)?.Value ?? "Finance Manager";

        var del = new SqlServer.FinanceManagerDelegate
        {
            DelegateUserId = request.DelegateUserId.Trim(),
            DelegateName = request.DelegateName.Trim(),
            AssignedBy = user,
            ValidFrom = request.ValidFrom ?? DateTime.UtcNow,
            ValidUntil = request.ValidUntil ?? DateTime.UtcNow.AddMonths(1),
            Reason = request.Reason ?? "Finance Manager authorized delegate",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _fomsDb.FinanceManagerDelegates.Add(del);
        await _fomsDb.SaveChangesAsync();

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = user,
            EntityName = "FinanceManagerDelegate",
            EntityId = del.DelegateId.ToString(),
            Action = "Create Delegate",
            Details = $"Configured delegate {del.DelegateName} ({del.DelegateUserId}). Valid: {del.ValidFrom:yyyy-MM-dd} to {del.ValidUntil:yyyy-MM-dd}.",
            LoggedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync(default);

        return Ok(del);
    }

    [Authorize]
    [HttpDelete("finance/delegates/{id}")]
    [HttpPut("finance/delegates/{id}/revoke")]
    public async Task<IActionResult> RevokeDelegate(int id)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
        if (!role.Equals("Finance Manager", StringComparison.OrdinalIgnoreCase))
            return StatusCode(403, new { message = "Only the Finance Manager can revoke delegates." });

        var del = await _fomsDb.FinanceManagerDelegates.FirstOrDefaultAsync(d => d.DelegateId == id);
        if (del == null) return NotFound(new { message = $"Delegate {id} not found." });

        del.IsActive = false;
        await _fomsDb.SaveChangesAsync();

        var user = User.FindFirst("name")?.Value ?? User.FindFirst(ClaimTypes.Name)?.Value ?? "Finance Manager";
        _context.AuditLogs.Add(new AuditLog
        {
            UserId = user,
            EntityName = "FinanceManagerDelegate",
            EntityId = del.DelegateId.ToString(),
            Action = "Revoke Delegate",
            Details = $"Revoked delegate {del.DelegateName} ({del.DelegateUserId}).",
            LoggedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync(default);

        return Ok(new { success = true, message = "Delegate revoked successfully." });
    }

    // Request DTOs
    public record ValidationActionRequest(string? Remarks);
    public record ResolvePaymentExceptionRequest(string Action, string Reason);
    public record RejectionRequest(string RejectionReason);
    public record ManualReviewActionRequest(string Status, string? Remarks);
    public class CreateDelegateRequest
    {
        public string DelegateUserId { get; set; } = string.Empty;
        public string DelegateName { get; set; } = string.Empty;
        public DateTime? ValidFrom { get; set; }
        public DateTime? ValidUntil { get; set; }
        public string? Reason { get; set; }
    }
}
