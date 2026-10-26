using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FOMS.Application.Configuration;
using FOMS.Application.Contracts;
using FOMS.Application.Interfaces;
using FOMS.Infrastructure.Persistence;
using FOMS.Domain.Entities.SqlServer;
using Microsoft.EntityFrameworkCore;

namespace FOMS.Api.Controllers;

#region Unified Request DTOs (Supports both DMS contracts and FOMS Web UI)

public class DmsDeliveryCompletedRequest
{
    [Required]
    public string WaybillNumber { get; set; } = string.Empty;

    public string? ClientCode { get; set; }
    public int? ClientId { get; set; }
    public string? ClientName { get; set; }

    public string? Origin { get; set; } = "Manila Hub";
    public string? Destination { get; set; }
    public string? DeliveryArea { get; set; } = "NCR";

    public decimal ActualWeight { get; set; } = 1.0m;
    public decimal? LengthCm { get; set; }
    public decimal? WidthCm { get; set; }
    public decimal? HeightCm { get; set; }
    public int? NumberOfBoxes { get; set; }
    public int? ItemCount { get; set; }

    public decimal? DeclaredValue { get; set; } = 1000.0m;

    public string? PodImageUrl { get; set; }
    public string? PodUrl { get; set; }

    public string? PodStatus { get; set; }
    public string? RecipientName { get; set; }
    public string? RecipientAddress { get; set; }
    public string? RecipientContact { get; set; }
    public DateTime? DeliveryDateTime { get; set; }
    public DateTime? DeliveryDate { get; set; }
    public string? AssignedDriver { get; set; }
    public string? DriverName { get; set; }
    public string? DriverEmployeeId { get; set; }
    public string? PackageDescription { get; set; }
    public string? DeliveryRemarks { get; set; }
    public bool AutoGenerateInvoice { get; set; } = true;

    public string EffectivePodUrl =>
        !string.IsNullOrWhiteSpace(PodImageUrl) ? PodImageUrl : (PodUrl ?? string.Empty);

    public DmsDeliveryCompletedDto ToDto() => new()
    {
        WaybillNumber = WaybillNumber,
        DeliveryStatus = "Completed",
        ClientName = ClientName,
        ClientCode = ClientCode,
        RecipientName = RecipientName,
        RecipientAddress = RecipientAddress,
        RecipientContact = RecipientContact,
        DeliveryArea = DeliveryArea ?? "NCR",
        DeliveryDateTime = DeliveryDateTime ?? DeliveryDate ?? DateTime.UtcNow,
        PodImageUrl = EffectivePodUrl,
        AssignedDriver = AssignedDriver ?? DriverName,
        DriverEmployeeId = DriverEmployeeId,
        PackageDescription = PackageDescription,
        ItemCount = ItemCount ?? NumberOfBoxes ?? 1,
        ActualWeight = ActualWeight > 0 ? ActualWeight : 1.0m,
        DeclaredValue = DeclaredValue ?? 1000.0m,
        DeliveryRemarks = DeliveryRemarks
    };
}

public class DmsDeliveryFailedRequest
{
    [Required]
    public string WaybillNumber { get; set; } = string.Empty;

    public string? FailedStatus { get; set; } = "Failed";

    [Required]
    public string FailureReason { get; set; } = string.Empty;

    public string? FailureRemarks { get; set; }
    public string? Remarks { get; set; }

    public int AttemptCount { get; set; } = 1;
    public DateTime? FailedAt { get; set; }
    public string? DriverEmployeeId { get; set; }
    public string? DriverName { get; set; }

    public DmsDeliveryFailedDto ToDto() => new()
    {
        WaybillNumber = WaybillNumber,
        FailedStatus = FailedStatus ?? "Failed",
        FailureReason = FailureReason,
        FailureRemarks = FailureRemarks ?? Remarks,
        AttemptCount = AttemptCount > 0 ? AttemptCount : 1,
        FailedAt = FailedAt ?? DateTime.UtcNow,
        DriverEmployeeId = DriverEmployeeId ?? DriverName
    };
}

