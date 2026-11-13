using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using FOMS.Application.Features;
using FOMS.Application.Interfaces;
using FOMS.Application.Services;
using FOMS.Domain.Entities;
using FOMS.Infrastructure.Persistence;

namespace FOMS.Api.Controllers;

public class PaymentDto
{
    public string? Id { get; set; }
    public string? InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? InvoiceNo { get; set; }
    public string? ClientId { get; set; }
    public string? ClientName { get; set; }
    public decimal? Amount { get; set; }
    public string? PaymentMethod { get; set; }
    public string? ReferenceNumber { get; set; }
    public bool? BankConfirmed { get; set; }
    public string? ProofOfPaymentUrl { get; set; }
    public string? ProofImageUrl { get; set; }
    public string? RecordedBy { get; set; }
    public string? RecordedAt { get; set; }
    public string? DateRecorded { get; set; }
    public string? PaymentDate { get; set; }
    public string? ValidatedBy { get; set; }
    public DateTime? ValidatedAt { get; set; }
    public string? Status { get; set; }
    public string? PaymentStatus { get; set; }
    public string? Notes { get; set; }
    public string? Remarks { get; set; }
    public string? OrNumber { get; set; }
    public string? CollectorReferenceNumber { get; set; }
    public string? Flow { get; set; }
    public bool? IsUnidentified { get; set; }
}

[Authorize]
[Route("payments")]
[Route("api/payments")]
[Route("api/v1/payments")]
public class PaymentsController : ApiControllerBase
{
    private readonly IApplicationDbContext _context;
    private readonly FomsDbContext _fomsDb;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(IApplicationDbContext context, FomsDbContext fomsDb, ILogger<PaymentsController> logger)
    {
        _context = context;
        _fomsDb = fomsDb;
        _logger = logger;
    }

    private static bool IsValidProofFormat(string? proof)
    {
        if (string.IsNullOrWhiteSpace(proof)) return true;
        var lower = proof.Trim().ToLowerInvariant();
        if (lower.StartsWith("data:image/png") ||
            lower.StartsWith("data:image/jpeg") ||
            lower.StartsWith("data:image/jpg") ||
            lower.StartsWith("data:application/pdf"))
            return true;

        var clean = lower.Split('?')[0].Split('#')[0];
        var ext = System.IO.Path.GetExtension(clean);
        return ext is ".pdf" or ".jpg" or ".jpeg" or ".png";
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

        if (role.Equals("Finance Manager", StringComparison.OrdinalIgnoreCase))
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

    [HttpGet("check-reference")]
    public async Task<IActionResult> CheckReference([FromQuery] string? referenceNumber, [FromQuery] string? orNumber)
    {
        var queryRef = referenceNumber?.Trim();
        var queryOr = orNumber?.Trim();

        if (string.IsNullOrWhiteSpace(queryRef) && string.IsNullOrWhiteSpace(queryOr))
            return BadRequest(new { message = "Reference number or OR number is required." });

        var isDuplicate = await _context.Payments.AnyAsync(p =>
            (!string.IsNullOrEmpty(queryRef) && p.ReferenceNumber == queryRef) ||
            (!string.IsNullOrEmpty(queryOr) && p.OrNumber == queryOr));

        return Ok(new
        {
            isDuplicate,
            alert = isDuplicate ? "Warning: Duplicate reference / OR number detected!" : "Reference is unique.",
            message = isDuplicate ? "Duplicate payment reference detected." : "Unique payment reference."
        });
    }

    [HttpPost("upload-proof")]
    public async Task<IActionResult> UploadProof([FromForm] Microsoft.AspNetCore.Http.IFormFile? file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "No file uploaded." });

        var ext = System.IO.Path.GetExtension(file.FileName).ToLowerInvariant();
        var allowed = new[] { ".pdf", ".jpg", ".jpeg", ".png" };
        if (!allowed.Contains(ext))
            return BadRequest(new { message = "Proof of payment must be a PDF, JPG, or PNG file." });

