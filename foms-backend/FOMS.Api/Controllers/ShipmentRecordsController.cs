using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FOMS.Application.Interfaces;
using FOMS.Domain.Entities;
using FOMS.Domain.Entities.SqlServer;
using FOMS.Infrastructure.Persistence;

namespace FOMS.Api.Controllers;

public class ManualWaybillDto
{
    public string WaybillNumber { get; set; } = string.Empty;
    public int? ClientId { get; set; }
    public string? ClientCode { get; set; }
    public string? Origin { get; set; }
    public string? Destination { get; set; }
    public string? DeliveryArea { get; set; }
    public decimal? ActualWeight { get; set; }
    public decimal? DeclaredValue { get; set; }
    public string? DeliveryStatus { get; set; }
    public string? PodStatus { get; set; }
    public bool IsException { get; set; } = false;
    public string? ExceptionReason { get; set; }
    public bool ClientSigned { get; set; } = false;
    public bool AccountingCopyMatched { get; set; } = false;
    public string? CtcDocumentUrl { get; set; }
    public string? CtcReason { get; set; }
    public string? CtcApprovedBy { get; set; }
}

public class CtcApprovalDto
{
    public string? CtcDocumentUrl { get; set; }
    public string? DocumentUrl { get; set; }
    public string? Reason { get; set; }
    public string? CtcReason { get; set; }
    public string? Approver { get; set; }
    public string? ApproverName { get; set; }
}

public class PodHoldDto
{
    public string? Reason { get; set; }
}

[Authorize]
[Route("api/shipment-records")]
[Route("api/v1/shipment-records")]
[Route("shipment-records")]
[Route("api/waybills")]
[Route("api/v1/waybills")]
[Route("waybills")]
public class ShipmentRecordsController : ApiControllerBase
{
    private readonly IApplicationDbContext _context;
    private readonly FomsDbContext _fomsDb;

    public ShipmentRecordsController(IApplicationDbContext context, FomsDbContext fomsDb)
    {
        _context = context;
        _fomsDb = fomsDb;
    }