public class DmsRedeliveryCompletedRequest
{
    [Required]
    public string WaybillNumber { get; set; } = string.Empty;

    public string? UpdatedStatus { get; set; } = "Completed";

    public string? NewPodImageUrl { get; set; }
    public string? PodUrl { get; set; }

    public int RedeliveryAttemptCount { get; set; } = 2;
    public string? RedeliveryReference { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? RecipientName { get; set; }
    public string? DriverName { get; set; }
    public string? DeliveryRemarks { get; set; }
    public string? Remarks { get; set; }

    public string EffectivePodUrl =>
        !string.IsNullOrWhiteSpace(NewPodImageUrl) ? NewPodImageUrl : (PodUrl ?? string.Empty);

    public DmsRedeliveryCompletedDto ToDto() => new()
    {
        WaybillNumber = WaybillNumber,
        UpdatedStatus = UpdatedStatus ?? "Completed",
        NewPodImageUrl = EffectivePodUrl,
        RedeliveryAttemptCount = RedeliveryAttemptCount > 0 ? RedeliveryAttemptCount : 2,
        RedeliveryReference = RedeliveryReference,
        CompletedAt = CompletedAt ?? DateTime.UtcNow,
        RecipientName = RecipientName,
        DeliveryRemarks = DeliveryRemarks ?? Remarks
    };
}

public class DmsCashAdvanceRequest
{
    public string? RequestId { get; set; }
    public string? AdvanceId { get; set; }

    public string? DeliveryReference { get; set; }
    public string? TripReference { get; set; }

    public string? RequesterName { get; set; }
    public string? CourierName { get; set; }
    public string? RequesterEmployeeId { get; set; }

    public string? Purpose { get; set; }
    public string? Notes { get; set; }

    public decimal? RequestedAmount { get; set; }
    public decimal? AmountGiven { get; set; }

    public DateTime? RequestDate { get; set; }
    public DateTime? ReleaseDate { get; set; }