        var uploadDir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "uploads", "proofs");
        System.IO.Directory.CreateDirectory(uploadDir);

        var fileName = $"{Guid.NewGuid():N}_{System.IO.Path.GetFileName(file.FileName)}";
        var fullPath = System.IO.Path.Combine(uploadDir, fileName);

        using (var stream = new System.IO.FileStream(fullPath, System.IO.FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var fileUrl = $"/uploads/proofs/{fileName}";
        return Ok(new { fileUrl, fileName });
    }

    public class ClaimPaymentDto
    {
        public string InvoiceId { get; set; } = string.Empty;
        public string? ClientId { get; set; }
    }

    [HttpPost("{id}/claim")]
    public async Task<IActionResult> ClaimPayment(string id, [FromBody] ClaimPaymentDto dto)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.InvoiceId))
            return BadRequest(new { message = "Invoice ID is required to claim an unidentified payment." });

        var payment = await _context.Payments.FindAsync(id) ?? await _context.Payments.FirstOrDefaultAsync(p => p.Id == id);
        if (payment == null)
            return NotFound(new { message = $"Payment {id} not found." });

        if (payment.PaymentStatus != "On Hold")
            return BadRequest(new { message = $"Only payments on hold can be claimed. Current status is {payment.PaymentStatus}." });

        var invoice = await _context.Invoices.FirstOrDefaultAsync(i => i.Id == dto.InvoiceId || i.InvoiceNo == dto.InvoiceId);
        if (invoice == null)
            return NotFound(new { message = $"Invoice '{dto.InvoiceId}' not found." });

        var user = User.FindFirst("name")?.Value ?? User.FindFirst(ClaimTypes.Name)?.Value ?? "User";

        payment.InvoiceId = invoice.Id;
        payment.InvoiceNo = invoice.InvoiceNo;
        payment.ClientId = invoice.ClientId;
        payment.ClientName = invoice.ClientName ?? string.Empty;

        if (payment.Amount < invoice.Balance)
        {
            payment.PaymentStatus = "Exception";
            payment.RejectionReason = $"Short payment: received {payment.Amount:N2}; invoice balance is {invoice.Balance:N2}.";
        }
        else if (payment.Amount > invoice.Balance)
        {
            payment.PaymentStatus = "Exception";
            payment.RejectionReason = $"Overpayment: received {payment.Amount:N2}; invoice balance is {invoice.Balance:N2}.";
        }
        else
        {
            payment.PaymentStatus = "Pending Validation";
            payment.RejectionReason = null;
        }

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = user,
            EntityName = "Payment",
            EntityId = payment.Id,
            Action = "Claim Payment",
            Details = $"Claimed unidentified payment {payment.ReferenceNumber} for invoice {invoice.InvoiceNo}. New status: {payment.PaymentStatus}.",
            LoggedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(default);
        return Ok(MapPayment(payment));
    }

    private static object MapPayment(Payment p)
    {
        var status = !string.IsNullOrEmpty(p.PaymentStatus) ? p.PaymentStatus : "Pending Validation";
        var isConfirmed = status.Equals("Validated", StringComparison.OrdinalIgnoreCase) || status.Equals("Approved", StringComparison.OrdinalIgnoreCase);

        var recDate = !string.IsNullOrWhiteSpace(p.PaymentDate) ? p.PaymentDate :
                      !string.IsNullOrWhiteSpace(p.DateRecorded) ? p.DateRecorded :
                      p.SubmittedAt.ToString("yyyy-MM-dd");

        return new
        {
            id = p.Id,
            invoiceId = p.InvoiceId,
            invoiceNumber = !string.IsNullOrEmpty(p.InvoiceNo) ? p.InvoiceNo : p.InvoiceId,
            invoiceNo = p.InvoiceNo,
            clientId = p.ClientId,
            clientName = p.ClientName,
            amount = p.Amount,
            flow = !string.IsNullOrEmpty(p.Flow) ? p.Flow : "inflow",
            paymentMethod = p.PaymentMethod,
            referenceNumber = p.ReferenceNumber,
            bankConfirmed = isConfirmed,
            proofOfPaymentUrl = p.ProofImageUrl,
            proofImageUrl = p.ProofImageUrl,
            recordedBy = p.RecordedBy,
            recordedAt = recDate,
            dateRecorded = recDate,
            paymentDate = recDate,
            validatedBy = p.ValidatedBy,
            validatedAt = p.ValidatedAt?.ToString("yyyy-MM-ddTHH:mm:ss"),
            status = status,
            paymentStatus = status,
            appliedAmount = p.AppliedAmount,
            rejectionReason = p.RejectionReason,
            exceptionResolutionReason = p.ExceptionResolutionReason,
            notes = p.Remarks,
            remarks = p.Remarks,
            orNumber = p.OrNumber
        };
    }

    [HttpGet]
    public async Task<IActionResult> GetPayments()
    {
        var payments = await _context.Payments
            .OrderByDescending(p => p.SubmittedAt)
            .ToListAsync();

        var mapped = payments.Select(MapPayment).ToList();
        return Ok(mapped);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var p = await _context.Payments.FirstOrDefaultAsync(x => x.Id == id || x.ReferenceNumber == id);
        if (p == null) return NotFound(new { message = $"Payment {id} not found." });

        return Ok(MapPayment(p));
    }

    [HttpPost]
    public async Task<IActionResult> RecordPayment([FromBody] PaymentDto dto)
    {
        if (dto == null)
            return BadRequest(new { message = "Payment details are required." });
        if (dto.Amount is null or <= 0m)
            return BadRequest(new { message = "Payment amount must be greater than zero." });

        var proofUrl = dto.ProofOfPaymentUrl ?? dto.ProofImageUrl;
        if (!IsValidProofFormat(proofUrl))
            return BadRequest(new { message = "Proof of payment must be a PDF, JPG, or PNG file." });

        var collectorOr = !string.IsNullOrWhiteSpace(dto.CollectorReferenceNumber)
            ? dto.CollectorReferenceNumber.Trim()
            : !string.IsNullOrWhiteSpace(dto.OrNumber)
                ? dto.OrNumber.Trim()
                : null;

        var referenceNumber = !string.IsNullOrWhiteSpace(dto.ReferenceNumber)
            ? dto.ReferenceNumber.Trim()
            : collectorOr;

        if (string.IsNullOrWhiteSpace(referenceNumber))
            return BadRequest(new { message = "Payment reference or collector reference number (OR) is required." });

        var method = dto.PaymentMethod?.Trim();
        if (method == null || !new[] { "Check", "Bank Transfer", "Cash" }
                .Contains(method, StringComparer.OrdinalIgnoreCase))
            return BadRequest(new { message = "Payment method must be Check, Bank Transfer, or Cash." });

        var invoiceId = dto.InvoiceId ?? dto.InvoiceNumber ?? dto.InvoiceNo;
        var isUnidentified = dto.IsUnidentified == true || string.IsNullOrWhiteSpace(invoiceId);

        Invoice? invoice = null;
        if (!isUnidentified)
        {
            invoice = await _context.Invoices.FirstOrDefaultAsync(i =>
                i.Id == invoiceId || i.InvoiceNo == invoiceId);
            if (invoice == null)
                return NotFound(new { message = "The invoice for this payment was not found." });

            if (invoice.DocumentStatus == "Void" || invoice.PaymentStatus == "Void" || invoice.DocumentStatus == InvoiceDocumentStatus.Void)
                return BadRequest(new { message = "Cannot record payment for a void invoice. Void is terminal." });
        }

        var user = User.FindFirst("name")?.Value ?? User.FindFirst(ClaimTypes.Name)?.Value ?? "User";
        var amount = dto.Amount.Value;

        var duplicateReference = await _context.Payments.AnyAsync(p =>
            p.ReferenceNumber == referenceNumber || (!string.IsNullOrEmpty(collectorOr) && p.OrNumber == collectorOr));

        string? exceptionReason = null;
        string initialStatus = "Pending Validation";

        if (isUnidentified)
        {
            initialStatus = "On Hold";
        }
        else if (duplicateReference)
        {
            initialStatus = "Exception";
            exceptionReason = $"Duplicate payment reference: {referenceNumber}.";
        }
        else if (invoice != null && amount < invoice.Balance)
        {
            initialStatus = "Exception";
            exceptionReason = $"Short payment: received {amount:N2}; invoice balance is {invoice.Balance:N2}.";
        }
        else if (invoice != null && amount > invoice.Balance)
        {
            initialStatus = "Exception";
            exceptionReason = $"Overpayment: received {amount:N2}; invoice balance is {invoice.Balance:N2}.";
        }

        var payment = new Payment
        {
            Id = Guid.NewGuid().ToString(),
            OrNumber = collectorOr ?? $"PENDING-{Guid.NewGuid().ToString("N")[..8].ToUpper()}",
            InvoiceId = invoice?.Id ?? "UNIDENTIFIED",
            InvoiceNo = invoice?.InvoiceNo ?? "UNIDENTIFIED",
            ClientId = invoice?.ClientId ?? dto.ClientId ?? "UNIDENTIFIED",
            ClientName = invoice?.ClientName ?? dto.ClientName ?? "Unidentified Client",
            PaymentDate = dto.PaymentDate ?? dto.DateRecorded ?? DateTime.UtcNow.ToString("yyyy-MM-dd"),
            Amount = amount,
            PaymentMethod = new[] { "Check", "Bank Transfer", "Cash" }
                .First(x => x.Equals(method, StringComparison.OrdinalIgnoreCase)),
            ReferenceNumber = referenceNumber,
            ProofImageUrl = proofUrl,
            Remarks = isUnidentified
                ? ((dto.Notes ?? dto.Remarks ?? "") + " [Unidentified payment - On Hold until claimed]").Trim()
                : (dto.Notes ?? dto.Remarks),
            RecordedBy = user,
            RecordedByEmployeeId = User.FindFirst("employeeNumber")?.Value ?? User.FindFirst("EmployeeNumber")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value ?? "EMP-003",
            DateRecorded = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            PaymentStatus = initialStatus,
            RejectionReason = exceptionReason,
            SubmittedAt = DateTime.UtcNow
        };

        _context.Payments.Add(payment);

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = user,
            EntityName = "Payment",
            EntityId = payment.Id,
            Action = "Record Payment",
            Details = $"Recorded payment of {payment.Amount:N2} for invoice {payment.InvoiceNo}. Ref: {payment.ReferenceNumber}. Status: {payment.PaymentStatus}. {exceptionReason}",
            LoggedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(default);

        if (exceptionReason != null)
        {
            try
            {
                _fomsDb.ExceptionRecords.Add(new FOMS.Domain.Entities.SqlServer.ExceptionRecord
                {
                    RecordType = "Payment",
                    RecordId = payment.Id,
                    Amount = payment.Amount,
                    Status = "Open",
                    Reason = exceptionReason,
                    CreatedAt = DateTime.UtcNow
                });
                await _fomsDb.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write exception record to SQL database.");
            }
        }

        // Sync with MSSQL FomsDbContext
        if (invoice != null &&
            int.TryParse(invoice.Id, out var parsedInvoiceId) &&
            int.TryParse(invoice.ClientId, out var parsedClientId))
        {
            try
            {
                _fomsDb.Payments.Add(new FOMS.Domain.Entities.SqlServer.Payment
                {
                    InvoiceId = parsedInvoiceId,
                    ClientId = parsedClientId,
                    Amount = payment.Amount,
                    PaymentMethod = payment.PaymentMethod,
                    ReferenceNumber = payment.ReferenceNumber,
                    PaymentStatus = payment.PaymentStatus,
                    SubmittedAt = DateTime.UtcNow
                });
                await _fomsDb.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "The payment was recorded but failed to synchronize with the legacy finance database.");
            }
        }

        return Ok(MapPayment(payment));
    }

    [Authorize]
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdatePayment(string id, [FromBody] PaymentDto dto)
    {
        if (dto.Status?.Equals("Approved", StringComparison.OrdinalIgnoreCase) == true ||
            dto.PaymentStatus?.Equals("Approved", StringComparison.OrdinalIgnoreCase) == true)
        {
            return await ApprovePaymentInternal(id, dto);
        }

        if (!string.IsNullOrWhiteSpace(dto.Status) || !string.IsNullOrWhiteSpace(dto.PaymentStatus))
            return BadRequest(new { message = "Payment status can only be changed through Finance Manager validation." });

        var payment = await _context.Payments.FindAsync(id) ?? await _context.Payments.FirstOrDefaultAsync(p => p.Id == id);
        if (payment == null) return NotFound(new { message = $"Payment {id} not found." });

        var user = User.FindFirst("name")?.Value ?? User.FindFirst(ClaimTypes.Name)?.Value ?? "User";

        if (!string.IsNullOrEmpty(dto.Notes) || !string.IsNullOrEmpty(dto.Remarks))
            payment.Remarks = dto.Notes ?? dto.Remarks;

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = user,
            EntityName = "Payment",
            EntityId = payment.Id,
            Action = "Update Payment",
            Details = $"Updated payment {payment.ReferenceNumber}. Status: {payment.PaymentStatus}.",
            LoggedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(default);

        return Ok(MapPayment(payment));
    }

    [Authorize]
    [HttpPost("{id}/approve")]
    public async Task<IActionResult> ApprovePayment(string id, [FromBody] PaymentDto? dto = null)
    {
        return await ApprovePaymentInternal(id, dto);
    }

    [Authorize]
    [HttpPost("{id}/{actionName}")]
    public async Task<IActionResult> ActionOnPayment(string id, string actionName, [FromBody] PaymentDto? dto)
    {
        var payment = await _context.Payments.FindAsync(id) ?? await _context.Payments.FirstOrDefaultAsync(p => p.Id == id);
        if (payment == null) return NotFound(new { message = $"Payment {id} not found." });

        var (isAuthorized, validatorRole, delegateNote) = await CheckValidationAuthorityAsync();
        if (!isAuthorized)
            return StatusCode(403, new { message = "Only the Finance Manager (or configured, logged delegate) is authorized to perform payment actions." });

        var user = User.FindFirst("name")?.Value ?? User.FindFirst(ClaimTypes.Name)?.Value ?? "User";
        var empId = User.FindFirst("employeeNumber")?.Value
            ?? User.FindFirst("EmployeeNumber")?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? user;
        var normAction = actionName.ToLowerInvariant();

        // Separation of Duties: Submitter cannot validate/approve/reject their own payment
        if (!string.IsNullOrWhiteSpace(payment.RecordedBy) &&
            (payment.RecordedBy.Equals(user, StringComparison.OrdinalIgnoreCase) ||
             payment.RecordedBy.Equals(empId, StringComparison.OrdinalIgnoreCase)))
        {
            return StatusCode(403, new { message = "Separation of duties violation: Submitter cannot validate or approve their own payment." });
        }

        if (normAction is "approve")
        {
            return await ApprovePaymentInternal(id, dto);
        }

        if (normAction is "verify" or "validate")
        {
            if (payment.PaymentStatus != "Pending Validation" && payment.PaymentStatus != "Pending Finance Validation")
                return BadRequest(new { message = "Only payments pending validation can be validated." });

            var inv = await _context.Invoices.FirstOrDefaultAsync(i => i.Id == payment.InvoiceId || i.InvoiceNo == payment.InvoiceNo);
            if (inv == null)
                return NotFound(new { message = $"Invoice '{payment.InvoiceId}' not found." });

            BillingComputationService.RecalculateInvoice(inv);
            var isOverpaymentApproved = !string.IsNullOrWhiteSpace(payment.ExceptionResolutionReason) &&
                payment.ExceptionResolutionReason.Contains("unapplied credit", StringComparison.OrdinalIgnoreCase);

            // Rule: Invoice becomes Paid only if validated payment equals invoice amount exactly
            if (payment.Amount != inv.Balance && !isOverpaymentApproved)
                return BadRequest(new { message = $"Invoice becomes Paid only if validated payment equals invoice amount exactly. Payment is {payment.Amount:N2}, invoice balance is {inv.Balance:N2}." });

            var appliedAmount = Math.Min(payment.Amount, inv.Balance);
            payment.AppliedAmount = appliedAmount;
            inv.AmountPaid += appliedAmount;
            BillingComputationService.RecalculateInvoice(inv);
            inv.UpdatedBy = user;
            inv.PaymentValidationStatus = "Validated";
            inv.PaymentStatus = inv.Balance <= 0 ? "Paid" : "Unpaid";

            payment.PaymentStatus = "Validated";
            payment.ValidatedBy = user + (delegateNote != null ? $" ({validatorRole})" : "");
            payment.ValidatedAt = DateTime.UtcNow;
            if (!string.IsNullOrEmpty(dto?.Remarks ?? dto?.Notes))
                payment.Remarks = dto?.Remarks ?? dto?.Notes;

            var unappliedAmount = payment.Amount - appliedAmount;
            if (unappliedAmount > 0m)
            {
                var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == payment.ClientId);
                if (client != null)
                {
                    client.AvailableCredit += unappliedAmount;
                    await BillingComputationService.ApplyAvailableCreditAsync(client.Id, _context);
                    BillingComputationService.RecalculateInvoice(inv);
                }
            }

            // Sync AR
            var receivable = await _context.ReceivableBalances.FirstOrDefaultAsync(r => r.InvoiceId == inv.Id);
            if (receivable != null)
            {
                receivable.BalanceAmount = inv.Balance;
                receivable.PaidAmount += appliedAmount;
                receivable.LastPaymentDate = DateTime.UtcNow;
                receivable.Status = inv.Balance <= 0 ? "Paid" : "Unpaid";
            }

            // Sync Cash Flow
            _context.CashFlowTransactions.Add(new CashFlowTransaction
            {
                Date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                Type = "Inflow",
                Category = "Accounts Receivable Collection",
                Amount = payment.Amount,
                ReferenceNo = payment.OrNumber,
                Description = $"Validated payment for Invoice {inv.InvoiceNo}. Ref: {payment.ReferenceNumber}. Validator: {validatorRole}."
            });

            try
            {
                int? parsedPaymentId = int.TryParse(payment.Id, out var pid) ? pid : null;
                int? parsedClientId = int.TryParse(inv.ClientId, out var cid) ? cid : null;
                _fomsDb.CashInflows.Add(new FOMS.Domain.Entities.SqlServer.CashInflow
                {
                    PaymentId = parsedPaymentId,
                    ClientId = parsedClientId,
                    Amount = payment.Amount,
                    Source = inv.ClientName ?? "Client Collection",
                    ReferenceNumber = payment.OrNumber,
                    Status = "Completed",
                    TransactionDate = DateTime.UtcNow
                });
                await _fomsDb.SaveChangesAsync();
            }
            catch { }
        }
        else if (normAction is "resolve-exception")
        {
            var reason = dto?.Remarks ?? dto?.Notes;
            if (string.IsNullOrWhiteSpace(reason))
                return BadRequest(new { message = "A resolution reason is required." });

            var res = await Mediator.Send(new PaymentFeatures.ResolvePaymentExceptionCommand(
                payment.Id,
                dto?.Status ?? "resolve",
                reason,
                $"{user} ({validatorRole})"));

            if (res == null)
                return Conflict(new { message = "The payment is not an unresolved exception." });

            return Ok(MapPayment(res));
        }
        else if (normAction is "reject")
        {
            payment.PaymentStatus = "Rejected";
            payment.RejectedAt = DateTime.UtcNow;
            payment.RejectedBy = user;
            payment.RejectionReason = dto?.Remarks ?? dto?.Notes ?? "Payment rejected by reviewer.";
        }

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = user,
            EntityName = "Payment",
            EntityId = payment.Id,
            Action = $"Payment {actionName}",
            Details = $"Payment {payment.ReferenceNumber} action {actionName} -> {payment.PaymentStatus}. Actor: {validatorRole}.",
            LoggedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(default);
        if (payment.InvoiceId != "UNIDENTIFIED" && !string.IsNullOrEmpty(payment.ClientId))
        {
            await BillingComputationService.SyncClientBalanceAsync(payment.ClientId, _context, default);
            await _context.SaveChangesAsync(default);
        }

        return Ok(MapPayment(payment));
    }

    [Authorize]
    private async Task<IActionResult> ApprovePaymentInternal(string id, PaymentDto? dto)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value
            ?? User.FindFirst("role")?.Value;

        if (string.IsNullOrWhiteSpace(role) || (!role.Equals("Finance Manager", StringComparison.OrdinalIgnoreCase) && !role.Equals("FINANCE_MANAGER", StringComparison.OrdinalIgnoreCase)))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Only the Finance Manager is authorized to perform final approval on payments." });
        }

        var approverEmpId = User.FindFirst("employeeNumber")?.Value
            ?? User.FindFirst("EmployeeNumber")?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        var user = User.FindFirst("name")?.Value
            ?? User.FindFirst(ClaimTypes.Name)?.Value
            ?? approverEmpId
            ?? "Finance Manager";

        var payment = await _context.Payments.FindAsync(id) ?? await _context.Payments.FirstOrDefaultAsync(p => p.Id == id);
        if (payment == null) return NotFound(new { message = $"Payment {id} not found." });

        if (payment.PaymentStatus.Equals("Approved", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(MapPayment(payment));
        }

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

        if (payment.PaymentStatus is "Rejected" or "Returned")
        {
            return BadRequest(new { message = $"Payment in status '{payment.PaymentStatus}' cannot be approved." });
        }

        var inv = await _context.Invoices.FirstOrDefaultAsync(i => i.Id == payment.InvoiceId || i.InvoiceNo == payment.InvoiceNo);
        if (inv == null)
            return NotFound(new { message = $"Invoice '{payment.InvoiceId}' not found." });

        if (inv.DocumentStatus == "Void" || inv.PaymentStatus == "Void" || inv.DocumentStatus == InvoiceDocumentStatus.Void)
        {
            return BadRequest(new { message = "Cannot approve payment for a void invoice. Void is terminal." });
        }

        BillingComputationService.RecalculateInvoice(inv);

        if (payment.Amount > inv.Balance)
        {
            return BadRequest(new { message = $"Overpayment rejected: payment amount {payment.Amount:N2} exceeds invoice balance {inv.Balance:N2}." });
        }

        var appliedAmount = payment.Amount;
        payment.AppliedAmount = appliedAmount;
        inv.AmountPaid += appliedAmount;
        BillingComputationService.RecalculateInvoice(inv);
        inv.UpdatedBy = user;
        inv.PaymentValidationStatus = "Validated";
        inv.PaymentStatus = inv.Balance <= 0 ? "Paid" : "Unpaid";

        payment.PaymentStatus = "Approved";
        payment.ValidatedBy = user;
        payment.ValidatedAt = DateTime.UtcNow;
        if (!string.IsNullOrEmpty(dto?.Remarks ?? dto?.Notes))
            payment.Remarks = dto?.Remarks ?? dto?.Notes;

        var orNum = (!string.IsNullOrWhiteSpace(payment.OrNumber) && !payment.OrNumber.StartsWith("PENDING-"))
            ? payment.OrNumber
            : $"OR-{DateTime.UtcNow:yyyyMMdd}-{payment.Id[..Math.Min(6, payment.Id.Length)].ToUpper()}";
        payment.OrNumber = orNum;

        var existingReceipt = await _context.OfficialReceipts.FirstOrDefaultAsync(r => r.PaymentId == payment.Id);
        if (existingReceipt == null)
        {
            var paymentCollection = new PaymentCollection
            {
                InvoiceId = inv.Id,
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

        var receivable = await _context.ReceivableBalances.FirstOrDefaultAsync(r => r.InvoiceId == inv.Id);
        if (receivable != null)
        {
            receivable.BalanceAmount = inv.Balance;
            receivable.PaidAmount += appliedAmount;
            receivable.LastPaymentDate = DateTime.UtcNow;
            receivable.Status = inv.Balance <= 0 ? "Paid" : "Unpaid";
        }

        _context.CashFlowTransactions.Add(new CashFlowTransaction
        {
            Date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            Type = "Inflow",
            Category = "Accounts Receivable Collection",
            Amount = payment.Amount,
            ReferenceNo = payment.OrNumber,
            Description = $"Approved payment for Invoice {inv.InvoiceNo}. Ref: {payment.ReferenceNumber}. Approver: {user}."
        });

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = user,
            EntityName = "Payment",
            EntityId = payment.Id,
            Action = "Approve Payment",
            Details = $"Approved payment {payment.ReferenceNumber} -> Approved. Actor: {user}.",
            LoggedAt = DateTime.UtcNow
        });

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

        if (payment.InvoiceId != "UNIDENTIFIED" && !string.IsNullOrEmpty(payment.ClientId))
        {
            await BillingComputationService.SyncClientBalanceAsync(payment.ClientId, _context, default);
            await _context.SaveChangesAsync(default);
        }

        return Ok(MapPayment(payment));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeletePayment(string id)
    {
        var user = User.FindFirst("name")?.Value ?? User.FindFirst(ClaimTypes.Name)?.Value ?? "Unknown User";
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "Unknown Role";
        var result = await Mediator.Send(new PaymentFeatures.DeletePaymentCommand(id, user, role));
        if (!result) return NotFound();
        return Ok(new { success = true });
    }
}