    private static string NormalizePodStatus(string? podStatus, string? deliveryStatus, bool clientSigned, bool accountingMatched, bool hasCtc)
    {
        if (hasCtc || string.Equals(podStatus, "CTC", StringComparison.OrdinalIgnoreCase))
            return "CTC";

        if (string.Equals(podStatus, "Rejected", StringComparison.OrdinalIgnoreCase))
            return "Rejected";

        if (string.Equals(podStatus, "On Hold", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(podStatus, "Hold", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(podStatus, "Failed", StringComparison.OrdinalIgnoreCase) ||
            (deliveryStatus != null && deliveryStatus.Contains("Failed", StringComparison.OrdinalIgnoreCase)))
            return "On Hold";

        if (string.Equals(podStatus, "Validated", StringComparison.OrdinalIgnoreCase))
        {
            // Validated requires both original client-signed POD and Accounting copy matched
            return (clientSigned && accountingMatched) ? "Validated" : "On Hold";
        }

        return "Pending";
    }

    [HttpGet]
    public async Task<IActionResult> GetShipments([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var waybills = await _fomsDb.Waybills.AsNoTracking().ToListAsync();
        var clients = await _fomsDb.Clients.AsNoTracking().ToListAsync();
        var pods = await _fomsDb.ProofOfDeliveries.AsNoTracking().ToListAsync();

        var items = waybills.Select(w =>
        {
            var c = clients.FirstOrDefault(cl => cl.ClientId == w.ClientId);
            var pod = pods.FirstOrDefault(p => p.WaybillId == w.WaybillId);
            string clientCode = c?.ClientCode ?? (w.ClientId == 1 ? "CA-001" : w.ClientId == 2 ? "CA-002" : w.ClientId == 3 ? "CA-003" : w.ClientId == 4 ? "CA-004" : w.ClientId.ToString());

            var normPodStatus = NormalizePodStatus(w.PodStatus, w.DeliveryStatus, w.ClientSigned, w.AccountingCopyMatched, w.HasCtc);
            bool isHold = normPodStatus == "On Hold";

            return new
            {
                id = w.WaybillId.ToString(),
                waybillNumber = w.WaybillNumber,
                trackingNumber = w.WaybillNumber,
                clientId = clientCode,
                clientCode = clientCode,
                clientName = c?.CompanyName ?? (clientCode == "LZD-001" || clientCode == "CA-001" ? "Lazada Philippines" : clientCode == "SHP-001" || clientCode == "CA-002" ? "Shopee Philippines" : clientCode == "TKT-001" || clientCode == "CA-003" ? "TikTok Shop Philippines" : clientCode == "ZAL-001" || clientCode == "CA-004" ? "Zalora Philippines" : $"Client {w.ClientId}"),
                origin = w.Origin ?? "Manila Hub",
                destination = w.Destination ?? "Customer Address",
                deliveryArea = w.DeliveryArea ?? "NCR",
                destinationArea = w.DeliveryArea ?? "NCR",
                actualWeight = w.ActualWeight ?? 1m,
                itemWeight = w.ActualWeight ?? 1m,
                volumeWeight = 1m,
                chargeableWeight = w.ActualWeight ?? 1m,
                status = normPodStatus,
                deliveryStatus = w.DeliveryStatus ?? (isHold ? "Failed Delivery" : "Delivered"),
                podStatus = normPodStatus,
                clientSigned = w.ClientSigned,
                accountingCopyMatched = w.AccountingCopyMatched,
                hasOriginalPOD = w.ClientSigned && w.AccountingCopyMatched,
                hasApprovedCTC = w.HasCtc || normPodStatus == "CTC",
                hasCtc = w.HasCtc || normPodStatus == "CTC",
                ctcDocumentUrl = w.CtcDocumentUrl,
                ctcReason = w.CtcReason,
                ctcApprovedBy = w.CtcApprovedBy,
                ctcApprovedAt = w.CtcApprovedAt?.ToString("yyyy-MM-ddTHH:mm:ss"),
                isHold = isHold,
                holdReason = w.HoldReason ?? (isHold ? "Missing counterpart copy or delivery failure" : null),
                failureReason = w.FailureReason ?? w.HoldReason,
                isDuplicateFlagged = w.IsDuplicateFlagged,
                isManualEntry = w.IsManualEntry,
                isException = w.IsException,
                exceptionReason = w.ExceptionReason,
                isBilled = w.IsBilled,
                invoiceId = w.InvoiceNo ?? w.InvoiceId?.ToString(),
                invoiceNo = w.InvoiceNo,
                date = (w.CreatedAt ?? DateTime.UtcNow).ToString("yyyy-MM-dd"),
                deliveryDate = (w.DeliveredAt ?? w.CreatedAt ?? DateTime.UtcNow).ToString("yyyy-MM-dd"),
                podUrl = pod?.SignatureImageUrl ?? w.PodReference ?? "https://speedex.ph/pod/sample.jpg",
                podImageUrl = pod?.SignatureImageUrl ?? w.PodReference ?? "https://speedex.ph/pod/sample.jpg",
                receiverName = pod?.ReceiverName ?? w.RecipientName ?? "Recipient Verified",
                encodedBy = w.IsManualEntry ? "Manual Entry (Exception)" : "DMS Integration",
                encodedAt = (w.CreatedAt ?? DateTime.UtcNow).ToString("yyyy-MM-ddTHH:mm:ss"),
                createdAt = (w.CreatedAt ?? DateTime.UtcNow).ToString("yyyy-MM-ddTHH:mm:ss")
            };
        }).ToList();

        return Ok(items);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var w = await _fomsDb.Waybills.FindAsync(id);
        if (w == null) return NotFound(new { message = "Waybill not found" });

        var c = await _fomsDb.Clients.FindAsync(w.ClientId);
        var normPodStatus = NormalizePodStatus(w.PodStatus, w.DeliveryStatus, w.ClientSigned, w.AccountingCopyMatched, w.HasCtc);

        return Ok(new
        {
            id = w.WaybillId.ToString(),
            waybillNumber = w.WaybillNumber,
            trackingNumber = w.WaybillNumber,
            clientId = w.ClientId.ToString(),
            clientName = c?.CompanyName ?? $"Client {w.ClientId}",
            origin = w.Origin,
            destination = w.Destination,
            deliveryArea = w.DeliveryArea,
            actualWeight = w.ActualWeight,
            status = normPodStatus,
            deliveryStatus = w.DeliveryStatus,
            podStatus = normPodStatus,
            clientSigned = w.ClientSigned,
            accountingCopyMatched = w.AccountingCopyMatched,
            hasOriginalPOD = w.ClientSigned && w.AccountingCopyMatched,
            hasApprovedCTC = w.HasCtc || normPodStatus == "CTC",
            hasCtc = w.HasCtc || normPodStatus == "CTC",
            ctcDocumentUrl = w.CtcDocumentUrl,
            ctcReason = w.CtcReason,
            ctcApprovedBy = w.CtcApprovedBy,
            ctcApprovedAt = w.CtcApprovedAt?.ToString("yyyy-MM-ddTHH:mm:ss"),
            isHold = normPodStatus == "On Hold",
            holdReason = w.HoldReason,
            isDuplicateFlagged = w.IsDuplicateFlagged,
            isManualEntry = w.IsManualEntry,
            isException = w.IsException,
            exceptionReason = w.ExceptionReason,
            isBilled = w.IsBilled,
            invoiceId = w.InvoiceNo ?? w.InvoiceId?.ToString(),
            invoiceNo = w.InvoiceNo
        });
    }

    /// <summary>
    /// Requirement: Records arrive from DMS (manual entry only for exceptions).
    /// Alert and block billing on duplicate waybill number (at entry and on receipt).
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ManualWaybillDto dto)
    {
        if (dto == null)
            return BadRequest(new { message = "Waybill payload is required." });

        // Requirement: Records arrive from DMS (manual entry only for exceptions).
        if (!dto.IsException || string.IsNullOrWhiteSpace(dto.ExceptionReason))
        {
            return BadRequest(new
            {
                message = "Manual waybill entry is only permitted for exceptions with a documented reason. Standard records must arrive from DMS."
            });
        }

        if (string.IsNullOrWhiteSpace(dto.WaybillNumber))
        {
            return BadRequest(new { message = "Waybill number is required." });
        }

        var cleanWbNo = dto.WaybillNumber.Trim();

        // Requirement: Alert on duplicate waybill number at entry
        var existing = await _fomsDb.Waybills.FirstOrDefaultAsync(w => w.WaybillNumber == cleanWbNo);
        if (existing != null)
        {
            _fomsDb.WaybillDuplicateAlerts.Add(new WaybillDuplicateAlert
            {
                WaybillNumber = cleanWbNo,
                OriginalWaybillId = existing.WaybillId,
                OriginalPriorStatus = existing.PodStatus ?? "Unknown",
                OriginalIsBilled = existing.IsBilled,
                IncomingPayloadJson = System.Text.Json.JsonSerializer.Serialize(dto),
                Status = "Active",
                CreatedAt = DateTime.UtcNow,
                ResolutionRemarks = $"Duplicate detected on manual entry attempt. Original waybill ID: {existing.WaybillId}."
            });
            await _fomsDb.SaveChangesAsync();

            return Conflict(new
            {
                message = $"Duplicate waybill number '{cleanWbNo}' detected at entry. Waybill entry blocked."
            });
        }

        int targetClientId = dto.ClientId ?? 1;
        if (!string.IsNullOrEmpty(dto.ClientCode))
        {
            var cl = await _fomsDb.Clients.FirstOrDefaultAsync(c => c.ClientCode == dto.ClientCode);
            if (cl != null) targetClientId = cl.ClientId;
        }

        // Status normalization: Pending, Validated, CTC, On Hold, Rejected
        string podStatus = "Pending";
        if (dto.DeliveryStatus != null && dto.DeliveryStatus.Contains("Failed", StringComparison.OrdinalIgnoreCase))
        {
            podStatus = "On Hold";
        }
        else if (!string.IsNullOrEmpty(dto.PodStatus))
        {
            var s = dto.PodStatus.Trim();
            if (s.Equals("Validated", StringComparison.OrdinalIgnoreCase))
                podStatus = (dto.ClientSigned && dto.AccountingCopyMatched) ? "Validated" : "On Hold";
            else if (s.Equals("CTC", StringComparison.OrdinalIgnoreCase))
                podStatus = "CTC";
            else if (s.Equals("Rejected", StringComparison.OrdinalIgnoreCase))
                podStatus = "Rejected";
            else if (s.Equals("On Hold", StringComparison.OrdinalIgnoreCase) || s.Equals("Hold", StringComparison.OrdinalIgnoreCase))
                podStatus = "On Hold";
            else
                podStatus = "Pending";
        }

        var w = new Waybill
        {
            WaybillNumber = cleanWbNo,
            ClientId = targetClientId,
            Origin = dto.Origin ?? "Manila Hub",
            Destination = dto.Destination ?? "Customer Address",
            DeliveryArea = dto.DeliveryArea ?? "NCR",
            ActualWeight = dto.ActualWeight ?? 1m,
            DeclaredValue = dto.DeclaredValue ?? 1000m,
            DeliveryStatus = dto.DeliveryStatus ?? (podStatus == "On Hold" ? "Failed" : "Delivered"),
            PodStatus = podStatus,
            IsManualEntry = true,
            IsException = true,
            ExceptionReason = dto.ExceptionReason,
            ClientSigned = dto.ClientSigned,
            AccountingCopyMatched = dto.AccountingCopyMatched,
            HasCtc = podStatus == "CTC",
            CtcDocumentUrl = dto.CtcDocumentUrl,
            CtcReason = dto.CtcReason,
            CtcApprovedBy = dto.CtcApprovedBy,
            CtcApprovedAt = podStatus == "CTC" ? DateTime.UtcNow : null,
            HoldReason = podStatus == "On Hold" ? (dto.ExceptionReason ?? "Missing counterpart copy or delivery failure") : null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _fomsDb.Waybills.Add(w);
        await _fomsDb.SaveChangesAsync();

        _fomsDb.AuditLogs.Add(new FOMS.Domain.Entities.SqlServer.AuditLog
        {
            role = User.Identity?.Name ?? "Accounting",
            action = "MANUAL_WAYBILL_EXCEPTION_CREATED",
            module = "Waybills",
            affected_record_id = w.WaybillId,
            remarks = $"Manual exception waybill {w.WaybillNumber} created: {w.ExceptionReason}. Initial POD Status: {w.PodStatus}.",
            created_at = DateTime.UtcNow
        });
        await _fomsDb.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = w.WaybillId }, w);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] System.Text.Json.JsonElement body)
    {
        FOMS.Domain.Entities.SqlServer.Waybill? w = null;
        if (int.TryParse(id, out var intId))
        {
            w = await _fomsDb.Waybills.FindAsync(intId);
        }
        if (w == null)
        {
            w = await _fomsDb.Waybills.FirstOrDefaultAsync(x => x.WaybillNumber == id);
        }
        if (w == null) return NotFound(new { message = "Waybill not found" });

        if (body.TryGetProperty("status", out var statusProp))
        {
            var s = statusProp.GetString();
            if (s == "Billed")
            {
                w.IsBilled = true;
                w.PodStatus = "Billed";
            }
            else if (!string.IsNullOrEmpty(s))
            {
                w.PodStatus = NormalizePodStatus(s, w.DeliveryStatus, w.ClientSigned, w.AccountingCopyMatched, w.HasCtc);
            }
        }
        if (body.TryGetProperty("podStatus", out var podStatusProp))
        {
            var s = podStatusProp.GetString();
            if (!string.IsNullOrEmpty(s))
            {
                w.PodStatus = NormalizePodStatus(s, w.DeliveryStatus, w.ClientSigned, w.AccountingCopyMatched, w.HasCtc);
            }
        }
        if (body.TryGetProperty("clientSigned", out var signedProp))
        {
            w.ClientSigned = signedProp.GetBoolean();
        }
        if (body.TryGetProperty("accountingCopyMatched", out var matchProp))
        {
            w.AccountingCopyMatched = matchProp.GetBoolean();
        }
        if (body.TryGetProperty("isBilled", out var billedProp))
        {
            w.IsBilled = billedProp.GetBoolean();
        }
        if (body.TryGetProperty("invoiceNo", out var invNoProp))
        {
            w.InvoiceNo = invNoProp.GetString();
        }
        if (body.TryGetProperty("holdReason", out var holdProp))
        {
            w.HoldReason = holdProp.GetString();
        }

        w.UpdatedAt = DateTime.UtcNow;
        await _fomsDb.SaveChangesAsync();
        return Ok(w);
    }