    public DmsCashAdvanceRequestDto ToDto()
    {
        var reqId = !string.IsNullOrWhiteSpace(RequestId)
            ? RequestId
            : (!string.IsNullOrWhiteSpace(AdvanceId) ? AdvanceId : $"ADV-{Guid.NewGuid().ToString("N")[..6].ToUpper()}");

        var amount = RequestedAmount ?? AmountGiven ?? 0m;
        var requester = !string.IsNullOrWhiteSpace(RequesterName) ? RequesterName : (CourierName ?? "Courier");
        var reference = DeliveryReference ?? TripReference;
        var purpose = !string.IsNullOrWhiteSpace(Purpose) ? Purpose : (Notes ?? "Operational Route Advance");

        return new DmsCashAdvanceRequestDto
        {
            RequestId = reqId,
            DeliveryReference = reference,
            RequesterName = requester,
            RequesterEmployeeId = RequesterEmployeeId,
            Purpose = purpose,
            RequestedAmount = amount,
            RequestDate = RequestDate ?? ReleaseDate ?? DateTime.UtcNow
        };
    }
}

public class DmsLiquidationItemRequest
{
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? ReceiptUrl { get; set; }
}

public class DmsLiquidationRequest
{
    public string? RequestId { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? CashAdvanceId { get; set; }
    public int? FomsCashAdvanceId { get; set; }

    public string? DeliveryReference { get; set; }
    public string? TripReference { get; set; }

    public string? SubmittedByName { get; set; }
    public string? CourierName { get; set; }

    public decimal? TotalAmount { get; set; }
    public decimal? TotalExpenses { get; set; }

    public decimal LeftoverCash { get; set; } = 0.0m;
    public decimal ReimbursementAmount { get; set; } = 0.0m;

    public List<DmsLiquidationReceiptItemDto>? Receipts { get; set; }
    public List<DmsLiquidationItemRequest>? Expenses { get; set; }
    public List<string>? DocumentUrls { get; set; }

    public string? Notes { get; set; }
    public string? Remarks { get; set; }
    public DateTime? SubmissionDate { get; set; }

    public DmsLiquidationSubmissionDto ToDto()
    {
        var reqId = !string.IsNullOrWhiteSpace(RequestId)
            ? RequestId
            : (!string.IsNullOrWhiteSpace(CashAdvanceId) ? CashAdvanceId : (!string.IsNullOrWhiteSpace(ReferenceNumber) ? ReferenceNumber : Guid.NewGuid().ToString("N")[..6].ToUpper()));

        var total = TotalAmount ?? TotalExpenses ?? 0m;
        var submitter = !string.IsNullOrWhiteSpace(SubmittedByName) ? SubmittedByName : (CourierName ?? "Courier");
        var notes = Notes ?? Remarks;

        var receiptItems = Receipts ?? new List<DmsLiquidationReceiptItemDto>();
        if (Expenses != null && Expenses.Any())
        {
            receiptItems.AddRange(Expenses.Select(e => new DmsLiquidationReceiptItemDto
            {
                Description = e.Description,
                Amount = e.Amount,
                ReceiptUrl = e.ReceiptUrl
            }));
        }

        if (DocumentUrls != null && DocumentUrls.Any() && !receiptItems.Any())
        {
            foreach (var url in DocumentUrls)
            {
                receiptItems.Add(new DmsLiquidationReceiptItemDto
                {
                    Description = "Receipt Attachment",
                    Amount = total,
                    ReceiptUrl = url
                });
            }
        }

        return new DmsLiquidationSubmissionDto
        {
            RequestId = reqId,
            FomsCashAdvanceId = FomsCashAdvanceId,
            DeliveryReference = DeliveryReference ?? TripReference,
            SubmittedByName = submitter,
            TotalAmount = total,
            LeftoverCash = LeftoverCash,
            ReimbursementAmount = ReimbursementAmount,
            Receipts = receiptItems,
            Notes = notes,
            SubmissionDate = SubmissionDate ?? DateTime.UtcNow
        };
    }
}

public class DmsWaybillCreatedDto
{
    [Required]
    public string WaybillNumber { get; set; } = string.Empty;
    public string? ClientName { get; set; }
    public string? ClientCode { get; set; }
    public int? ClientId { get; set; }
    public string? Origin { get; set; } = "Manila Hub";
    public string? Destination { get; set; } = "Customer Address";
    public string? DeliveryArea { get; set; } = "NCR";
    public decimal ActualWeight { get; set; } = 1.0m;
    public decimal? LengthCm { get; set; }
    public decimal? WidthCm { get; set; }
    public decimal? HeightCm { get; set; }
    public int? NumberOfBoxes { get; set; } = 1;
    public decimal? DeclaredValue { get; set; } = 1000m;
    public string? DeliveryStatus { get; set; } = "Delivered";
    public string? PodStatus { get; set; } = "Validated";
    public string? PodUrl { get; set; }
    public string? RecipientName { get; set; }
    public string? AssignedDriver { get; set; }
    public string? DriverEmployeeId { get; set; }
}

#endregion

[ApiController]
[Route("api/integration/dms")]
public class DmsIntegrationController : ControllerBase
{
    private readonly IDmsIntegrationService _dmsService;
    private readonly DmsIntegrationOptions _options;
    private readonly ILogger<DmsIntegrationController> _logger;
    private readonly FomsDbContext? _fomsDb;

    public DmsIntegrationController(
        IDmsIntegrationService dmsService,
        IOptions<DmsIntegrationOptions> options,
        ILogger<DmsIntegrationController> logger,
        FomsDbContext? fomsDb = null)
    {
        _dmsService = dmsService;
        _options = options.Value;
        _logger = logger;
        _fomsDb = fomsDb;
    }

