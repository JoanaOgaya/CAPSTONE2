using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FOMS.Domain.Entities.SqlServer;
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
    public string? PaymentDate { get; set; }
    public string? DateRecorded { get; set; }
    public string? ProofOfPaymentUrl { get; set; }
    public string? ProofImageUrl { get; set; }
    public string? Status { get; set; }
    public string? PaymentStatus { get; set; }
    public string? Notes { get; set; }
    public string? Remarks { get; set; }
    public string? RecordedBy { get; set; }
    public string? OrNumber { get; set; }
    public string? Flow { get; set; }
}

[Authorize]
[Route("payments")]
[Route("api/payments")]
[Route("api/v1/payments")]
public class PaymentsController : ApiControllerBase
{
    private readonly FomsDbContext _fomsDb;

    public PaymentsController(FomsDbContext fomsDb)
    {
        _fomsDb = fomsDb;
    }

    private (string role, string? clientId, string username) GetCaller()
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value
            ?? User.FindFirst("role")?.Value
            ?? string.Empty;
        var clientId = User.FindFirst("client_id")?.Value;
        var user = User.FindFirst("name")?.Value
            ?? User.FindFirst(ClaimTypes.Name)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? "User";
        return (role, clientId, user);
    }

    private static object MapPayment(Payment p)
    {
        var status = !string.IsNullOrEmpty(p.PaymentStatus) ? p.PaymentStatus : "Pending Validation";
        var isConfirmed = status.Equals("Validated", StringComparison.OrdinalIgnoreCase) || status.Equals("Approved", StringComparison.OrdinalIgnoreCase);

        var recDate = !string.IsNullOrWhiteSpace(p.PaymentDate) ? p.PaymentDate :
                      !string.IsNullOrWhiteSpace(p.DateRecorded) ? p.DateRecorded :
                      (p.SubmittedAt ?? DateTime.UtcNow).ToString("yyyy-MM-dd");

        return new
        {
            id = p.Id,
            invoiceId = p.InvoiceIdStr ?? p.InvoiceId?.ToString(),
            invoiceNumber = !string.IsNullOrEmpty(p.InvoiceNo) ? p.InvoiceNo : (p.InvoiceIdStr ?? p.InvoiceId?.ToString()),
            invoiceNo = p.InvoiceNo,
            clientId = p.ClientIdStr ?? p.ClientId?.ToString(),
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
            notes = p.Remarks,
            remarks = p.Remarks
        };
    }

    [HttpGet]
    public async Task<IActionResult> GetPayments()
    {
        var (role, clientIdClaim, _) = GetCaller();

        var query = _fomsDb.Payments.AsNoTracking().AsQueryable();

        // IAM-06: Client isolation
        if (string.Equals(role, "Client", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "CLIENT", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(clientIdClaim))
                return Forbid("No client association found in token.");

            query = query.Where(p => p.ClientIdStr == clientIdClaim);
        }

        var payments = await query.OrderByDescending(p => p.SubmittedAt).ToListAsync();
        var mapped = payments.Select(MapPayment).ToList();
        return Ok(mapped);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var (role, clientIdClaim, _) = GetCaller();

        var p = await _fomsDb.Payments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id || x.ReferenceNumber == id);
        if (p == null) return NotFound(new { message = $"Payment {id} not found." });

        if (string.Equals(role, "Client", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "CLIENT", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(clientIdClaim) || p.ClientIdStr != clientIdClaim)
            {
                return Forbid("Access denied to payment record.");
            }
        }

        return Ok(MapPayment(p));
    }

    [HttpPost]
    public async Task<IActionResult> RecordPayment([FromBody] PaymentDto dto)
    {
        var (role, clientIdClaim, user) = GetCaller();

        if (string.Equals(role, "Client", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "CLIENT", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(clientIdClaim))
                return Forbid("No client association found in token.");
            dto.ClientId = clientIdClaim;
        }

        var invoice = await _fomsDb.Invoices.FirstOrDefaultAsync(i => i.Id == dto.InvoiceId || i.InvoiceNo == dto.InvoiceNumber || i.InvoiceNo == dto.InvoiceId);
        var invId = invoice?.Id ?? dto.InvoiceId ?? Guid.NewGuid().ToString();
        var invNo = invoice?.InvoiceNo ?? dto.InvoiceNumber ?? dto.InvoiceNo ?? $"INV-{DateTime.UtcNow:yyyyMMdd}";
        var clientId = invoice?.ClientIdStr ?? dto.ClientId ?? "1";
        var clientName = invoice?.ClientName ?? dto.ClientName ?? "Client";

        var payment = new Payment
        {
            Id = Guid.NewGuid().ToString(),
            InvoiceIdStr = invId,
            InvoiceNo = invNo,
            ClientIdStr = clientId,
            ClientId = int.TryParse(clientId, out var parsedCId) ? parsedCId : null,
            ClientName = clientName,
            PaymentDate = dto.PaymentDate ?? dto.DateRecorded ?? DateTime.UtcNow.ToString("yyyy-MM-dd"),
            Amount = dto.Amount ?? 0m,
            Flow = dto.Flow ?? "inflow",
            PaymentMethod = dto.PaymentMethod ?? "Bank Transfer",
            ReferenceNumber = dto.ReferenceNumber ?? Guid.NewGuid().ToString("N")[..8].ToUpper(),
            ProofImageUrl = dto.ProofOfPaymentUrl ?? dto.ProofImageUrl,
            Remarks = dto.Notes ?? dto.Remarks ?? "Payment recorded",
            RecordedBy = user,
            DateRecorded = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            PaymentStatus = dto.Status ?? dto.PaymentStatus ?? "Pending Finance Validation",
            SubmittedAt = DateTime.UtcNow
        };

        _fomsDb.Payments.Add(payment);

        if (invoice != null && (payment.PaymentStatus == "Validated" || payment.PaymentStatus == "Approved"))
        {
            invoice.AmountPaid += payment.Amount;
            invoice.Balance = Math.Max(0, invoice.TotalAmount - invoice.AmountPaid);
            if (invoice.Balance <= 0)
            {
                invoice.PaymentStatus = "Paid";
                invoice.PaymentValidationStatus = "Paid";
            }
        }

        _fomsDb.AuditLogs.Add(new AuditLog
        {
            role = role,
            action = "Record Payment",
            module = "Payments",
            affected_record_type = "Payment",
            before_value = null,
            after_value = JsonSerializer.Serialize(new { payment.ReferenceNumber, payment.Amount, payment.PaymentStatus }),
            remarks = $"Recorded payment of ₱{payment.Amount:N2} for invoice {payment.InvoiceNo}. Ref: {payment.ReferenceNumber}.",
            created_at = DateTime.UtcNow,
            internal_retention_until = DateTime.UtcNow.AddYears(5)
        });

        await _fomsDb.SaveChangesAsync();

        return Ok(MapPayment(payment));
    }

    private async Task<(bool allowed, FinanceManagerDelegate? delegateRecord, string? errorMessage)> CheckPaymentValidationAuthorizationAsync(string role, string userId)
    {
        bool isFm = string.Equals(role, "Financial Manager", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(role, "FINANCIAL_MANAGER", StringComparison.OrdinalIgnoreCase);

        if (isFm) return (true, null, null);

        var now = DateTime.UtcNow;
        var activeDelegate = await _fomsDb.FinanceManagerDelegates
            .FirstOrDefaultAsync(d => d.DelegateUserId == userId && d.IsActive && d.ValidFrom <= now && d.ValidUntil >= now);

        if (activeDelegate != null)
        {
            return (true, activeDelegate, null);
        }

        var anyDelegate = await _fomsDb.FinanceManagerDelegates
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefaultAsync(d => d.DelegateUserId == userId);

        if (anyDelegate != null)
        {
            if (!anyDelegate.IsActive)
                return (false, null, $"Delegation for user '{userId}' has been revoked.");
            if (now > anyDelegate.ValidUntil)
                return (false, null, $"Delegation for user '{userId}' expired on {anyDelegate.ValidUntil:yyyy-MM-dd HH:mm:ss} UTC.");
            if (now < anyDelegate.ValidFrom)
                return (false, null, $"Delegation for user '{userId}' is not effective until {anyDelegate.ValidFrom:yyyy-MM-dd HH:mm:ss} UTC.");
        }

        return (false, null, "PAY-03 VIOLATION: Only the Financial Manager or a configured active delegate may validate payments.");
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdatePayment(string id, [FromBody] PaymentDto dto)
    {
        var (role, _, user) = GetCaller();

        var payment = await _fomsDb.Payments.FirstOrDefaultAsync(p => p.Id == id);
        if (payment == null) return NotFound(new { message = $"Payment {id} not found." });

        var beforeVal = JsonSerializer.Serialize(new { payment.ReferenceNumber, payment.Amount, payment.PaymentStatus });

        var newStatus = dto.Status ?? dto.PaymentStatus;
        FinanceManagerDelegate? activeDelegate = null;
        if (!string.IsNullOrEmpty(newStatus))
        {
            if (newStatus.Equals("Validated", StringComparison.OrdinalIgnoreCase) || newStatus.Equals("Approved", StringComparison.OrdinalIgnoreCase))
            {
                var (allowed, delRecord, err) = await CheckPaymentValidationAuthorizationAsync(role, user);
                if (!allowed)
                {
                    return StatusCode(403, new { message = err });
                }
                activeDelegate = delRecord;

                var validatorName = activeDelegate != null ? $"{user} (Delegate: {activeDelegate.DelegateName})" : user;
                payment.ValidatedBy = validatorName;
                payment.ValidatedAt = DateTime.UtcNow;

                var inv = await _fomsDb.Invoices.FirstOrDefaultAsync(i => i.Id == payment.InvoiceIdStr || i.InvoiceNo == payment.InvoiceNo);
                if (inv != null)
                {
                    inv.AmountPaid += payment.Amount;
                    inv.Balance = Math.Max(0, inv.TotalAmount - inv.AmountPaid);
                    if (inv.Balance <= 0)
                    {
                        inv.PaymentStatus = "Paid";
                        inv.PaymentValidationStatus = "Paid";
                    }
                }
            }
            payment.PaymentStatus = newStatus;
        }

        if (!string.IsNullOrEmpty(dto.ReferenceNumber))
            payment.ReferenceNumber = dto.ReferenceNumber;

        if (!string.IsNullOrEmpty(dto.Notes) || !string.IsNullOrEmpty(dto.Remarks))
            payment.Remarks = dto.Notes ?? dto.Remarks;

        _fomsDb.AuditLogs.Add(new AuditLog
        {
            role = activeDelegate != null ? $"{role} (FM Delegate)" : role,
            user_id = int.TryParse(user, out var u) ? u : null,
            action = "Update Payment",
            module = "Payments",
            affected_record_type = "Payment",
            before_value = beforeVal,
            after_value = JsonSerializer.Serialize(new { payment.ReferenceNumber, payment.Amount, payment.PaymentStatus, payment.ValidatedBy }),
            remarks = activeDelegate != null
                ? $"Payment {payment.ReferenceNumber} updated to {payment.PaymentStatus} by delegate '{user}' under delegation #{activeDelegate.DelegateId}."
                : $"Updated payment {payment.ReferenceNumber}. Status: {payment.PaymentStatus}.",
            created_at = DateTime.UtcNow,
            internal_retention_until = DateTime.UtcNow.AddYears(5)
        });

        await _fomsDb.SaveChangesAsync();

        return Ok(MapPayment(payment));
    }

    [HttpPost("{id}/{actionName}")]
    public async Task<IActionResult> ActionOnPayment(string id, string actionName, [FromBody] PaymentDto? dto)
    {
        var (role, _, user) = GetCaller();

        var payment = await _fomsDb.Payments.FirstOrDefaultAsync(p => p.Id == id);
        if (payment == null) return NotFound(new { message = $"Payment {id} not found." });

        var beforeVal = JsonSerializer.Serialize(new { payment.ReferenceNumber, payment.Amount, payment.PaymentStatus });

        var newStatus = actionName.ToLower() switch
        {
            "approve" or "verify" or "validate" => "Validated",
            "reject" => "Rejected",
            _ => payment.PaymentStatus
        };

        FinanceManagerDelegate? activeDelegate = null;
        if (newStatus == "Validated")
        {
            var (allowed, delRecord, err) = await CheckPaymentValidationAuthorizationAsync(role, user);
            if (!allowed)
            {
                return StatusCode(403, new { message = err });
            }
            activeDelegate = delRecord;
        }

        var validatorName = activeDelegate != null ? $"{user} (Delegate: {activeDelegate.DelegateName})" : user;
        payment.PaymentStatus = newStatus;
        payment.ValidatedBy = validatorName;
        payment.ValidatedAt = DateTime.UtcNow;

        if (actionName.Equals("reject", StringComparison.OrdinalIgnoreCase))
        {
            payment.RejectedAt = DateTime.UtcNow;
            payment.RejectedBy = user;
            payment.RejectionReason = dto?.Remarks ?? dto?.Notes ?? "Payment rejected by reviewer.";
        }

        var inv = await _fomsDb.Invoices.FirstOrDefaultAsync(i => i.Id == payment.InvoiceIdStr || i.InvoiceNo == payment.InvoiceNo);
        if (inv != null && newStatus == "Validated")
        {
            inv.AmountPaid += payment.Amount;
            inv.Balance = Math.Max(0, inv.TotalAmount - inv.AmountPaid);
            if (inv.Balance <= 0)
            {
                inv.PaymentStatus = "Paid";
                inv.PaymentValidationStatus = "Paid";
            }
        }

        _fomsDb.AuditLogs.Add(new AuditLog
        {
            role = activeDelegate != null ? $"{role} (FM Delegate)" : role,
            user_id = int.TryParse(user, out var u) ? u : null,
            action = $"Payment {actionName}",
            module = "Payments",
            affected_record_type = "Payment",
            before_value = beforeVal,
            after_value = JsonSerializer.Serialize(new { payment.ReferenceNumber, payment.Amount, payment.PaymentStatus, payment.ValidatedBy }),
            remarks = activeDelegate != null
                ? $"Payment {payment.ReferenceNumber} validated by delegate '{user}' under delegation #{activeDelegate.DelegateId}."
                : $"Payment {payment.ReferenceNumber} action {actionName} -> {newStatus} by '{user}'.",
            created_at = DateTime.UtcNow,
            internal_retention_until = DateTime.UtcNow.AddYears(5)
        });

        await _fomsDb.SaveChangesAsync();

        return Ok(MapPayment(payment));
    }
}