    /// <summary>
    /// Validates original client-signed POD matched to Accounting's copy.
    /// </summary>
    [HttpPost("{id}/validate")]
    public async Task<IActionResult> ValidatePod(int id)
    {
        var w = await _fomsDb.Waybills.FindAsync(id);
        if (w == null) return NotFound(new { message = "Waybill not found" });

        w.ClientSigned = true;
        w.AccountingCopyMatched = true;
        w.AccountingCheckedAt = DateTime.UtcNow;
        w.AccountingCheckedBy = User.Identity?.Name ?? "Head Accountant";
        w.PodStatus = "Validated";
        w.HoldReason = null;
        w.UpdatedAt = DateTime.UtcNow;

        _fomsDb.AuditLogs.Add(new FOMS.Domain.Entities.SqlServer.AuditLog
        {
            role = User.Identity?.Name ?? "Head Accountant",
            action = "POD_VALIDATED",
            module = "POD Matching",
            affected_record_id = w.WaybillId,
            remarks = $"Waybill {w.WaybillNumber} POD validated: original client-signed POD matched to Accounting's copy.",
            created_at = DateTime.UtcNow
        });

        await _fomsDb.SaveChangesAsync();
        return Ok(new { success = true, podStatus = w.PodStatus, message = "Original client-signed POD matched and validated." });
    }