    private IActionResult? RejectUnlessAuthorised()
    {
        // 1. Allow authenticated user JWT from web frontend
        if (User.Identity?.IsAuthenticated == true)
            return null;

        // 2. Machine-to-machine key authorization
        var expectedKey = _options.InboundApiKey;
        if (string.IsNullOrWhiteSpace(expectedKey))
        {
            _logger.LogWarning("DMS integration InboundApiKey is not configured in environment variables.");
            return StatusCode(503, new { isSuccess = false, message = "DMS integration is not configured on this server." });
        }

        var headerValue = Request.Headers["X-Api-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(headerValue) || !ConstantTimeEquals(headerValue.Trim(), expectedKey.Trim()))
        {
            _logger.LogWarning("Unauthorized DMS call: missing or invalid X-Api-Key.");
            return Unauthorized(new { isSuccess = false, message = "Invalid or missing X-Api-Key header." });
        }

        return null;
    }

    private static bool ConstantTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++)
            diff |= a[i] ^ b[i];
        return diff == 0;
    }

    private IActionResult StandardResponse(DmsIntegrationResponseDto result)
    {
        var payload = new
        {
            isSuccess = result.IsSuccess,
            message = result.Message,
            waybillNumber = result.WaybillNumber,
            invoiceNumber = result.InvoiceNumber,
            computedAmount = result.ComputedAmount,
            billingHoldActive = result.BillingHoldActive,
            cashAdvanceId = result.CashAdvanceId,
            liquidationId = result.LiquidationId,
            status = result.Status,
            data = new
            {
                waybillNumber = result.WaybillNumber,
                invoiceNumber = result.InvoiceNumber,
                totalComputedAmount = result.ComputedAmount,
                computedAmount = result.ComputedAmount,
                billingStatus = result.BillingHoldActive == true ? "On Hold - Delivery Failed" : (result.Status ?? "Validated"),
                billingHoldActive = result.BillingHoldActive,
                cashAdvanceId = result.CashAdvanceId,
                liquidationId = result.LiquidationId,
                status = result.Status
            }
        };

        return result.IsSuccess ? Ok(payload) : BadRequest(payload);
    }

