using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using FOMS.Application.Interfaces;
using FOMS.Application.Services;
using FOMS.Domain.Entities;

namespace FOMS.Application.Features;

public static class PaymentFeatures
{
    public record ResolvePaymentExceptionCommand(
        string Id,
        string Action,
        string Reason,
        string ResolvedBy
    ) : IRequest<Payment?>;

    public class ResolvePaymentExceptionCommandHandler : IRequestHandler<ResolvePaymentExceptionCommand, Payment?>
    {
        private readonly IApplicationDbContext _context;

        public ResolvePaymentExceptionCommandHandler(IApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<Payment?> Handle(ResolvePaymentExceptionCommand request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.Action))
                throw new InvalidOperationException("An exception-resolution action is required.");
            if (string.IsNullOrWhiteSpace(request.Reason))
                throw new InvalidOperationException("A resolution reason is required.");

            var payment = await _context.Payments
                .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);
            if (payment == null || payment.PaymentStatus != "Exception")
                return null;

            var action = request.Action.Trim().ToLowerInvariant();
            var rejectionReason = payment.RejectionReason ?? string.Empty;
            var isDuplicate = rejectionReason.Contains("duplicate", StringComparison.OrdinalIgnoreCase);
            var isShort = rejectionReason.Contains("short", StringComparison.OrdinalIgnoreCase);
            var isOver = rejectionReason.Contains("over", StringComparison.OrdinalIgnoreCase);