    /// <summary>
    /// Lost original needs CTC (document, reason, approver) flagged on invoice and audit trail.
    /// </summary>
    [HttpPost("{id}/ctc")]
    public async Task<IActionResult> ApproveCtc(int id, [FromBody] CtcApprovalDto dto)
    {
        var w = await _fomsDb.Waybills.FindAsync(id);
        if (w == null) return NotFound(new { message = "Waybill not found" });

        var docUrl = dto?.CtcDocumentUrl ?? dto?.DocumentUrl;
        var reason = dto?.CtcReason ?? dto?.Reason;
        var approver = dto?.ApproverName ?? dto?.Approver ?? User.Identity?.Name ?? "Finance Manager";

        if (string.IsNullOrWhiteSpace(docUrl) || string.IsNullOrWhiteSpace(reason) || string.IsNullOrWhiteSpace(approver))
        {
            return BadRequest(new { message = "CTC approval requires document attachment, reason, and approver name." });
        }

        w.PodStatus = "CTC";
        w.HasCtc = true;
        w.CtcDocumentUrl = docUrl;
        w.CtcReason = reason;
        w.CtcApprovedBy = approver;
        w.CtcApprovedAt = DateTime.UtcNow;
        w.HoldReason = null;
        w.UpdatedAt = DateTime.UtcNow;

        _fomsDb.AuditLogs.Add(new FOMS.Domain.Entities.SqlServer.AuditLog
        {
            role = approver,
            action = "POD_CTC_APPROVED",
            module = "POD Matching",
            affected_record_id = w.WaybillId,
            remarks = $"Waybill {w.WaybillNumber} approved as Certified True Copy (CTC). Reason: {reason}. Approver: {approver}. Document: {docUrl}.",
            created_at = DateTime.UtcNow
        });

        await _fomsDb.SaveChangesAsync();
        return Ok(new { success = true, podStatus = w.PodStatus, message = "Certified True Copy (CTC) approved for lost original POD." });
    }