    /// <summary>Integration Health & status check.</summary>
    [HttpGet("health")]
    [AllowAnonymous]
    public IActionResult CheckHealth()
    {
        return Ok(new
        {
            status = "Healthy",
            service = "FOMS-DMS Integration",
            configured = !string.IsNullOrWhiteSpace(_options.InboundApiKey),
            canCallDms = _options.CanCallDms,
            dmsBaseUrl = _options.BaseUrl,
            timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// DMS ↔ FOMS: Ingests new waybill created/dispatched in DMS (FR-005, 006, 007).
    /// Dynamically inserts or updates in dbo.Waybills as unbilled record for billing validation and invoice creation.
    /// </summary>
    [HttpPost("waybill-created")]
    [HttpPost("waybill-intake")]
    [AllowAnonymous]
    public async Task<IActionResult> WaybillCreated([FromBody] DmsWaybillCreatedDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.WaybillNumber))
            return BadRequest(new { isSuccess = false, message = "WaybillNumber is required" });

        if (_fomsDb == null)
            return Ok(new { isSuccess = true, message = "Waybill created acknowledged." });

        var client = await _fomsDb.Clients.FirstOrDefaultAsync(c =>
            (dto.ClientId.HasValue && c.ClientId == dto.ClientId.Value) ||
            (!string.IsNullOrEmpty(dto.ClientCode) && (c.ClientCode == dto.ClientCode || c.DmsClientId == dto.ClientCode)) ||
            (!string.IsNullOrEmpty(dto.ClientName) && c.CompanyName == dto.ClientName));

        int clientId = client?.ClientId ?? dto.ClientId ?? 1;
        string clientName = client?.CompanyName ?? dto.ClientName ?? "Lazada Philippines";

        var cleanWaybillNo = dto.WaybillNumber.Trim();
        var waybill = await _fomsDb.Waybills.FirstOrDefaultAsync(w => w.WaybillNumber == cleanWaybillNo);
        if (waybill == null)
        {
            waybill = new Waybill
            {
                WaybillNumber = cleanWaybillNo,
                ClientId = clientId,
                Origin = dto.Origin ?? "Manila Hub",
                Destination = dto.Destination ?? "Customer Address",
                DeliveryArea = dto.DeliveryArea ?? "NCR",
                ActualWeight = dto.ActualWeight > 0 ? dto.ActualWeight : 1.0m,
                LengthCm = dto.LengthCm,
                WidthCm = dto.WidthCm,
                HeightCm = dto.HeightCm,
                NumberOfBoxes = dto.NumberOfBoxes ?? 1,
                DeclaredValue = dto.DeclaredValue ?? 1000m,
                DeliveryStatus = dto.DeliveryStatus ?? "Delivered",
                PodStatus = dto.PodStatus ?? "Validated",
                IsBilled = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _fomsDb.Waybills.Add(waybill);
        }
        else
        {
            waybill.DeliveryStatus = dto.DeliveryStatus ?? waybill.DeliveryStatus;
            waybill.PodStatus = dto.PodStatus ?? waybill.PodStatus;
            waybill.ActualWeight = dto.ActualWeight > 0 ? dto.ActualWeight : waybill.ActualWeight;
            waybill.DeliveryArea = dto.DeliveryArea ?? waybill.DeliveryArea;
            waybill.DeclaredValue = dto.DeclaredValue ?? waybill.DeclaredValue;
            waybill.UpdatedAt = DateTime.UtcNow;
        }

        await _fomsDb.SaveChangesAsync();

        _fomsDb.AuditLogs.Add(new AuditLog
        {
            role = "DMS Operations Dispatch",
            action = "DMS_WAYBILL_INGESTED",
            module = "DMS ↔ FOMS Waybills",
            affected_record_id = waybill.WaybillId,
            remarks = $"DMS waybill {cleanWaybillNo} ({clientName}, {dto.ActualWeight}kg, {dto.DeliveryArea}) ingested into FOMS as unbilled waybill ready for billing validation.",
            created_at = DateTime.UtcNow
        });
        await _fomsDb.SaveChangesAsync();

        _logger.LogInformation("Ingested DMS waybill {WaybillNumber} for client {ClientName}", cleanWaybillNo, clientName);

        return Ok(new
        {
            isSuccess = true,
            message = $"Waybill {cleanWaybillNo} ingested successfully into FOMS as unbilled waybill.",
            data = new
            {
                waybillId = waybill.WaybillId,
                waybillNumber = waybill.WaybillNumber,
                clientName,
                deliveryStatus = waybill.DeliveryStatus,
                podStatus = waybill.PodStatus,
                isBilled = waybill.IsBilled
            }
        });
    }

    /// <summary>
    /// DMS ↔ FOMS Integration #1: Completed Delivery → Billing / AR.
    /// Ingests waybill details & POD, validates docs (FR-011, 012), applies billing rate (FR-010),
    /// generates BillingDocument (FR-015), creates invoice (FR-015–020), and records Accounts Receivable (FR-023).
    /// </summary>
    [HttpPost("delivery-completed")]
    [AllowAnonymous]
    public async Task<IActionResult> DeliveryCompleted([FromBody] DmsDeliveryCompletedRequest req)
    {
        var denied = RejectUnlessAuthorised();
        if (denied != null) return denied;

        if (string.IsNullOrWhiteSpace(req.WaybillNumber))
            return BadRequest(new { isSuccess = false, message = "WaybillNumber is required." });

        if (string.IsNullOrWhiteSpace(req.EffectivePodUrl))
            return BadRequest(new { isSuccess = false, message = "Proof of Delivery (PodImageUrl) is required per FR-011." });

        var result = await _dmsService.ProcessDeliveryCompletedAsync(req.ToDto());
        return StandardResponse(result);
    }

    /// <summary>
    /// DMS ↔ FOMS Integration #2: Failed Delivery → Billing Hold.
    /// Driver marks Failed Delivery in DMS → DMS sends status/reason (FR-013) to place FOMS billing hold (FR-012, 013, 021).
    /// Prevents invoice computation and bundling until resolved.
    /// </summary>
    [HttpPost("delivery-failed")]
    [AllowAnonymous]
    public async Task<IActionResult> DeliveryFailed([FromBody] DmsDeliveryFailedRequest req)
    {
        var denied = RejectUnlessAuthorised();
        if (denied != null) return denied;

        if (string.IsNullOrWhiteSpace(req.WaybillNumber))
            return BadRequest(new { isSuccess = false, message = "WaybillNumber is required." });

        if (string.IsNullOrWhiteSpace(req.FailureReason))
            return BadRequest(new { isSuccess = false, message = "FailureReason is required per FR-013." });

        var result = await _dmsService.ProcessDeliveryFailedAsync(req.ToDto());
        return StandardResponse(result);
    }

    /// <summary>
    /// DMS ↔ FOMS Integration #2: Successful Re-Delivery Update.
    /// DMS sends updated Completed status + new POD → FOMS lifts hold and resumes invoice processing (FR-022).
    /// </summary>
    [HttpPost("redelivery-completed")]
    [HttpPost("delivery-redelivered")]
    [AllowAnonymous]
    public async Task<IActionResult> RedeliveryCompleted([FromBody] DmsRedeliveryCompletedRequest req)
    {
        var denied = RejectUnlessAuthorised();
        if (denied != null) return denied;

        if (string.IsNullOrWhiteSpace(req.WaybillNumber))
            return BadRequest(new { isSuccess = false, message = "WaybillNumber is required." });

        if (string.IsNullOrWhiteSpace(req.EffectivePodUrl))
            return BadRequest(new { isSuccess = false, message = "New Proof of Delivery (NewPodImageUrl) is required per FR-022." });

        var result = await _dmsService.ProcessRedeliveryCompletedAsync(req.ToDto());
        return StandardResponse(result);
    }

    /// <summary>
    /// DMS ↔ FOMS Integration #3: Delivery Cash Advance Creation.
    /// Ops creates cash advance in DMS → sent to FOMS for finance review/approval (FR-050) prior to payout (FR-043, 044).
    /// </summary>
    [HttpPost("cash-advance")]
    [HttpPost("cash-advance-request")]
    [AllowAnonymous]
    public async Task<IActionResult> CreateCashAdvance([FromBody] DmsCashAdvanceRequest req)
    {
        var denied = RejectUnlessAuthorised();
        if (denied != null) return denied;

        var dto = req.ToDto();
        if (dto.RequestedAmount <= 0)
            return BadRequest(new { isSuccess = false, message = "RequestedAmount must be greater than 0." });

        var result = await _dmsService.ProcessCashAdvanceRequestAsync(dto);
        return StandardResponse(result);
    }

    /// <summary>
    /// DMS ↔ FOMS Integration #3: Post-trip Liquidation Submission.
    /// Post-trip liquidation + receipts submitted in DMS → FOMS validates and marks Pending Validation (FR-048–053).
    /// </summary>
    [HttpPost("liquidation")]
    [HttpPost("liquidation-submission")]
    [AllowAnonymous]
    public async Task<IActionResult> SubmitLiquidation([FromBody] DmsLiquidationRequest req)
    {
        var denied = RejectUnlessAuthorised();
        if (denied != null) return denied;

        var dto = req.ToDto();
        if (string.IsNullOrWhiteSpace(dto.RequestId))
            return BadRequest(new { isSuccess = false, message = "RequestId is required." });

        var result = await _dmsService.ProcessLiquidationSubmissionAsync(dto);
        return StandardResponse(result);
    }

    /// <summary>
    /// Look up waybill status, POD, and billing details.
    /// </summary>
    [HttpGet("waybills/{waybillNo}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetWaybillStatus(string waybillNo)
    {
        var denied = RejectUnlessAuthorised();
        if (denied != null) return denied;

        var result = await _dmsService.GetWaybillStatusAsync(waybillNo);
        if (result == null)
            return NotFound(new { isSuccess = false, message = $"Waybill '{waybillNo}' not found in FOMS." });

        return Ok(result);
    }
}