            if (isDuplicate)
            {
                // PAY-10: Duplicate payments must always be rejected
                if (action != "reject")
                    throw new InvalidOperationException("Duplicate payment exceptions cannot be approved or validated; duplicate payments must be rejected.");

                payment.PaymentStatus = "Rejected";
                payment.RejectedAt = DateTime.UtcNow;
                payment.RejectedBy = request.ResolvedBy;
                payment.RejectionReason = request.Reason.Trim();
            }
            else if (action == "reject")
            {
                payment.PaymentStatus = "Rejected";
                payment.RejectedAt = DateTime.UtcNow;
                payment.RejectedBy = request.ResolvedBy;
                payment.RejectionReason = request.Reason.Trim();

                if (isShort)
                {
                    // PAY-10: Short payment rejected + email notice
                    var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == payment.ClientId, cancellationToken);
                    var clientEmail = client?.Email ?? "client@foms.local";
                    _context.Notifications.Add(new Notification
                    {
                        Id = Guid.NewGuid().ToString(),
                        Type = "warning",
                        Title = "[NTF-02] Short Payment Notice Dispatched",
                        Description = $"Email notice sent to client ({clientEmail}): Payment {payment.ReferenceNumber} of {payment.Amount:N2} for Invoice {payment.InvoiceNo} was rejected due to short payment. Reason: {request.Reason.Trim()}.",
                        InvoiceNo = payment.InvoiceNo,
                        RecipientRole = "Client",
                        RelatedPaymentId = payment.Id,
                        RelatedInvoiceId = payment.InvoiceId,
                        Read = false,
                        Date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                        Timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                        Source = "Finance Department"
                    });
                }
            }
            else if (action is "send-to-validation" or "resolve" or "new-payment")
            {
                if (isShort && action == "send-to-validation")
                {
                    throw new InvalidOperationException("Short payments cannot be sent to validation; short payments must either be recorded as a new payment for the exact amount or rejected with an email notice.");
                }

                // PAY-10: Resolved payments return to Pending Validation, never directly Paid.
                payment.PaymentStatus = "Pending Validation";

                var reason = request.Reason.Trim();
                if (isOver && reason.Contains("credited", StringComparison.OrdinalIgnoreCase) && !reason.Contains("unapplied credit", StringComparison.OrdinalIgnoreCase))
                {
                    reason = reason.Replace("credited", "held as unapplied credit applied", StringComparison.OrdinalIgnoreCase);
                }
                payment.ExceptionResolutionReason = reason;
                payment.RejectionReason = null;
            }
            else
            {
                throw new InvalidOperationException("Action must be 'reject', 'resolve', or 'send-to-validation'.");
            }

            await _context.PaymentHistories.AddAsync(new PaymentHistory
            {
                PaymentId = payment.Id,
                InvoiceId = payment.InvoiceId,
                Status = payment.PaymentStatus,
                Action = "Resolve Payment Exception",
                Remarks = $"{request.Action}: {request.Reason.Trim()}",
                PerformedBy = request.ResolvedBy,
                PerformedRole = "Finance Manager",
                CreatedAt = DateTime.UtcNow
            }, cancellationToken);

            await _context.AuditLogs.AddAsync(new AuditLog
            {
                UserId = request.ResolvedBy,
                EntityName = "Payment",
                EntityId = payment.Id,
                Action = "Resolve Payment Exception",
                Details = $"Resolved payment exception using '{action}'. Reason: {request.Reason.Trim()}",
                BeforeValue = "Status: Exception",
                AfterValue = $"Status: {payment.PaymentStatus}"
            }, cancellationToken);

            await _context.SaveChangesAsync(cancellationToken);
            return payment;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // GET PAYMENTS
    // ─────────────────────────────────────────────────────────────────
    public record GetPaymentsQuery : IRequest<List<Payment>>;

    public class GetPaymentsQueryHandler : IRequestHandler<GetPaymentsQuery, List<Payment>>
    {
        private readonly IApplicationDbContext _context;

        public GetPaymentsQueryHandler(IApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<Payment>> Handle(GetPaymentsQuery request, CancellationToken cancellationToken)
        {
            return await _context.Payments.ToListAsync(cancellationToken);
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // RECORD PAYMENT — with full validation guards (TASK 3, 4, 10)
    // ─────────────────────────────────────────────────────────────────
    public record RecordPaymentCommand(
        string OrNumber,
        string InvoiceId,
        string InvoiceNo,
        string ClientId,
        string ClientName,
        string PaymentDate,
        decimal Amount,
        string PaymentMethod,
        string ReferenceNumber,
        string? ProofImageUrl,
        string? Remarks,
        string RecordedBy
    ) : IRequest<Payment>;

    public class RecordPaymentCommandHandler : IRequestHandler<RecordPaymentCommand, Payment>
    {
        private readonly IApplicationDbContext _context;

        public RecordPaymentCommandHandler(IApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<Payment> Handle(RecordPaymentCommand request, CancellationToken cancellationToken)
        {
            // Guard — Reference Number is required
            if (string.IsNullOrWhiteSpace(request.ReferenceNumber))
                throw new InvalidOperationException("Reference Number is required.");

            // ── TASK 3: Guard — Zero / Negative Payment ──────────────────────
            if (request.Amount <= 0m)
                throw new InvalidOperationException("Payment amount must be greater than zero.");

            // Guard — Proof of payment format check (PDF/JPG/PNG)
            if (!string.IsNullOrWhiteSpace(request.ProofImageUrl))
            {
                var lower = request.ProofImageUrl.Trim().ToLowerInvariant();
                var clean = lower.Split('?')[0].Split('#')[0];
                var ext = System.IO.Path.GetExtension(clean);
                var isDataUrl = lower.StartsWith("data:image/png") || lower.StartsWith("data:image/jpeg") || lower.StartsWith("data:image/jpg") || lower.StartsWith("data:application/pdf");
                if (!isDataUrl && ext is not (".pdf" or ".jpg" or ".jpeg" or ".png"))
                    throw new InvalidOperationException("Proof of payment must be a PDF, JPG, or PNG file.");
            }

            // ── Fetch Invoice (or mark Unidentified On Hold) ─────────────────
            var isUnidentified = string.IsNullOrWhiteSpace(request.InvoiceId) ||
                                 request.InvoiceId.Equals("UNIDENTIFIED", StringComparison.OrdinalIgnoreCase);

            var invoice = isUnidentified
                ? null
                : await _context.Invoices.FirstOrDefaultAsync(i => i.Id == request.InvoiceId, cancellationToken);

            if (!isUnidentified && invoice == null)
                throw new InvalidOperationException($"Invoice '{request.InvoiceId}' not found.");

            // Duplicate reference check
            var duplicateReference = await _context.Payments
                .AnyAsync(p => p.ReferenceNumber == request.ReferenceNumber, cancellationToken);

            string? exceptionReason = null;
            string initialStatus = "Pending Validation";

            if (isUnidentified)
            {
                initialStatus = "On Hold";
            }
            else if (duplicateReference)
            {
                exceptionReason = $"Duplicate payment reference: {request.ReferenceNumber}.";
                initialStatus = "Exception";
            }
            else if (invoice != null && request.Amount < invoice.Balance)
            {
                exceptionReason = $"Short payment: received {request.Amount:N2}; invoice balance is {invoice.Balance:N2}.";
                initialStatus = "Exception";
            }
            else if (invoice != null && request.Amount > invoice.Balance)
            {
                exceptionReason = $"Overpayment: received {request.Amount:N2}; invoice balance is {invoice.Balance:N2}.";
                initialStatus = "Exception";
            }

            // ── Record the payment ────────────────────────────────────────────
            var beforeBalance = invoice?.Balance ?? 0m;
            var beforeStatus = invoice?.PaymentStatus ?? "Unidentified";

            // Generate a temporary OR number placeholder (real OR is generated on validation)
            var tempOrNum = $"PENDING-{Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper()}";

            // Check if an OR number was explicitly provided; if not, use placeholder
            var orNum = string.IsNullOrWhiteSpace(request.OrNumber) ? tempOrNum : request.OrNumber;

            // Guard — Duplicate OR Number (only if not using placeholder)
            if (!orNum.StartsWith("PENDING-"))
            {
                var orExists = await _context.Payments
                    .AnyAsync(p => p.OrNumber == orNum, cancellationToken);
                if (orExists)
                    throw new InvalidOperationException(
                        $"Official Receipt number '{orNum}' already exists. Each payment must have a unique OR number.");
            }

            var payment = new Payment
            {
                OrNumber = orNum,
                InvoiceId = request.InvoiceId,
                InvoiceNo = request.InvoiceNo,
                ClientId = request.ClientId,
                ClientName = request.ClientName,
                PaymentDate = request.PaymentDate,
                Amount = request.Amount,
                PaymentMethod = request.PaymentMethod,
                ReferenceNumber = request.ReferenceNumber,
                ProofImageUrl = request.ProofImageUrl,
                Remarks = request.Remarks,
                RecordedBy = request.RecordedBy,
                DateRecorded = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                PaymentStatus = initialStatus,
                RejectionReason = exceptionReason,
                SubmittedAt = DateTime.UtcNow
            };

            // ── TASK 17: Audit Log with Before/After values ────────────────────
            var audit = new AuditLog
            {
                UserId = request.RecordedBy,
                EntityName = "Payment",
                EntityId = payment.Id,
                Action = "Record Payment",
                Details = $"Recorded payment of {request.Amount:N2} for invoice {request.InvoiceNo}. " +
                          $"Method: {request.PaymentMethod}. Ref: {request.ReferenceNumber}. " +
                        $"Status: {payment.PaymentStatus}. {exceptionReason}",
                BeforeValue = $"Balance: {beforeBalance:N2} | Status: {beforeStatus}",
                AfterValue = $"Balance: {invoice.Balance:N2} | Status: {payment.PaymentStatus}"
            };
            await _context.AuditLogs.AddAsync(audit, cancellationToken);

            _context.Payments.Add(payment);

            // C2 FR-029 / PAY-10: Route validation and exception work to the Finance Manager.
            var notifFm = new Notification
            {
                Id = Guid.NewGuid().ToString(),
                Type = "PAYMENT_VALIDATION_REQUIRED",
                Title = payment.PaymentStatus == "Exception" ? "Payment Exception Requires Resolution" : "New Payment Recorded",
                Description = exceptionReason ??
                    $"A payment of {request.Amount:N2} for Invoice {request.InvoiceNo} is pending Finance validation.",
                InvoiceNo = request.InvoiceNo,
                RecipientRole = "Finance Manager",
                RelatedPaymentId = payment.Id,
                RelatedInvoiceId = request.InvoiceId,
                Read = false,
                Date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                Timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                Source = "Accountant"
            };
            _context.Notifications.Add(notifFm);

            await _context.SaveChangesAsync(cancellationToken);

            return payment;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // DELETE PAYMENT — revert invoice and sync balance
    // ─────────────────────────────────────────────────────────────────
    public record DeletePaymentCommand(string Id, string User, string Role) : IRequest<bool>;

    public class DeletePaymentCommandHandler : IRequestHandler<DeletePaymentCommand, bool>
    {
        private readonly IApplicationDbContext _context;

        public DeletePaymentCommandHandler(IApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<bool> Handle(DeletePaymentCommand request, CancellationToken cancellationToken)
        {
            var payment = await _context.Payments
                .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);
            if (payment == null) return false;

            // Revert Invoice
            var invoice = await _context.Invoices
                .FirstOrDefaultAsync(i => i.Id == payment.InvoiceId, cancellationToken);
            if (invoice != null)
            {
                var beforeBalance = invoice.Balance;
                var beforeStatus = invoice.PaymentStatus;

                var appliedAmount = payment.AppliedAmount > 0m ? payment.AppliedAmount : payment.Amount;
                invoice.AmountPaid = Math.Max(0m, invoice.AmountPaid - appliedAmount);
                BillingComputationService.RecalculateInvoice(invoice);
                invoice.UpdatedBy = $"{request.User} ({request.Role})";

                var audit = new AuditLog
                {
                    UserId = $"{request.User} ({request.Role})",
                    EntityName = "Payment",
                    EntityId = payment.Id,
                    Action = "Delete Payment",
                    Details = $"Deleted payment of {payment.Amount:N2} for invoice {payment.InvoiceNo} (OR: {payment.OrNumber}).",
                    BeforeValue = $"Balance: {beforeBalance:N2} | Status: {beforeStatus}",
                    AfterValue = $"Balance: {invoice.Balance:N2} | Status: {invoice.PaymentStatus}"
                };
                await _context.AuditLogs.AddAsync(audit, cancellationToken);
            }

            // Remove associated PaymentCollection and OfficialReceipt
            var collection = await _context.PaymentCollections
                .FirstOrDefaultAsync(c => c.InvoiceId == payment.InvoiceId
                                          && c.AmountCollected == payment.Amount,
                                     cancellationToken);
            if (collection != null)
            {
                var receipt = await _context.OfficialReceipts
                    .FirstOrDefaultAsync(r => r.PaymentCollectionId == collection.Id, cancellationToken);
                if (receipt != null) _context.OfficialReceipts.Remove(receipt);
                _context.PaymentCollections.Remove(collection);
            }

            _context.Payments.Remove(payment);
            await _context.SaveChangesAsync(cancellationToken);

            // TASK 12: Sync client balance after reversal
            if (invoice != null)
            {
                // Sync Billing Invoice and ReceivableBalance
                await BillingComputationService.SyncBillingInvoiceAndReceivableAsync(invoice, _context, cancellationToken);

                await BillingComputationService.SyncClientBalanceAsync(payment.ClientId, _context, cancellationToken);
                await _context.SaveChangesAsync(cancellationToken);
            }

            return true;
        }
    }
}