    /// <summary>
    /// Failed delivery / missing counterpart = On Hold.
    /// </summary>
    [HttpPost("{id}/hold")]
    public async Task<IActionResult> PutOnHold(int id, [FromBody] PodHoldDto? dto)
    {
        var w = await _fomsDb.Waybills.FindAsync(id);
        if (w == null) return NotFound(new { message = "Waybill not found" });

        var reason = dto?.Reason ?? "Missing counterpart copy or delivery failure";
        w.PodStatus = "On Hold";
        w.HoldReason = reason;
        w.UpdatedAt = DateTime.UtcNow;

        _fomsDb.AuditLogs.Add(new FOMS.Domain.Entities.SqlServer.AuditLog
        {
            role = User.Identity?.Name ?? "Staff",
            action = "POD_ON_HOLD",
            module = "POD Matching",
            affected_record_id = w.WaybillId,
            remarks = $"Waybill {w.WaybillNumber} placed On Hold. Reason: {reason}.",
            created_at = DateTime.UtcNow
        });

        await _fomsDb.SaveChangesAsync();
        return Ok(new { success = true, podStatus = w.PodStatus, holdReason = w.HoldReason });
    }

    /// <summary>
    /// Rejection for invalid, damaged, or unreadable POD.
    /// </summary>
    [HttpPost("{id}/reject")]
    public async Task<IActionResult> RejectPod(int id, [FromBody] PodHoldDto? dto)
    {
        var w = await _fomsDb.Waybills.FindAsync(id);
        if (w == null) return NotFound(new { message = "Waybill not found" });

        var reason = dto?.Reason ?? "Damaged, unreadable, or invalid POD";
        w.PodStatus = "Rejected";
        w.HoldReason = reason;
        w.UpdatedAt = DateTime.UtcNow;

        _fomsDb.AuditLogs.Add(new FOMS.Domain.Entities.SqlServer.AuditLog
        {
            role = User.Identity?.Name ?? "Staff",
            action = "POD_REJECTED",
            module = "POD Matching",
            affected_record_id = w.WaybillId,
            remarks = $"Waybill {w.WaybillNumber} POD rejected. Reason: {reason}.",
            created_at = DateTime.UtcNow
        });

        await _fomsDb.SaveChangesAsync();
        return Ok(new { success = true, podStatus = w.PodStatus, reason });
    }

