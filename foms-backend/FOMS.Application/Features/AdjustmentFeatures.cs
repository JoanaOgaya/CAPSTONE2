using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using FOMS.Application.Interfaces;
using FOMS.Application.Services;
using FOMS.Domain.Entities;

namespace FOMS.Application.Features;

public static class AdjustmentFeatures
{
    // ─────────────────────────────────────────────────────────────────
    // Manila timezone helper (R4.11 q4 / FIN-23)
    // Mirrors CeoApprovalGateService.GetManilaToday().
    // ─────────────────────────────────────────────────────────────────
    internal static DateTime GetManilaToday()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "Singapore Standard Time" : "Asia/Manila");
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date;
    }

    internal static string GetManilaTodayString() => GetManilaToday().ToString("yyyy-MM-dd");

    // ─────────────────────────────────────────────────────────────────
    // GET ADJUSTMENTS — newest first
    // ─────────────────────────────────────────────────────────────────
    public record GetAdjustmentsQuery : IRequest<List<PaymentAdjustment>>;

    public class GetAdjustmentsQueryHandler : IRequestHandler<GetAdjustmentsQuery, List<PaymentAdjustment>>
    {
        private readonly IApplicationDbContext _context;

        public GetAdjustmentsQueryHandler(IApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<PaymentAdjustment>> Handle(GetAdjustmentsQuery request, CancellationToken cancellationToken)
        {
            if (!await _context.PaymentAdjustments.AnyAsync(cancellationToken))
            {
                var samples = new List<PaymentAdjustment>
                {
                    new()
                    {
                        Id = "ADJ-001",
                        InvoiceNo = "LZD-2026-0001",
                        AdjustmentType = "Credit",
                        Amount = 2500m,
                        Reason = "Volume tier discount correction for Q3 shipments",
                        AdjustedBy = "Bookkeeper",
                        ApprovedBy = "Crystalyn Joyce C. Fajardo",
                        DateRequested = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd"),
                        Status = "Pending"
                    },
                    new()
                    {
                        Id = "ADJ-002",
                        InvoiceNo = "BI-2026-0001",
                        AdjustmentType = "Debit",
                        Amount = 1200m,
                        Reason = "Special delivery surcharge addition",
                        AdjustedBy = "Bookkeeper",
                        ApprovedBy = "Crystalyn Joyce C. Fajardo",
                        DateRequested = DateTime.UtcNow.AddDays(-3).ToString("yyyy-MM-dd"),
                        DateApproved = DateTime.UtcNow.AddDays(-2).ToString("yyyy-MM-dd"),
                        Status = "Approved"
                    }
                };
                _context.PaymentAdjustments.AddRange(samples);
                await _context.SaveChangesAsync(cancellationToken);
            }

            // Clean up any legacy GUID records into clean ADJ-00x records
            var guidRecords = await _context.PaymentAdjustments
                .Where(a => a.Id.Length > 15 && a.Id.Contains("-"))
                .ToListAsync(cancellationToken);

            if (guidRecords.Count > 0)
            {
                int nextNum = 3;
                foreach (var g in guidRecords)
                {
                    var cleanId = $"ADJ-{nextNum:D3}";
                    while (await _context.PaymentAdjustments.AnyAsync(x => x.Id == cleanId, cancellationToken))
                    {
                        nextNum++;
                        cleanId = $"ADJ-{nextNum:D3}";
                    }

                    _context.PaymentAdjustments.Remove(g);
                    _context.PaymentAdjustments.Add(new PaymentAdjustment
                    {
                        Id = cleanId,
                        InvoiceNo = g.InvoiceNo,
                        AdjustmentType = g.AdjustmentType,
                        Amount = g.Amount,
                        Reason = g.Reason,
                        AdjustedBy = g.AdjustedBy,
                        ApprovedBy = g.ApprovedBy,
                        DateRequested = g.DateRequested,
                        DateApproved = g.DateApproved,
                        Status = g.Status
                    });
                    nextNum++;
                }
                await _context.SaveChangesAsync(cancellationToken);
            }

            // Return newest first so the list is always up-to-date on screen
            return await _context.PaymentAdjustments
                .OrderByDescending(a => a.DateRequested)
                .ThenByDescending(a => a.Id)
                .ToListAsync(cancellationToken);
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // CREATE ADJUSTMENT REQUEST — stays Pending until approved before posting (FIN-23)
    // ─────────────────────────────────────────────────────────────────
    public record CreateAdjustmentCommand(
        string InvoiceNo,
        string AdjustmentType,   // "Credit" | "Debit" | "Write-Off" | "Credit Memo" | "Adjustment"
        decimal Amount,
        string Reason,
        string AdjustedBy = "",  // Submitter
        string ApprovedBy = "",  // Approver (if pre-assigned)
        string Role = "",        // Creator role (must be Finance Manager per FIN-23)
        DateTime? AdjustmentDate = null
    ) : IRequest<PaymentAdjustment?>;

    public class CreateAdjustmentCommandHandler : IRequestHandler<CreateAdjustmentCommand, PaymentAdjustment?>
    {
        private readonly IApplicationDbContext _context;

        public CreateAdjustmentCommandHandler(IApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<PaymentAdjustment?> Handle(CreateAdjustmentCommand request, CancellationToken cancellationToken)
        {
            // FIN-23: Adjusting entries created by the Finance Manager only
            if (!string.IsNullOrWhiteSpace(request.Role) && !FOMS.Domain.Constants.FomsRoles.IsFinanceManager(request.Role))
            {
                throw new UnauthorizedAccessException("Only the Finance Manager can create adjusting entries (FIN-23).");
            }

            // FIN-23: Reason is strictly mandatory
            if (string.IsNullOrWhiteSpace(request.Reason))
            {
                throw new InvalidOperationException("A reason is strictly required when creating an adjusting entry (FIN-23).");
            }

            // FIN-23 / R4.2: Closed accounting period blocks creation of adjusting entries
            // R4.11 q4: Use the caller-supplied adjustment date (Manila calendar date), not DateTime.UtcNow.
            var effectiveDate = (request.AdjustmentDate ?? GetManilaToday()).Date;
            bool isPeriodClosed = await _context.AccountingPeriodCloses
                .AnyAsync(p => effectiveDate >= p.PeriodStart.Date && effectiveDate <= p.PeriodEnd.Date, cancellationToken);
            if (isPeriodClosed)
            {
                throw new InvalidOperationException("Cannot create an adjusting entry in a closed accounting period (FIN-23 / R4.2).");
            }

            // Guard: amount must be positive
            if (request.Amount <= 0m)
                throw new InvalidOperationException("Adjustment amount must be greater than zero.");

            // Normalize and validate adjustment type
            var rawType = (request.AdjustmentType ?? "Credit").Trim();
            var normalizedType = rawType.Equals("Credit Memo", StringComparison.OrdinalIgnoreCase) ? "Credit" :
                                 rawType.Equals("Adjustment", StringComparison.OrdinalIgnoreCase) ? "Debit" :
                                 rawType;

            var validTypes = new[] { "Credit", "Debit", "Write-Off" };
            if (!validTypes.Contains(normalizedType, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Invalid adjustment type '{request.AdjustmentType}'. Allowed types: Credit, Debit, Write-Off.");

            // Find the invoice (match by InvoiceNo or Id)
            var invoice = await _context.Invoices
                .FirstOrDefaultAsync(i => i.InvoiceNo == request.InvoiceNo || i.Id == request.InvoiceNo, cancellationToken);
            if (invoice == null)
                return null;

            // Guard: Credit / Write-Off cannot exceed the current invoice balance / total amount
            var adjTypeLower = normalizedType.ToLower();
            var maxAllowable = invoice.Balance > 0 ? invoice.Balance : (invoice.TotalAmount > 0 ? invoice.TotalAmount : 0);
            if ((adjTypeLower == "credit" || adjTypeLower == "write-off") && maxAllowable > 0 && request.Amount > maxAllowable)
                throw new InvalidOperationException(
                    $"Adjustment amount (₱{request.Amount:N2}) cannot exceed the invoice amount (₱{maxAllowable:N2}).");

            // Generate clean sequential ID: ADJ-001, ADJ-002, ADJ-003...
            var count = await _context.PaymentAdjustments.CountAsync(cancellationToken);
            var nextId = $"ADJ-{(count + 1):D3}";
            while (await _context.PaymentAdjustments.AnyAsync(a => a.Id == nextId, cancellationToken))
            {
                count++;
                nextId = $"ADJ-{(count + 1):D3}";
            }

            var adjustment = new PaymentAdjustment
            {
                Id           = nextId,
                InvoiceNo    = invoice.InvoiceNo,
                AdjustmentType = normalizedType,
                Amount       = request.Amount,
                Reason       = request.Reason,
                AdjustedBy   = request.AdjustedBy,
                ApprovedBy   = string.Empty,   // Not yet approved — populated on Approve
                // R4.11 q4: Store the caller-supplied date (Manila calendar date), falling back to Manila today.
                DateRequested = (request.AdjustmentDate ?? GetManilaToday()).ToString("yyyy-MM-dd"),
                DateApproved = string.Empty,   // Not yet approved
                Status       = "Pending"
            };

            var audit = new AuditLog
            {
                UserId     = request.AdjustedBy,
                EntityName = "PaymentAdjustment",
                EntityId   = adjustment.Id,
                Action     = "Create Payment Adjustment Request",
                Details    = $"Created pending {request.AdjustmentType} adjustment of {request.Amount:N2} " +
                             $"for invoice {request.InvoiceNo}. Reason: {request.Reason}.",
                BeforeValue = $"Balance: {invoice.Balance:N2} | Status: {invoice.PaymentStatus}",
                AfterValue  = "Status: Pending — awaiting Accountant approval"
            };
            await _context.AuditLogs.AddAsync(audit, cancellationToken);

            _context.PaymentAdjustments.Add(adjustment);
            await _context.SaveChangesAsync(cancellationToken);

            return adjustment;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // APPROVE ADJUSTMENT — applies balance change and syncs downstream
    // ─────────────────────────────────────────────────────────────────
    public record ApproveAdjustmentCommand(
        string Id,
        string ApprovedBy,
        string Role = "Head Accountant"
    ) : IRequest<PaymentAdjustment?>;

    public class ApproveAdjustmentCommandHandler : IRequestHandler<ApproveAdjustmentCommand, PaymentAdjustment?>
    {
        private readonly IApplicationDbContext _context;

        public ApproveAdjustmentCommandHandler(IApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<PaymentAdjustment?> Handle(ApproveAdjustmentCommand request, CancellationToken cancellationToken)
        {
            var adjustment = await _context.PaymentAdjustments
                .FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken);

            if (adjustment == null || adjustment.Status != "Pending")
                return null;

            // R5.11 item 1 / R4.11 q4: Proposed approver is Head Accountant
            if (!string.IsNullOrWhiteSpace(request.Role) && !FOMS.Domain.Constants.FomsRoles.IsHeadAccountant(request.Role))
            {
                throw new UnauthorizedAccessException("Adjusting entries strictly require Head Accountant approval before posting (R5.11 item 1 / R4.11 q4).");
            }

            // Separation of Duties: Creator cannot approve their own entry
            if (!string.IsNullOrWhiteSpace(request.ApprovedBy) &&
                string.Equals(adjustment.AdjustedBy, request.ApprovedBy, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The creator of an adjusting entry cannot approve their own entry (Separation of Duties).");
            }

            // FIN-23 / R4.2: Closed accounting period blocks approval of adjusting entries
            // R4.11 q4: Use the adjustment's own date for the period check; fall back to Manila today.
            var adjDate = DateTime.TryParse(adjustment.DateRequested, out var parsedDate) ? parsedDate.Date : GetManilaToday();
            bool isPeriodClosed = await _context.AccountingPeriodCloses
                .AnyAsync(p => adjDate >= p.PeriodStart.Date && adjDate <= p.PeriodEnd.Date, cancellationToken);
            if (isPeriodClosed)
            {
                throw new InvalidOperationException("Cannot approve an adjusting entry in a closed accounting period (FIN-23 / R4.2).");
            }

            var invoice = await _context.Invoices
                .FirstOrDefaultAsync(i => i.InvoiceNo == adjustment.InvoiceNo, cancellationToken);
            if (invoice == null)
                return null;

            var beforeBalance = invoice.Balance;
            var beforeStatus  = invoice.PaymentStatus;
            var beforeTotal   = invoice.TotalAmount;

            // Mark adjustment as approved
            adjustment.Status       = "Approved";
            adjustment.ApprovedBy   = request.ApprovedBy;
            // R4.11 q4: DateApproved is Manila today (the date the HA approves), not UTC.
            adjustment.DateApproved = GetManilaTodayString();

            var adjType = adjustment.AdjustmentType.ToLower();

            // ── Apply the adjustment to the invoice ──────────────────────────────
            if (adjType == "credit" || adjType == "write-off")
            {
                if (invoice.Balance > 0m)
                {
                    invoice.CreditMemoAmount += adjustment.Amount;
                }
                else
                {
                    var client = await _context.Clients
                        .FirstOrDefaultAsync(c => c.Id == invoice.ClientId, cancellationToken);
                    if (client == null)
                        throw new InvalidOperationException($"Client '{invoice.ClientId}' for invoice '{invoice.InvoiceNo}' was not found.");

                    // C1 FR-034 / C2 FR-032: Paid-invoice credits remain client credit, never cash outflow.
                    client.AvailableCredit += adjustment.Amount;

                    // R4.5: Post Dr Cargo Claims (Sales Allowances) / Cr Unapplied Client Credits
                    await PostCreditMemoJournalEntryAsync(invoice, adjustment, cancellationToken);
                }
            }
            else if (adjType == "debit")
            {
                invoice.OtherCharges += adjustment.Amount;
            }

            // Fully recalculate all derived fields using the authoritative service
            BillingComputationService.RecalculateInvoice(invoice);
            invoice.UpdatedBy   = request.ApprovedBy;
            // R4.11 q4: LastUpdated uses Manila today for consistency.
            invoice.LastUpdated = GetManilaTodayString();

            var audit = new AuditLog
            {
                UserId     = request.ApprovedBy,
                EntityName = "PaymentAdjustment",
                EntityId   = adjustment.Id,
                Action     = "Approve Payment Adjustment",
                Details    = $"Approved {adjustment.AdjustmentType} adjustment of {adjustment.Amount:N2} " +
                             $"for invoice {adjustment.InvoiceNo}. Reason: {adjustment.Reason}.",
                BeforeValue = $"Total: {beforeTotal:N2} | Balance: {beforeBalance:N2} | Status: {beforeStatus}",
                AfterValue  = $"Total: {invoice.TotalAmount:N2} | Balance: {invoice.Balance:N2} | Status: {invoice.PaymentStatus}"
            };
            await _context.AuditLogs.AddAsync(audit, cancellationToken);

            await _context.SaveChangesAsync(cancellationToken);

            await BillingComputationService.ApplyAvailableCreditAsync(invoice.ClientId, _context, cancellationToken);
            BillingComputationService.RecalculateInvoice(invoice);

            // Sync the Billing Invoice mirror and ReceivableBalance
            await BillingComputationService.SyncBillingInvoiceAndReceivableAsync(invoice, _context, cancellationToken);

            // Sync the client's outstanding balance
            await BillingComputationService.SyncClientBalanceAsync(invoice.ClientId, _context, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            return adjustment;
        }

        private async Task PostCreditMemoJournalEntryAsync(Invoice invoice, PaymentAdjustment adjustment, CancellationToken cancellationToken)
        {
            var claimsAccount = await _context.ChartOfAccounts.FirstOrDefaultAsync(
                a => a.Name.Contains("Cargo Claims") || a.AccountCode == "4200" || a.Name.Contains("Sales Allowances"),
                cancellationToken);
            if (claimsAccount == null)
            {
                claimsAccount = new ChartOfAccount
                {
                    Id = Guid.NewGuid().ToString(),
                    AccountCode = "4200",
                    Name = "Cargo Claims (Sales Allowances)",
                    Type = "Expense"
                };
                _context.ChartOfAccounts.Add(claimsAccount);
            }

            var unappliedCreditsAccount = await _context.ChartOfAccounts.FirstOrDefaultAsync(
                a => a.Name.Contains("Unapplied Client Credits") || a.AccountCode == "2160",
                cancellationToken);
            if (unappliedCreditsAccount == null)
            {
                unappliedCreditsAccount = new ChartOfAccount
                {
                    Id = Guid.NewGuid().ToString(),
                    AccountCode = "2160",
                    Name = "Unapplied Client Credits",
                    Type = "Liability"
                };
                _context.ChartOfAccounts.Add(unappliedCreditsAccount);
            }

            var jeId = Guid.NewGuid().ToString();
            var je = new JournalEntry
            {
                Id = jeId,
                EntryNumber = $"JE-CREDITMEMO-{invoice.InvoiceNo}-{jeId[..8]}",
                InvoiceId = invoice.Id,
                // R4.11 q4: Journal entry date = adjustment's own date, never DateTime.UtcNow.
                // Parse DateRequested (a Manila calendar date string "yyyy-MM-dd"); fall back to Manila today.
                Date = DateTime.TryParse(adjustment.DateRequested, out var jeDate)
                    ? jeDate.Date
                    : GetManilaToday(),
                Description = $"Credit Memo on Paid Invoice {invoice.InvoiceNo}: Dr Cargo Claims (Sales Allowances) / Cr Unapplied Client Credits",
                Posted = true
            };
            _context.JournalEntries.Add(je);

            _context.GeneralLedgerEntries.Add(new GeneralLedgerEntry
            {
                Id = Guid.NewGuid().ToString(),
                JournalEntryId = jeId,
                AccountId = claimsAccount.Id,
                Debit = adjustment.Amount,
                Credit = 0m
            });

            _context.GeneralLedgerEntries.Add(new GeneralLedgerEntry
            {
                Id = Guid.NewGuid().ToString(),
                JournalEntryId = jeId,
                AccountId = unappliedCreditsAccount.Id,
                Debit = 0m,
                Credit = adjustment.Amount
            });
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // REJECT ADJUSTMENT — marks as rejected; NO balance change
    // ─────────────────────────────────────────────────────────────────
    public record RejectAdjustmentCommand(
        string Id,
        string RejectedBy
    ) : IRequest<PaymentAdjustment?>;

    public class RejectAdjustmentCommandHandler : IRequestHandler<RejectAdjustmentCommand, PaymentAdjustment?>
    {
        private readonly IApplicationDbContext _context;

        public RejectAdjustmentCommandHandler(IApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<PaymentAdjustment?> Handle(RejectAdjustmentCommand request, CancellationToken cancellationToken)
        {
            var adjustment = await _context.PaymentAdjustments
                .FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken);

            if (adjustment == null || adjustment.Status != "Pending")
                return null;

            adjustment.Status       = "Rejected";
            adjustment.ApprovedBy   = request.RejectedBy; // Records who rejected it
            // R4.11 q4: DateApproved is Manila today (the date the HA approves), not UTC.
            adjustment.DateApproved = GetManilaTodayString();

            var audit = new AuditLog
            {
                UserId     = request.RejectedBy,
                EntityName = "PaymentAdjustment",
                EntityId   = adjustment.Id,
                Action     = "Reject Payment Adjustment",
                Details    = $"Rejected {adjustment.AdjustmentType} adjustment of {adjustment.Amount:N2} " +
                             $"for invoice {adjustment.InvoiceNo}. Reason: {adjustment.Reason}.",
                BeforeValue = "Status: Pending",
                AfterValue  = $"Status: Rejected by {request.RejectedBy}"
            };
            await _context.AuditLogs.AddAsync(audit, cancellationToken);

            await _context.SaveChangesAsync(cancellationToken);
            return adjustment;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // DELETE ADJUSTMENT — only if still Pending (Bookkeeper or Accountant)
    // ─────────────────────────────────────────────────────────────────
    public record DeleteAdjustmentCommand(string Id, string DeletedBy) : IRequest<bool>;

    public class DeleteAdjustmentCommandHandler : IRequestHandler<DeleteAdjustmentCommand, bool>
    {
        private readonly IApplicationDbContext _context;

        public DeleteAdjustmentCommandHandler(IApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<bool> Handle(DeleteAdjustmentCommand request, CancellationToken cancellationToken)
        {
            var adjustment = await _context.PaymentAdjustments
                .FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken);

            // Only Pending adjustments can be deleted
            if (adjustment == null || adjustment.Status != "Pending")
                return false;

            var audit = new AuditLog
            {
                UserId     = request.DeletedBy,
                EntityName = "PaymentAdjustment",
                EntityId   = adjustment.Id,
                Action     = "Delete Payment Adjustment",
                Details    = $"Deleted pending {adjustment.AdjustmentType} adjustment of {adjustment.Amount:N2} " +
                             $"for invoice {adjustment.InvoiceNo}.",
                BeforeValue = "Status: Pending",
                AfterValue  = "Deleted"
            };
            await _context.AuditLogs.AddAsync(audit, cancellationToken);

            _context.PaymentAdjustments.Remove(adjustment);
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
    }
}