    [HttpPost("{id}/{actionName}")]
    public async Task<IActionResult> Act(int id, string actionName, [FromBody] System.Text.Json.JsonElement? body = null)
    {
        var lower = actionName?.ToLowerInvariant();
        if (lower == "validate" || lower == "approve")
            return await ValidatePod(id);

        if (lower == "ctc")
        {
            var dto = new CtcApprovalDto();
            if (body.HasValue)
            {
                if (body.Value.TryGetProperty("ctcDocumentUrl", out var doc) || body.Value.TryGetProperty("documentUrl", out doc))
                    dto.CtcDocumentUrl = doc.GetString();
                if (body.Value.TryGetProperty("ctcReason", out var r) || body.Value.TryGetProperty("reason", out r))
                    dto.CtcReason = r.GetString();
                if (body.Value.TryGetProperty("approverName", out var app) || body.Value.TryGetProperty("approver", out app))
                    dto.ApproverName = app.GetString();
            }
            return await ApproveCtc(id, dto);
        }

        if (lower == "hold")
        {
            var dto = new PodHoldDto();
            if (body.HasValue && (body.Value.TryGetProperty("reason", out var r) || body.Value.TryGetProperty("holdReason", out r)))
                dto.Reason = r.GetString();
            return await PutOnHold(id, dto);
        }

        if (lower == "reject")
        {
            var dto = new PodHoldDto();
            if (body.HasValue && (body.Value.TryGetProperty("reason", out var r) || body.Value.TryGetProperty("rejectionReason", out r)))
                dto.Reason = r.GetString();
            return await RejectPod(id, dto);
        }

        return BadRequest(new { message = $"Unknown action '{actionName}'. Supported actions: validate, ctc, hold, reject." });
    }
}
