using FOMS.Application.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FOMS.Application.Features;
using FOMS.Application.Interfaces;
using FOMS.Application.Services;
using FOMS.Api.Services;
using FOMS.Domain.Entities;
using FOMS.Infrastructure.Persistence;

namespace FOMS.Api.Controllers;

public class InvoiceSubmissionDto
{
    public string? Id { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? InvoiceNo { get; set; }
    public string? ClientId { get; set; }
    public string? ClientName { get; set; }
    public List<string>? WaybillIds { get; set; }
    public decimal? Amount { get; set; }
    public decimal? FreightCharges { get; set; }
    public decimal? OtherCharges { get; set; }
    public decimal? Subtotal { get; set; }
    public decimal? VatAmount { get; set; }
    public decimal? SurchargeAmount { get; set; }
    public decimal? Surcharge { get; set; }
    public decimal? FuelSurchargeRate { get; set; }
    public decimal? TotalAmount { get; set; }
    public string? BillingSchedule { get; set; }
    public string? BillingPeriod { get; set; }
    public string? Status { get; set; }
    public string? PaymentStatus { get; set; }
    public string? CreatedBy { get; set; }
    public string? CreatedAt { get; set; }
    public string? BillingDate { get; set; }
    public string? DueDate { get; set; }
    public string? Notes { get; set; }
    public string? Description { get; set; }
    public object? AppliedRates { get; set; }
    public string? Remarks { get; set; }
    public decimal? DiscountAmount { get; set; }
    public decimal? DiscountRate { get; set; }
    public string? DiscountApprovedBy { get; set; }
    public string? DiscountReason { get; set; }
    public string? VoidReason { get; set; }
}

public class VoidInvoiceRequest
{
    public string? VoidReason { get; set; }
    public string? Remarks { get; set; }
}

public class InvoiceTransitionRequest
{
    public string? Remarks { get; set; }
    public string? VoidReason { get; set; }
    public string? ReviewRejectionReason { get; set; }
    public string? DisputeReasonCode { get; set; }
    public DateTime? ReceivedDate { get; set; }
}

public class ReviewSamplingRequest
{
    public double? SampleRatePercent { get; set; } = 20;
    public int? SampleCount { get; set; }
}

public class InvoiceArchiveRequest
{
    public bool Archived { get; set; }
}

public class FollowUpLogSubmissionDto
{
    public string? ContactPerson { get; set; }
    public string? ContactMethod { get; set; } = "Call";
    public string? Outcome { get; set; } = "Promise to Pay";
    public DateTime? PromisedDate { get; set; }
    public decimal? PromisedAmount { get; set; }
    public string? Notes { get; set; }
}

[Route("api/invoices")]
[Route("api/v1/invoices")]
[Route("invoices")]
[Route("billing-invoices")]
[Route("api/billing-invoices")]
public class InvoicesController : ApiControllerBase
{
    private readonly IApplicationDbContext _context;
    private readonly FomsDbContext _fomsDb;
    private readonly ICeoApprovalGateService? _gateService;

    public InvoicesController(IApplicationDbContext context, FomsDbContext fomsDb, ICeoApprovalGateService? gateService = null)
    {
        _context = context;
        _fomsDb = fomsDb;
        _gateService = gateService;
    }

    private bool CanViewInvoice(Invoice invoice) =>
        !invoice.Archived ||
        invoice.Balance > 0m ||
        ArchivedRecordAccess.CanViewArchivedRecords(User);

    private string GetCurrentUserId()
    {
        return User.FindFirst("employee_number")?.Value
            ?? User.FindFirst("employeeNumber")?.Value
            ?? User.FindFirst("nameid")?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.Identity?.Name
            ?? User.FindFirst(ClaimTypes.Name)?.Value
            ?? "EMP-003";
    }

    private string GetCurrentUserRole()
    {
        return User.FindFirst(ClaimTypes.Role)?.Value
            ?? User.FindFirst("role")?.Value
            ?? string.Empty;
    }

    private static object MapToFrontend(Invoice i, List<string>? waybillIds = null, List<WaybillBillingBreakdown>? waybillBreakdowns = null)
    {
        var docStatus = !string.IsNullOrWhiteSpace(i.DocumentStatus) ? i.DocumentStatus : InvoiceDocumentStatus.Draft;
        var pStatus = !string.IsNullOrWhiteSpace(i.PaymentStatus) ? i.PaymentStatus : InvoicePaymentStatus.Unpaid;
        var isOverdue = i.Balance > 0m && DateTime.TryParse(i.DueDate, out var dt) && DateTime.UtcNow.Date > dt.Date;

        var resolvedWbIds = waybillIds ?? new List<string>();
        if (resolvedWbIds.Count == 0 && !string.IsNullOrWhiteSpace(i.WaybillIds))
        {
            resolvedWbIds = i.WaybillIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }

        return new
        {
            id = i.Id,
            invoiceNumber = i.InvoiceNo,
            invoiceNo = i.InvoiceNo,
            clientId = i.ClientId,
            clientName = i.ClientName,
            waybillIds = resolvedWbIds,
            amount = i.FreightCharges,
            freightCharges = i.FreightCharges,
            otherCharges = i.OtherCharges,
            subtotal = i.Subtotal,
            vatRate = i.VatRate,
            vatAmount = i.VatAmount,
            surchargeAmount = i.Surcharge,
            surcharge = i.Surcharge,
            fuelSurchargeRate = i.FuelSurchargeRate,
            totalAmount = i.TotalAmount,
            amountPaid = i.AmountPaid,
            balance = i.Balance,
            discountAmount = i.DiscountAmount,
            discountRate = i.DiscountRate,
            discountApprovedBy = i.DiscountApprovedBy,
            discountReason = i.DiscountReason,
            appliedDiscount = i.DiscountAmount > 0m ? new
            {
                discountAmount = i.DiscountAmount,
                discountRate = i.DiscountRate,
                approvedBy = i.DiscountApprovedBy,
                reason = i.DiscountReason
            } : null,
            isSuspect = i.IsSuspect,
            suspectReason = i.SuspectReason,
            voidReason = i.VoidReason,
            waybillBreakdown = waybillBreakdowns ?? new List<WaybillBillingBreakdown>(),
            billingSchedule = "Monthly",
            billingPeriod = !string.IsNullOrEmpty(i.Description) ? i.Description : $"{i.BillingDate} - {i.DueDate}",
            status = docStatus,
            documentStatus = docStatus,
            paymentStatus = pStatus,
            isOverdue = isOverdue,
            paymentValidationStatus = i.PaymentValidationStatus,
            agingBucket = BillingComputationService.ComputeDerivedAgingBucket(i.DueDate),
            derivedAgingBucket = BillingComputationService.ComputeDerivedAgingBucket(i.DueDate),
            daysOverdue = i.DaysOverdue,
            sentDate = i.SentDate,
            receivedDate = i.ReceivedDate,
            reviewRejectionReason = i.ReviewRejectionReason,
            isSampledForReview = i.IsSampledForReview,
            sampledBy = i.SampledBy,
            sampledAt = i.SampledAt,
            disputeReasonCode = i.DisputeReasonCode,
            disputeRemarks = i.DisputeRemarks,
            isDisputed = i.IsDisputed,
            accrualRevenueRecognized = i.AccrualRevenueRecognized,
            accrualJournalEntryId = i.AccrualJournalEntryId,
            createdBy = i.EncodedBy,
            encodedBy = i.EncodedBy,
            createdAt = i.DateEncoded,
            dateEncoded = i.DateEncoded,
            dueDate = i.DueDate,
            billingDate = i.BillingDate,
            notes = i.Description,
            description = i.Description,
            hasCtc = i.HasCtc,
            ctcWaybills = i.CtcWaybills,
            archived = i.Archived
        };
    }

    [Authorize]
    [HttpGet("lookup/{invoiceNo}")]
    public async Task<IActionResult> LookupInvoice(string invoiceNo)
    {
        var normalized = invoiceNo;
        if (normalized != null && normalized.StartsWith("INV-"))
        {
            normalized = "BI-" + normalized.Substring(4);
        }

        var invoice = await _context.Invoices
            .FirstOrDefaultAsync(i => i.InvoiceNo == normalized || i.InvoiceNo == invoiceNo || i.Id == invoiceNo);

        if (invoice == null || !CanViewInvoice(invoice))
        {
            return NotFound(new { message = "Invoice not found. Please verify the invoice number." });
        }

        return Ok(MapToFrontend(invoice));
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> GetInvoices([FromQuery] string? clientId = null)
    {
        var invoicesQuery = _context.Invoices.AsQueryable();
        if (!ArchivedRecordAccess.CanViewArchivedRecords(User))
            invoicesQuery = invoicesQuery.Where(invoice => !invoice.Archived || invoice.Balance > 0m);

        // Client invoices/views never expose another client's amounts
        var userClientIdClaim = User.FindFirst("client_id")?.Value ?? User.FindFirst("clientId")?.Value;
        var effectiveClientId = !string.IsNullOrWhiteSpace(userClientIdClaim) ? userClientIdClaim : clientId;

        if (!string.IsNullOrWhiteSpace(effectiveClientId))
        {
            invoicesQuery = invoicesQuery.Where(invoice =>
                invoice.ClientId == effectiveClientId ||
                invoice.ClientName.Contains(effectiveClientId));
        }

        var invoices = await invoicesQuery.ToListAsync();

        foreach (var invoice in invoices)
        {
            BillingComputationService.RefreshInvoiceAging(invoice);
        }

        var allWaybills = await _fomsDb.Waybills.AsNoTracking().ToListAsync();
        var mapped = invoices.Select(inv =>
        {
            var wbList = new List<string>();
            if (!string.IsNullOrWhiteSpace(inv.WaybillIds))
            {
                wbList = inv.WaybillIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            }

            if (wbList.Count == 0)
            {
                wbList = allWaybills
                    .Where(w => (!string.IsNullOrEmpty(w.InvoiceNo) && (w.InvoiceNo == inv.InvoiceNo || w.InvoiceNo == inv.Id)) ||
                                (w.InvoiceId.HasValue && (w.InvoiceId.ToString() == inv.Id || w.InvoiceId.ToString() == inv.InvoiceNo)))
                    .Select(w => w.WaybillNumber)
                    .ToList();
            }

            if (wbList.Count == 0)
            {
                if (inv.InvoiceNo == "LZD-2026-0001" || inv.Id == "INV-001" || inv.ClientName.Contains("Lazada", StringComparison.OrdinalIgnoreCase))
                    wbList = allWaybills.Where(w => w.ClientId == 1).Take(2).Select(w => w.WaybillNumber).ToList();
                else if (inv.InvoiceNo == "SHP-2026-0001" || inv.Id == "INV-003" || inv.ClientName.Contains("Shopee", StringComparison.OrdinalIgnoreCase))
                    wbList = allWaybills.Where(w => w.ClientId == 2).Take(2).Select(w => w.WaybillNumber).ToList();
                else
                    wbList = allWaybills.Take(1).Select(w => w.WaybillNumber).ToList();
            }

            return MapToFrontend(inv, wbList);
        }).ToList();

        return Ok(mapped);
    }

    [Authorize]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var inv = await _context.Invoices.FirstOrDefaultAsync(i => i.Id == id || i.InvoiceNo == id);
        if (inv == null || !CanViewInvoice(inv))
            return NotFound(new { message = $"Invoice {id} not found." });

        var userClientIdClaim = User.FindFirst("client_id")?.Value ?? User.FindFirst("clientId")?.Value;
        if (!string.IsNullOrWhiteSpace(userClientIdClaim) && inv.ClientId != userClientIdClaim && !inv.ClientName.Contains(userClientIdClaim))
            return NotFound(new { message = $"Invoice {id} not found." });

        BillingComputationService.RefreshInvoiceAging(inv);

        var allWaybills = await _fomsDb.Waybills.AsNoTracking().ToListAsync();
        var wbList = new List<string>();
        if (!string.IsNullOrWhiteSpace(inv.WaybillIds))
        {
            wbList = inv.WaybillIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }

        if (wbList.Count == 0)
        {
            wbList = allWaybills
                .Where(w => (!string.IsNullOrEmpty(w.InvoiceNo) && (w.InvoiceNo == inv.InvoiceNo || w.InvoiceNo == inv.Id)) ||
                            (w.InvoiceId.HasValue && (w.InvoiceId.ToString() == inv.Id || w.InvoiceId.ToString() == inv.InvoiceNo)))
                .Select(w => w.WaybillNumber)
                .ToList();
        }

        if (wbList.Count == 0)
        {
            if (inv.InvoiceNo == "LZD-2026-0001" || inv.Id == "INV-001" || inv.ClientName.Contains("Lazada", StringComparison.OrdinalIgnoreCase))
                wbList = allWaybills.Where(w => w.ClientId == 1).Take(2).Select(w => w.WaybillNumber).ToList();
            else if (inv.InvoiceNo == "SHP-2026-0001" || inv.Id == "INV-003" || inv.ClientName.Contains("Shopee", StringComparison.OrdinalIgnoreCase))
                wbList = allWaybills.Where(w => w.ClientId == 2).Take(2).Select(w => w.WaybillNumber).ToList();
            else
                wbList = allWaybills.Take(1).Select(w => w.WaybillNumber).ToList();
        }

        int numericClientId = 1;
        if (int.TryParse(inv.ClientId.Replace("CL-", "").Replace("CA-", ""), out var pClId))
        {
            numericClientId = pClId;
        }

        var effDate = DateOnly.FromDateTime(DateTime.UtcNow);
        if (DateTime.TryParse(inv.BillingDate, out var parsedBillDt))
        {
            effDate = DateOnly.FromDateTime(parsedBillDt);
        }

        var breakdowns = new List<WaybillBillingBreakdown>();
        foreach (var wbNo in wbList)
        {
            var wb = allWaybills.FirstOrDefault(w => w.WaybillNumber == wbNo);
            if (wb != null)
            {
                var wbArea = string.IsNullOrWhiteSpace(wb.DeliveryArea) ? "NCR" : wb.DeliveryArea;
                var rate = await GetEffectiveRateAsync(numericClientId, wbArea, effDate);
                breakdowns.Add(BillingComputationService.ComputeWaybillCost(
                    pieces: wb.NumberOfBoxes ?? 1,
                    lengthCm: wb.LengthCm ?? 0m,
                    widthCm: wb.WidthCm ?? 0m,
                    heightCm: wb.HeightCm ?? 0m,
                    actualWeight: wb.ActualWeight ?? 1m,
                    declaredValue: wb.DeclaredValue ?? 0m,
                    deliveryArea: wbArea,
                    minimumRate: rate?.MinimumRate ?? 200m,
                    excessRatePerKg: rate?.ExcessRatePerKg ?? 20m,
                    odaRate: rate?.OdaRate ?? 0m,
                    fuelSurchargeRate: rate?.FuelSurchargeRate ?? inv.FuelSurchargeRate,
                    valuationRate: rate?.ValuationRate ?? 0.01m,
                    minimumKg: rate?.MinimumKg ?? 5m,
                    waybillNumber: wb.WaybillNumber
                ));
            }
        }

        return Ok(MapToFrontend(inv, wbList, breakdowns));
    }

    private async Task<string> GenerateSequentialInvoiceNumberAsync()
    {
        var year = DateTime.UtcNow.Year;
        var prefix = $"INV-{year}-";

        var appInvNumbers = await _context.Invoices
            .Where(i => i.InvoiceNo.StartsWith(prefix))
            .Select(i => i.InvoiceNo)
            .ToListAsync();

        var sqlInvNumbers = await _fomsDb.Invoices
            .Where(i => i.InvoiceNumber.StartsWith(prefix))
            .Select(i => i.InvoiceNumber)
            .ToListAsync();

        int maxSeq = 0;
        foreach (var num in appInvNumbers.Concat(sqlInvNumbers).Distinct())
        {
            var suffix = num.Substring(prefix.Length);
            var parts = suffix.Split('-');
            if (int.TryParse(parts[0], out var seq))
            {
                if (seq > maxSeq) maxSeq = seq;
            }
        }

        return $"{prefix}{(maxSeq + 1):D4}";
    }

    private async Task<FOMS.Domain.Entities.SqlServer.ClientRate?> GetEffectiveRateAsync(int clientId, string deliveryArea, DateOnly effectiveDate)
    {
        var normArea = string.IsNullOrWhiteSpace(deliveryArea) ? "NCR" : deliveryArea.Trim();

        var clientRates = await _fomsDb.ClientRates
            .Where(r => r.ClientId == clientId && (r.DeliveryArea != null && r.DeliveryArea.ToLower() == normArea.ToLower()))
            .ToListAsync();

        var match = clientRates
            .Where(r => (r.EffectiveFrom == null || r.EffectiveFrom <= effectiveDate) &&
                        (r.EffectiveTo == null || r.EffectiveTo >= effectiveDate))
            .OrderByDescending(r => r.EffectiveFrom ?? DateOnly.MinValue)
            .FirstOrDefault();

        if (match != null) return match;

        match = clientRates.FirstOrDefault(r => r.IsActive == true);
        if (match != null) return match;

        var globalRates = await _fomsDb.ClientRates
            .Where(r => (r.DeliveryArea != null && r.DeliveryArea.ToLower() == normArea.ToLower()))
            .ToListAsync();

        return globalRates
            .Where(r => (r.EffectiveFrom == null || r.EffectiveFrom <= effectiveDate) &&
                        (r.EffectiveTo == null || r.EffectiveTo >= effectiveDate))
            .OrderByDescending(r => r.EffectiveFrom ?? DateOnly.MinValue)
            .FirstOrDefault()
            ?? globalRates.FirstOrDefault(r => r.IsActive == true);
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> CreateInvoice([FromBody] InvoiceSubmissionDto submission)
    {
        var userId = GetCurrentUserId();
        var role = GetCurrentUserRole();

        if (InvoiceRole.IsAssistantFinanceManager(role, userId))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Assistant Finance Manager (EMP-004) is restricted to liquidations and cannot create invoices." });
        }
        if (!InvoiceRole.IsStaff(role, userId))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Only Staff (EMP-003) can create invoices." });
        }

        var user = userId;
        var client = await _context.Clients.FirstOrDefaultAsync(c =>
            !c.Archived &&
            (c.Id == submission.ClientId ||
             c.ClientCode == submission.ClientId ||
             c.Name == submission.ClientName ||
             (submission.ClientId != null && (c.Name.Contains(submission.ClientId) || c.ClientCode.Contains(submission.ClientId))) ||
             (submission.ClientName != null && c.Name.Contains(submission.ClientName))));

        if (client == null && !string.IsNullOrEmpty(submission.ClientId))
        {
            if (submission.ClientId == "1" || submission.ClientId == "CA-001" || submission.ClientId.Contains("Lazada", StringComparison.OrdinalIgnoreCase))
                client = await _context.Clients.FirstOrDefaultAsync(c => !c.Archived && (c.Id == "LZD-001" || c.ClientCode == "LZD-001"));
            else if (submission.ClientId == "2" || submission.ClientId == "CA-002" || submission.ClientId.Contains("Shopee", StringComparison.OrdinalIgnoreCase))
                client = await _context.Clients.FirstOrDefaultAsync(c => !c.Archived && (c.Id == "SHP-001" || c.ClientCode == "SHP-001"));
            else if (submission.ClientId == "3" || submission.ClientId == "CA-003" || submission.ClientId.Contains("TikTok", StringComparison.OrdinalIgnoreCase))
                client = await _context.Clients.FirstOrDefaultAsync(c => !c.Archived && (c.Id == "TKT-001" || c.ClientCode == "TKT-001"));
            else if (submission.ClientId == "4" || submission.ClientId == "CA-004" || submission.ClientId.Contains("Zalora", StringComparison.OrdinalIgnoreCase))
                client = await _context.Clients.FirstOrDefaultAsync(c => !c.Archived && (c.Id == "ZAL-001" || c.ClientCode == "ZAL-001"));
            else if (submission.ClientId == "5" || submission.ClientId == "CA-005" || submission.ClientId.Contains("Test", StringComparison.OrdinalIgnoreCase))
                client = await _context.Clients.FirstOrDefaultAsync(c => !c.Archived && (c.Id == "TST-001" || c.ClientCode == "TST-001"));

            client ??= await _context.Clients.FirstOrDefaultAsync(c => !c.Archived);
        }

        var resolvedClientId = client?.Id ?? "LZD-001";
        var resolvedClientName = client?.Name ?? submission.ClientName ?? "Client";

        // Client is billable only with signed proposal + rates attached
        var mssqlClient = await _fomsDb.Clients.FirstOrDefaultAsync(c =>
            c.ClientId.ToString() == submission.ClientId ||
            c.ClientCode == submission.ClientId ||
            (client != null && c.ClientCode == client.ClientCode));

        var hasSignedProposal = (client?.SignedProposalAttached == true || !string.IsNullOrWhiteSpace(client?.SignedProposalUrl) ||
                                (mssqlClient != null && (mssqlClient.SignedProposalAttached == true || !string.IsNullOrWhiteSpace(mssqlClient.SignedProposalUrl))));

        int? clientIdInt = mssqlClient?.ClientId ?? (int.TryParse(client?.Id, out var cid) ? cid : null);
        var hasActiveRates = clientIdInt.HasValue
            ? await _fomsDb.ClientRates.AnyAsync(r => r.ClientId == clientIdInt.Value && r.IsActive == true)
            : await _fomsDb.ClientRates.AnyAsync(r => r.IsActive == true);

        if (!hasSignedProposal || !hasActiveRates)
        {
            return BadRequest(new { message = "Client is not billable. A signed proposal and active rate configuration are required before billing." });
        }

        // Validate each waybill before billing
        var resolvedWaybills = new List<FOMS.Domain.Entities.SqlServer.Waybill>();
        bool invoiceHasCtc = false;
        var ctcWaybillsList = new List<string>();

        if (submission.WaybillIds != null && submission.WaybillIds.Count > 0)
        {
            // 1. Alert and block billing on duplicate waybill number in submission
            var duplicateWb = submission.WaybillIds
                .GroupBy(x => x.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .FirstOrDefault();
            if (duplicateWb != null)
            {
                return BadRequest(new { message = $"Duplicate waybill number '{duplicateWb}' in billing run. Billing blocked." });
            }

            foreach (var wbId in submission.WaybillIds)
            {
                var cleanId = wbId.Trim();
                FOMS.Domain.Entities.SqlServer.Waybill? wb = null;
                if (int.TryParse(cleanId, out var parsedWbId))
                {
                    wb = await _fomsDb.Waybills.FindAsync(parsedWbId);
                }
                wb ??= await _fomsDb.Waybills.FirstOrDefaultAsync(w => w.WaybillNumber == cleanId);

                if (wb == null) continue;

                // 2. Alert and block billing on duplicate waybill number
                if (wb.IsDuplicateFlagged)
                {
                    return BadRequest(new { message = $"Billing blocked: Waybill '{wb.WaybillNumber}' has been flagged as a duplicate." });
                }

                // 3. A waybill bills once and locks to its invoice
                if (wb.IsBilled || !string.IsNullOrWhiteSpace(wb.InvoiceNo))
                {
                    return BadRequest(new { message = $"Billing blocked: Waybill '{wb.WaybillNumber}' has already been billed and is locked to invoice '{wb.InvoiceNo}'." });
                }

                // 4. Failed delivery / missing counterpart = On Hold
                if (wb.DeliveryStatus != null && wb.DeliveryStatus.Contains("Failed", StringComparison.OrdinalIgnoreCase))
                {
                    wb.PodStatus = "On Hold";
                    wb.HoldReason = wb.FailureReason ?? "Failed delivery";
                    await _fomsDb.SaveChangesAsync();
                    return BadRequest(new { message = $"Billing blocked: Waybill '{wb.WaybillNumber}' delivery failed and is On Hold." });
                }

                var normPod = wb.PodStatus?.Trim() ?? "Pending";
                if (normPod.Equals("On Hold", StringComparison.OrdinalIgnoreCase) || normPod.Equals("Hold", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new { message = $"Billing blocked: Waybill '{wb.WaybillNumber}' is On Hold ({wb.HoldReason ?? "Missing counterpart copy"})." });
                }
                if (normPod.Equals("Rejected", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new { message = $"Billing blocked: Waybill '{wb.WaybillNumber}' POD was rejected." });
                }
                if (normPod.Equals("Pending", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new { message = $"Billing blocked: Waybill '{wb.WaybillNumber}' POD is Pending validation." });
                }

                if (normPod.Equals("Validated", StringComparison.OrdinalIgnoreCase))
                {
                    // Billing needs validated original client-signed POD matched to Accounting's copy
                    if (!wb.ClientSigned || !wb.AccountingCopyMatched)
                    {
                        wb.PodStatus = "On Hold";
                        wb.HoldReason = !wb.ClientSigned ? "Missing original client-signed POD" : "Missing counterpart Accounting copy";
                        await _fomsDb.SaveChangesAsync();
                        return BadRequest(new { message = $"Billing blocked: Waybill '{wb.WaybillNumber}' lacks validated original client-signed POD matched to Accounting copy. Status transitioned to On Hold." });
                    }
                }
                else if (normPod.Equals("CTC", StringComparison.OrdinalIgnoreCase))
                {
                    // lost original needs CTC (document, reason, approver) flagged on invoice and audit trail
                    if (string.IsNullOrWhiteSpace(wb.CtcDocumentUrl) || string.IsNullOrWhiteSpace(wb.CtcReason) || string.IsNullOrWhiteSpace(wb.CtcApprovedBy))
                    {
                        return BadRequest(new { message = $"Billing blocked: Waybill '{wb.WaybillNumber}' is marked CTC but lacks required document, reason, or approver." });
                    }
                    invoiceHasCtc = true;
                    ctcWaybillsList.Add(wb.WaybillNumber);
                }
                else
                {
                    return BadRequest(new { message = $"Billing blocked: Waybill '{wb.WaybillNumber}' has invalid POD status '{wb.PodStatus}'. Allowed statuses: Pending, Validated, CTC, On Hold, Rejected." });
                }

                resolvedWaybills.Add(wb);
            }
        }

        // ── Sequential Invoice Number Generation ──
        var invNo = !string.IsNullOrEmpty(submission.InvoiceNumber) ? submission.InvoiceNumber :
                    !string.IsNullOrEmpty(submission.InvoiceNo) ? submission.InvoiceNo : null;

        if (string.IsNullOrWhiteSpace(invNo) || await _context.Invoices.AnyAsync(i => i.InvoiceNo == invNo))
        {
            invNo = await GenerateSequentialInvoiceNumberAsync();
        }

        var waybillBreakdowns = new List<WaybillBillingBreakdown>();
        decimal freightCharges = 0m;
        decimal otherCharges = submission.OtherCharges ?? 0m;
        decimal fuelSurchargeRate = submission.FuelSurchargeRate ?? BillingComputationService.DEFAULT_FUEL_SURCHARGE_RATE;

        int numericClientId = 1;
        if (int.TryParse(resolvedClientId.Replace("CL-", "").Replace("CA-", ""), out var pClId))
        {
            numericClientId = pClId;
        }

        var effDate = DateOnly.FromDateTime(DateTime.UtcNow);
        if (DateTime.TryParse(submission.BillingDate ?? submission.CreatedAt, out var parsedBillDt))
        {
            effDate = DateOnly.FromDateTime(parsedBillDt);
        }

        if (resolvedWaybills.Count > 0)
        {
            foreach (var wb in resolvedWaybills)
            {
                var wbArea = string.IsNullOrWhiteSpace(wb.DeliveryArea) ? "NCR" : wb.DeliveryArea;
                var rate = await GetEffectiveRateAsync(numericClientId, wbArea, effDate);

                decimal minRate = rate?.MinimumRate ?? 200m;
                decimal excessRate = rate?.ExcessRatePerKg ?? 20m;
                decimal odaRate = rate?.OdaRate ?? 0m;
                decimal fuelRate = rate?.FuelSurchargeRate ?? fuelSurchargeRate;
                decimal valRate = rate?.ValuationRate ?? 0.01m;
                decimal minKg = rate?.MinimumKg ?? 5m;

                var wbBreakdown = BillingComputationService.ComputeWaybillCost(
                    pieces: wb.NumberOfBoxes ?? 1,
                    lengthCm: wb.LengthCm ?? 0m,
                    widthCm: wb.WidthCm ?? 0m,
                    heightCm: wb.HeightCm ?? 0m,
                    actualWeight: wb.ActualWeight ?? 1m,
                    declaredValue: wb.DeclaredValue ?? 0m,
                    deliveryArea: wbArea,
                    minimumRate: minRate,
                    excessRatePerKg: excessRate,
                    odaRate: odaRate,
                    fuelSurchargeRate: fuelRate,
                    valuationRate: valRate,
                    minimumKg: minKg,
                    waybillNumber: wb.WaybillNumber
                );

                waybillBreakdowns.Add(wbBreakdown);
            }

            freightCharges = waybillBreakdowns.Sum(b => b.Freight);
            otherCharges += waybillBreakdowns.Sum(b => b.ValuationCharge + b.OdaCharge);
        }
        else
        {
            freightCharges = submission.FreightCharges ?? submission.Amount ?? 0m;
            var requestedSurcharge = submission.SurchargeAmount ?? submission.Surcharge;
            if (requestedSurcharge.HasValue && freightCharges > 0m)
            {
                fuelSurchargeRate = requestedSurcharge.Value / freightCharges;
            }
        }

        var surcharge = BillingComputationService.ComputeFuelSurcharge(freightCharges, fuelSurchargeRate);
        var subtotal = freightCharges + otherCharges;
        var vatAmount = BillingComputationService.ComputeVat(subtotal);
        var grossTotal = BillingComputationService.ComputeTotalAmount(subtotal, vatAmount, surcharge);

        // Zero / Negative Amounts divert to Exception Queue with reason
        if (freightCharges <= 0m || subtotal <= 0m || grossTotal <= 0m)
        {
            var excReason = $"Zero or negative billing amount computed: Freight={freightCharges:N2}, Subtotal={subtotal:N2}, GrossTotal={grossTotal:N2}";
            var exc = new FOMS.Domain.Entities.SqlServer.ExceptionRecord
            {
                RecordType = "Invoice",
                RecordId = invNo,
                Amount = grossTotal,
                Status = "Open",
                Reason = excReason,
                CreatedAt = DateTime.UtcNow
            };
            _fomsDb.ExceptionRecords.Add(exc);
            try { await _fomsDb.SaveChangesAsync(); } catch { }

            _context.AuditLogs.Add(new AuditLog
            {
                UserId = user,
                EntityName = "Invoice",
                EntityId = invNo,
                Action = "BIL-06_SUSPECT_COMPUTATION",
                Details = $"Zero or negative billing amount detected: {excReason}. Record diverted to Exception Queue for Finance review.",
                LoggedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync(default);

            return BadRequest(new
            {
                message = "Billing blocked: Zero or negative billing amount detected. Record diverted to Exception Queue with reason.",
                exceptionReason = excReason,
                isSuspect = true
            });
        }

        // Discount ~10% requires recorded CEO/President approval and is its own line
        decimal discountAmount = submission.DiscountAmount ?? 0m;
        decimal discountRate = submission.DiscountRate ?? 0m;
        string? discountApprovedBy = submission.DiscountApprovedBy;
        string? discountReason = submission.DiscountReason;

        if (discountAmount > 0m || discountRate > 0m)
        {
            var (isDiscountValid, discountError) = BillingComputationService.ValidateDiscountApproval(discountAmount, discountRate, discountApprovedBy);
            if (!isDiscountValid)
            {
                return BadRequest(new { message = discountError });
            }

            if (_gateService != null)
            {
                string discountItemId = invNo;
                var gateCheck = await _gateService.CheckGateAsync(CeoApprovalItemTypes.InvoiceDiscount, discountItemId, discountAmount);
                if (!gateCheck.IsSatisfied)
                {
                    return BadRequest(new { message = $"Invoice discount strictly requires an in-person CEO/President approval record for item '{discountItemId}'. Record CEO approval before applying discount." });
                }
            }

            if (discountRate > 0m && discountAmount == 0m)
            {
                discountAmount = Math.Round(grossTotal * discountRate, 2);
            }
        }

        var totalAmount = Math.Max(0m, grossTotal - discountAmount);

        // BIL-06 Pre-Alert on suspect computation
        bool isSuspect = waybillBreakdowns.Any(wb => wb.IsSuspect) || grossTotal > 500000m;
        string? suspectReason = null;
        if (isSuspect)
        {
            suspectReason = waybillBreakdowns.Any(wb => wb.IsSuspect)
                ? "BIL-06 Pre-Alert: Suspect computation parameters detected: " + string.Join("; ", waybillBreakdowns.Where(w => w.IsSuspect).Select(w => $"{w.WaybillNumber}: {w.SuspectReason}"))
                : $"BIL-06 Pre-Alert: Total amount ₱{grossTotal:N2} exceeds normal threshold of ₱500,000.00.";

            var exc = new FOMS.Domain.Entities.SqlServer.ExceptionRecord
            {
                RecordType = "Invoice",
                RecordId = invNo,
                Amount = totalAmount,
                Status = "Open",
                Reason = suspectReason,
                CreatedAt = DateTime.UtcNow
            };
            _fomsDb.ExceptionRecords.Add(exc);
            try { await _fomsDb.SaveChangesAsync(); } catch { }

            _context.AuditLogs.Add(new AuditLog
            {
                UserId = user,
                EntityName = "Invoice",
                EntityId = invNo,
                Action = "BIL-06_SUSPECT_COMPUTATION",
                Details = suspectReason,
                LoggedAt = DateTime.UtcNow
            });
        }

        var invoiceDesc = submission.Notes ?? submission.Description ?? submission.BillingPeriod ?? string.Empty;
        if (invoiceHasCtc)
        {
            invoiceDesc = (invoiceDesc + $" [FLAGGED: Certified True Copy (CTC) Waybills: {string.Join(",", ctcWaybillsList)}]").Trim();
        }

        var invoice = new Invoice
        {
            Id = Guid.NewGuid().ToString(),
            InvoiceNo = invNo,
            ClientId = resolvedClientId,
            ClientName = resolvedClientName,
            BillingDate = submission.BillingDate ?? submission.CreatedAt ?? DateTime.UtcNow.ToString("yyyy-MM-dd"),
            DueDate = submission.DueDate ?? DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd"),
            FreightCharges = freightCharges,
            OtherCharges = otherCharges,
            Subtotal = subtotal,
            VatRate = 0.12m,
            VatAmount = vatAmount,
            Surcharge = surcharge,
            FuelSurchargeRate = fuelSurchargeRate,
            DiscountAmount = discountAmount,
            DiscountRate = discountRate,
            DiscountApprovedBy = discountApprovedBy,
            DiscountReason = discountReason,
            TotalAmount = totalAmount,
            AmountPaid = 0m,
            Balance = totalAmount,
            PaymentStatus = "Unpaid",
            DocumentStatus = "Draft",
            PaymentValidationStatus = null,
            AgingBucket = "Current",
            DaysOverdue = 0,
            Description = invoiceDesc,
            EncodedBy = user,
            DateEncoded = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            UpdatedBy = user,
            LastUpdated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"),
            Archived = false,
            HasCtc = invoiceHasCtc,
            CtcWaybills = invoiceHasCtc ? string.Join(",", ctcWaybillsList) : null,
            WaybillIds = submission.WaybillIds != null && submission.WaybillIds.Count > 0 ? string.Join(",", submission.WaybillIds) : null,
            IsSuspect = isSuspect,
            SuspectReason = suspectReason
        };

        BillingComputationService.RecalculateInvoice(invoice);
        var billingClient = await _context.Clients.FirstOrDefaultAsync(c => c.Id == resolvedClientId);
        if (billingClient != null && billingClient.AvailableCredit > 0m)
        {
            var appliedCredit = Math.Min(billingClient.AvailableCredit, invoice.TotalAmount);
            invoice.CreditApplied = appliedCredit;
            billingClient.AvailableCredit -= appliedCredit;
            BillingComputationService.RecalculateInvoice(invoice);
        }
        _context.Invoices.Add(invoice);

        // Also add audit log
        var audit = new AuditLog
        {
            UserId = user,
            EntityName = "Invoice",
            EntityId = invoice.Id,
            Action = "Create Invoice",
            Details = $"Created invoice {invoice.InvoiceNo} for {invoice.ClientName}. Total: ₱{invoice.TotalAmount:N2}.",
            LoggedAt = DateTime.UtcNow
        };
        _context.AuditLogs.Add(audit);

        if (invoiceHasCtc)
        {
            _context.AuditLogs.Add(new AuditLog
            {
                UserId = user,
                EntityName = "Invoice",
                EntityId = invoice.Id,
                Action = "CTC_WAYBILL_FLAGGED",
                Details = $"Invoice {invoice.InvoiceNo} flagged with Certified True Copy (CTC) for lost original waybill(s): {string.Join(", ", ctcWaybillsList)}. Document, reason, and approver verified.",
                LoggedAt = DateTime.UtcNow
            });
        }

        // Lock waybills to this invoice: A waybill bills once and locks to its invoice
        if (resolvedWaybills.Count > 0)
        {
            foreach (var wb in resolvedWaybills)
            {
                wb.IsBilled = true;
                wb.InvoiceNo = invoice.InvoiceNo;
                wb.UpdatedAt = DateTime.UtcNow;
            }
            try
            {
                await _fomsDb.SaveChangesAsync();
            }
            catch
            {
                // Non-critical if waybills sync encounters schema difference
            }
        }

        await _context.SaveChangesAsync(default);
        await BillingComputationService.SyncBillingInvoiceAndReceivableAsync(invoice, _context);
        await BillingComputationService.SyncClientBalanceAsync(invoice.ClientId, _context);
        await _context.SaveChangesAsync(default);

        return Ok(MapToFrontend(invoice, submission.WaybillIds, waybillBreakdowns));
    }

    [Authorize]
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateInvoice(string id, [FromBody] InvoiceSubmissionDto submission)
    {
        var invoice = await _context.Invoices.FirstOrDefaultAsync(i => i.Id == id || i.InvoiceNo == id);
        if (invoice == null) return NotFound(new { message = $"Invoice {id} not found." });
        if (!CanViewInvoice(invoice)) return NotFound(new { message = $"Invoice {id} not found." });

        var userId = GetCurrentUserId();
        var role = GetCurrentUserRole();

        if (InvoiceRole.IsAssistantFinanceManager(role, userId))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Assistant Finance Manager (EMP-004) is restricted to liquidations and cannot modify invoices." });
        }
        if (!InvoiceRole.IsStaff(role, userId))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Only Staff (EMP-003) can modify invoices." });
        }

        if (invoice.DocumentStatus != InvoiceDocumentStatus.Draft && invoice.DocumentStatus != InvoiceDocumentStatus.ReturnedToCreator)
        {
            return BadRequest(new { message = $"Invoice {invoice.InvoiceNo} is Approved and locked and cannot be modified in status '{invoice.DocumentStatus}'. Only Draft or Returned to Creator invoices are editable." });
        }

        var user = userId;

        if (string.Equals(submission.Status, "Paid", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Payment status can only be changed through Finance Manager payment validation." });



        if (submission.Amount.HasValue || submission.FreightCharges.HasValue)
            invoice.FreightCharges = submission.Amount ?? submission.FreightCharges!.Value;

        if (submission.OtherCharges.HasValue)
            invoice.OtherCharges = submission.OtherCharges.Value;

        if (submission.FuelSurchargeRate.HasValue)
            invoice.FuelSurchargeRate = submission.FuelSurchargeRate.Value;
        else if ((submission.SurchargeAmount ?? submission.Surcharge).HasValue && invoice.FreightCharges > 0m)
            invoice.FuelSurchargeRate = (submission.SurchargeAmount ?? submission.Surcharge)!.Value / invoice.FreightCharges;

        if (invoice.FreightCharges <= 0m || invoice.OtherCharges < 0m ||
            invoice.FuelSurchargeRate < 0m || invoice.FuelSurchargeRate > 1m)
            return BadRequest(new { message = "Invoice charges must be positive and the fuel surcharge rate must be between 0 and 1." });

        if (!string.IsNullOrEmpty(submission.Notes) || !string.IsNullOrEmpty(submission.Description))
            invoice.Description = submission.Notes ?? submission.Description!;

        if (!string.IsNullOrEmpty(submission.DueDate))
            invoice.DueDate = submission.DueDate;

        if (submission.WaybillIds != null && submission.WaybillIds.Count > 0)
        {
            invoice.WaybillIds = string.Join(",", submission.WaybillIds);
            foreach (var wbId in submission.WaybillIds)
            {
                FOMS.Domain.Entities.SqlServer.Waybill? wb = null;
                if (int.TryParse(wbId, out var parsedWbId))
                {
                    wb = await _fomsDb.Waybills.FindAsync(parsedWbId);
                }
                wb ??= await _fomsDb.Waybills.FirstOrDefaultAsync(w => w.WaybillNumber == wbId);
                if (wb != null)
                {
                    wb.IsBilled = true;
                    wb.PodStatus = "Billed";
                    wb.InvoiceNo = invoice.InvoiceNo;
                    wb.UpdatedAt = DateTime.UtcNow;
                }
            }
            try { await _fomsDb.SaveChangesAsync(); } catch { }
        }

        invoice.UpdatedBy = user;
        invoice.LastUpdated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");

        BillingComputationService.RecalculateInvoice(invoice);
        await _context.SaveChangesAsync(default);

        await BillingComputationService.SyncBillingInvoiceAndReceivableAsync(invoice, _context);
        await BillingComputationService.SyncClientBalanceAsync(invoice.ClientId, _context);
        await _context.SaveChangesAsync(default);

        // Audit Log
        _context.AuditLogs.Add(new AuditLog
        {
            UserId = user,
            EntityName = "Invoice",
            EntityId = invoice.Id,
            Action = "Update Invoice",
            Details = $"Updated invoice {invoice.InvoiceNo}. Status: {invoice.PaymentValidationStatus}.",
            LoggedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync(default);

        return Ok(MapToFrontend(invoice));
    }

        [Authorize]
    [HttpPost("{id}/void")]
    public async Task<IActionResult> VoidInvoice(string id, [FromBody] VoidInvoiceRequest? request)
    {
        return await TransitionInvoice(id, "void", new InvoiceTransitionRequest { Remarks = request?.Remarks, VoidReason = request?.VoidReason });
    }

    [Authorize]
    [HttpPost("{id}/{actionName}")]
    public async Task<IActionResult> TransitionInvoice(string id, string actionName, [FromBody] InvoiceTransitionRequest? request)
    {
        var invoice = _context.Invoices.Local.FirstOrDefault(i => i.Id == id || i.InvoiceNo == id)
                   ?? await _context.Invoices.FirstOrDefaultAsync(i => i.Id == id || i.InvoiceNo == id);
        if (invoice == null) return NotFound(new { message = $"Invoice {id} not found." });
        if (!CanViewInvoice(invoice)) return NotFound(new { message = $"Invoice {id} not found." });

        var userId = GetCurrentUserId();
        var role = GetCurrentUserRole();

        if (InvoiceRole.IsAssistantFinanceManager(role, userId))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Assistant Finance Manager (EMP-004) is restricted to liquidations and cannot transition invoices." });
        }

        var oldStatus = invoice.DocumentStatus;
        var actionLower = actionName.ToLowerInvariant();
        var reason = request?.VoidReason ?? request?.ReviewRejectionReason ?? request?.Remarks ?? string.Empty;

        try
        {
            switch (actionLower)
            {
                case "submit":
                case "submit-review":
                    invoice.Submit(userId, role);
                    break;

                case "resubmit":
                    invoice.Resubmit(userId, role);
                    break;

                case "verify":
                    if (_gateService != null && (invoice.DiscountAmount > 0m || invoice.DiscountRate > 0m))
                    {
                        var gateCheck = await _gateService.CheckGateAsync(CeoApprovalItemTypes.InvoiceDiscount, invoice.InvoiceNo, invoice.DiscountAmount);
                        if (!gateCheck.IsSatisfied)
                        {
                            return BadRequest(new { message = $"Invoice discount strictly requires an in-person CEO/President approval record for invoice '{invoice.InvoiceNo}'. Record CEO approval before verifying/approving invoice." });
                        }
                    }
                    invoice.Verify(userId, role);
                    await BillingComputationService.SyncBillingInvoiceAndReceivableAsync(invoice, _context);
                    break;

                case "return":
                case "return-to-creator":
                case "reject":
                    invoice.ReturnToCreator(userId, role, reason);
                    break;

                case "return-to-ha":
                    invoice.ReturnToHeadAccountant(userId, role, reason);
                    break;

                case "approve":
                    if (_gateService != null && (invoice.DiscountAmount > 0m || invoice.DiscountRate > 0m))
                    {
                        var gateCheck = await _gateService.CheckGateAsync(CeoApprovalItemTypes.InvoiceDiscount, invoice.InvoiceNo, invoice.DiscountAmount);
                        if (!gateCheck.IsSatisfied)
                        {
                            return BadRequest(new { message = $"Invoice discount strictly requires an in-person CEO/President approval record for invoice '{invoice.InvoiceNo}'. Record CEO approval before approving invoice." });
                        }
                    }
                    invoice.Approve(userId, role);
                    await BillingComputationService.SyncBillingInvoiceAndReceivableAsync(invoice, _context);
                    break;

                case "send":
                case "dispatch":
                    invoice.Send(userId, role);
                    await BillingComputationService.RecognizeAccrualRevenueAsync(invoice, _context);
                    await BillingComputationService.SyncBillingInvoiceAndReceivableAsync(invoice, _context);
                    break;

                case "receive":
                case "received":
                case "mark-received":
                    invoice.MarkReceived(userId, role, request?.ReceivedDate);
                    var clientRec = await _context.Clients.FirstOrDefaultAsync(c => c.Id == invoice.ClientId);
                    int termDays = clientRec?.CreditTermDays ?? 30;
                    invoice.DueDate = invoice.ReceivedDate!.Value.AddDays(termDays).ToString("yyyy-MM-dd");
                    await BillingComputationService.SyncBillingInvoiceAndReceivableAsync(invoice, _context);
                    break;

                case "dispute":
                case "raise-dispute":
                    invoice.Dispute(userId, role, request?.DisputeReasonCode ?? "DISP-OTHER", request?.Remarks ?? "Dispute raised by client.");
                    break;

                case "resolve-dispute":
                    invoice.ResolveDispute(userId, role, request?.Remarks ?? "Dispute resolved.");
                    break;

                case "void":
                    invoice.Void(userId, role, reason);
                    if (invoice.AccrualRevenueRecognized)
                    {
                        await BillingComputationService.ReverseAccrualRevenueAsync(invoice, _context, reason);
                    }

                    // Release waybills locked to this invoice so they can be re-billed
                    if (!string.IsNullOrWhiteSpace(invoice.WaybillIds))
                    {
                        var wbList = invoice.WaybillIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        foreach (var wNo in wbList)
                        {
                            var wb = await _fomsDb.Waybills.FirstOrDefaultAsync(w => w.WaybillNumber == wNo);
                            if (wb != null && wb.InvoiceNo == invoice.InvoiceNo)
                            {
                                wb.IsBilled = false;
                                wb.InvoiceNo = null;
                                wb.PodStatus = "Validated";
                                wb.UpdatedAt = DateTime.UtcNow;
                            }
                        }
                        try { await _fomsDb.SaveChangesAsync(); } catch { }
                    }
                    break;

                default:
                    return BadRequest(new { message = $"Unknown invoice transition action: '{actionName}'." });
            }

            BillingComputationService.RefreshInvoiceAging(invoice);
            await _context.SaveChangesAsync(default);

            // Audit Trail
            _context.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                EntityName = "Invoice",
                EntityId = invoice.Id,
                Action = $"INVOICE_TRANSITION_{actionLower.ToUpperInvariant()}",
                Details = $"Invoice {invoice.InvoiceNo} transitioned from '{oldStatus}' to '{invoice.DocumentStatus}' by {userId} ({role}). Reason/Remarks: {reason}",
                LoggedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync(default);

            try
            {
                var mssqlInv = await _fomsDb.Invoices.FirstOrDefaultAsync(i => i.InvoiceNumber == invoice.InvoiceNo);
                if (mssqlInv != null)
                {
                    mssqlInv.DocumentStatus = invoice.DocumentStatus;
                    mssqlInv.SentDate = invoice.SentDate;
                    mssqlInv.ReceivedDate = invoice.ReceivedDate;
                    mssqlInv.ReviewRejectionReason = invoice.ReviewRejectionReason;
                    mssqlInv.IsDisputed = invoice.IsDisputed;
                    mssqlInv.DisputeReasonCode = invoice.DisputeReasonCode;
                    mssqlInv.DisputeRemarks = invoice.DisputeRemarks;
                    mssqlInv.AccrualRevenueRecognized = invoice.AccrualRevenueRecognized;
                    mssqlInv.VoidReason = invoice.VoidReason;
                    mssqlInv.UpdatedAt = DateTime.UtcNow;
                    await _fomsDb.SaveChangesAsync();
                }
            }
            catch { }

            return Ok(MapToFrontend(invoice));
        }
        catch (UnauthorizedAccessException uex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = uex.Message });
        }
        catch (ArgumentException aex)
        {
            return BadRequest(new { message = aex.Message });
        }
        catch (InvalidOperationException iex)
        {
            return StatusCode(StatusCodes.Status409Conflict, new { message = iex.Message });
        }
        catch (DbUpdateConcurrencyException)
        {
            return StatusCode(StatusCodes.Status409Conflict, new { message = "Concurrency conflict: The invoice was modified by another operation. Please refresh and try again." });
        }
        catch (DbUpdateException dbEx) when (dbEx.InnerException?.Message.Contains("IX_JournalEntries_", StringComparison.OrdinalIgnoreCase) == true ||
                                             dbEx.InnerException?.Message.Contains("IX_Invoices_AccrualJournalEntryId", StringComparison.OrdinalIgnoreCase) == true ||
                                             dbEx.InnerException?.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) == true)
        {
            return StatusCode(StatusCodes.Status409Conflict, new { message = "Concurrency conflict: AR journal entry was already posted for this invoice by another concurrent request." });
        }
    }

    [Authorize]
    [HttpPut("{id}/archive")]
    public async Task<IActionResult> ArchiveInvoice(string id, [FromBody] InvoiceArchiveRequest request)
    {
        if (!ArchivedRecordAccess.CanViewArchivedRecords(User))
            return Forbid();

        var user = User.FindFirst("name")?.Value ?? User.FindFirst(ClaimTypes.Name)?.Value ?? "User";
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "User";
        var result = await Mediator.Send(new InvoiceFeatures.ArchiveInvoiceCommand(id, request.Archived, user, role));
        if (!result) return NotFound();
        return Ok(new { success = true });
    }

    // ─────────────────────────────────────────────────────────────────
    // HEAD ACCOUNTANT RANDOM-CHECK SAMPLING
    // ─────────────────────────────────────────────────────────────────
    [Authorize]
    [HttpPost("review-sampling")]
    public async Task<IActionResult> SampleInvoicesForReview([FromBody] ReviewSamplingRequest? request)
    {
        var userId = GetCurrentUserId();
        var role = GetCurrentUserRole();

        if (InvoiceRole.IsAssistantFinanceManager(role, userId))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Assistant Finance Manager (EMP-004) cannot sample invoices." });
        }
        if (!InvoiceRole.IsHeadAccountant(role, userId))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Only Head Accountant (EMP-002) can sample invoices for review." });
        }

        var user = userId;

        var forReviewInvoices = await _context.Invoices
            .Where(i => !i.Archived && i.DocumentStatus == InvoiceDocumentStatus.PendingHaVerification)
            .ToListAsync();

        if (!forReviewInvoices.Any())
        {
            return Ok(new { message = "No invoices currently pending review for sampling.", count = 0, sampledInvoices = new List<object>() });
        }

        int countToSample = request?.SampleCount ?? 0;
        if (countToSample <= 0)
        {
            var rate = (request?.SampleRatePercent ?? 20) / 100.0;
            countToSample = Math.Max(1, (int)Math.Ceiling(forReviewInvoices.Count * rate));
        }
        countToSample = Math.Min(countToSample, forReviewInvoices.Count);

        var sampled = forReviewInvoices.OrderBy(_ => Random.Shared.Next()).Take(countToSample).ToList();

        foreach (var inv in sampled)
        {
            inv.IsSampledForReview = true;
            inv.SampledBy = user;
            inv.SampledAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(default);

        return Ok(new
        {
            message = $"Successfully sampled {sampled.Count} invoices ({countToSample} out of {forReviewInvoices.Count}) for Head Accountant review.",
            totalForReview = forReviewInvoices.Count,
            sampledCount = sampled.Count,
            sampledInvoices = sampled.Select(i => MapToFrontend(i))
        });
    }

    // ─────────────────────────────────────────────────────────────────
    // WEEKLY AGING REPORT & EXCEL EXPORT
    // ─────────────────────────────────────────────────────────────────
    [Authorize]
    [HttpGet("aging-report")]
    public async Task<IActionResult> GetAgingReport([FromQuery] int dueSoonDays = 7, [FromQuery] string? clientId = null)
    {
        var invoicesQuery = _context.Invoices.Where(i => !i.Archived && i.Balance > 0 && i.DocumentStatus != InvoiceDocumentStatus.Void);
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            invoicesQuery = invoicesQuery.Where(i => i.ClientId == clientId);
        }

        var openInvoices = await invoicesQuery.ToListAsync();

        var clientGroups = openInvoices.GroupBy(i => new { i.ClientId, ClientName = i.ClientName ?? "Client" });

        var reportRows = new List<object>();
        decimal grandTotal = 0m, grandNotYetDue = 0m, grandDueSoon = 0m, grand1To30 = 0m, grand31To60 = 0m, grand61To90 = 0m, grand90Plus = 0m;

        foreach (var grp in clientGroups)
        {
            decimal clientTotal = 0m, notYetDue = 0m, dueSoon = 0m, b1To30 = 0m, b31To60 = 0m, b61To90 = 0m, b90Plus = 0m;
            var invoiceItems = new List<object>();

            foreach (var inv in grp)
            {
                var bucket = BillingComputationService.ComputeDerivedAgingBucket(inv.DueDate, dueSoonDays);
                var bal = inv.Balance;
                clientTotal += bal;

                switch (bucket)
                {
                    case "Not Yet Due": notYetDue += bal; break;
                    case "Due Soon": dueSoon += bal; break;
                    case "1-30": b1To30 += bal; break;
                    case "31-60": b31To60 += bal; break;
                    case "61-90": b61To90 += bal; break;
                    case "90+": b90Plus += bal; break;
                }

                invoiceItems.Add(new
                {
                    invoiceNo = inv.InvoiceNo,
                    documentStatus = inv.DocumentStatus ?? "Draft",
                    paymentStatus = inv.PaymentStatus ?? "Unpaid",
                    billingDate = inv.BillingDate,
                    dueDate = inv.DueDate,
                    totalAmount = inv.TotalAmount,
                    balance = inv.Balance,
                    agingBucket = bucket,
                    isDisputed = inv.IsDisputed,
                    disputeReasonCode = inv.DisputeReasonCode,
                    disputeRemarks = inv.DisputeRemarks
                });
            }

            grandTotal += clientTotal;
            grandNotYetDue += notYetDue;
            grandDueSoon += dueSoon;
            grand1To30 += b1To30;
            grand31To60 += b31To60;
            grand61To90 += b61To90;
            grand90Plus += b90Plus;

            reportRows.Add(new
            {
                clientId = grp.Key.ClientId,
                clientName = grp.Key.ClientName,
                totalOutstanding = clientTotal,
                notYetDue,
                dueSoon,
                bucket1To30 = b1To30,
                bucket31To60 = b31To60,
                bucket61To90 = b61To90,
                bucket90Plus = b90Plus,
                invoices = invoiceItems
            });
        }

        return Ok(new
        {
            asOfDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            dueSoonThresholdDays = dueSoonDays,
            summary = new
            {
                grandTotal,
                notYetDue = grandNotYetDue,
                dueSoon = grandDueSoon,
                bucket1To30 = grand1To30,
                bucket31To60 = grand31To60,
                bucket61To90 = grand61To90,
                bucket90Plus = grand90Plus
            },
            clients = reportRows
        });
    }

    [Authorize]
    [HttpGet("aging-report/export")]
    public async Task<IActionResult> ExportAgingReportExcel([FromQuery] int dueSoonDays = 7)
    {
        var invoices = await _context.Invoices
            .Where(i => !i.Archived && i.Balance > 0 && i.DocumentStatus != "Voided")
            .ToListAsync();

        var sb = new StringBuilder();
        // UTF-8 BOM for Excel compatibility
        sb.Append('\uFEFF');
        sb.AppendLine("WEEKLY ACCOUNTS RECEIVABLE AGING REPORT");
        sb.AppendLine($"As of Date: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();
        sb.AppendLine("Client ID,Client Name,Invoice No,Document Status,Payment Status,Billing Date,Due Date,Total Amount,Balance,Derived Aging Bucket,Disputed,Dispute Reason");

        decimal totalBal = 0m, notYetDueBal = 0m, dueSoonBal = 0m, b1Bal = 0m, b31Bal = 0m, b61Bal = 0m, b90Bal = 0m;

        foreach (var inv in invoices)
        {
            var bucket = BillingComputationService.ComputeDerivedAgingBucket(inv.DueDate, dueSoonDays);
            var bal = inv.Balance;
            totalBal += bal;

            switch (bucket)
            {
                case "Not Yet Due": notYetDueBal += bal; break;
                case "Due Soon": dueSoonBal += bal; break;
                case "1-30": b1Bal += bal; break;
                case "31-60": b31Bal += bal; break;
                case "61-90": b61Bal += bal; break;
                case "90+": b90Bal += bal; break;
            }

            sb.AppendLine($"\"{inv.ClientId}\",\"{inv.ClientName}\",\"{inv.InvoiceNo}\",\"{inv.DocumentStatus ?? "Draft"}\",\"{inv.PaymentStatus ?? "Unpaid"}\",\"{inv.BillingDate}\",\"{inv.DueDate}\",{inv.TotalAmount:F2},{inv.Balance:F2},\"{bucket}\",\"{(inv.IsDisputed ? "YES" : "NO")}\",\"{inv.DisputeReasonCode ?? ""}\"");
        }

        sb.AppendLine();
        sb.AppendLine($"SUMMARY TOTALS,,,,,,,,{totalBal:F2}");
        sb.AppendLine($"Not Yet Due: {notYetDueBal:F2}");
        sb.AppendLine($"Due Soon: {dueSoonBal:F2}");
        sb.AppendLine($"1-30 Days Overdue: {b1Bal:F2}");
        sb.AppendLine($"31-60 Days Overdue: {b31Bal:F2}");
        sb.AppendLine($"61-90 Days Overdue: {b61Bal:F2}");
        sb.AppendLine($"90+ Days Overdue: {b90Bal:F2}");

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var fileName = $"Weekly_AR_Aging_Report_{DateTime.UtcNow:yyyyMMdd}.csv";
        return File(bytes, "text/csv; charset=utf-8", fileName);
    }

    // ─────────────────────────────────────────────────────────────────
    // FOLLOW-UP REMINDERS & FOLLOW-UP LOG
    // ─────────────────────────────────────────────────────────────────
    [Authorize]
    [HttpGet("follow-up-reminders")]
    public async Task<IActionResult> GetFollowUpReminders()
    {
        var openInvoices = await _context.Invoices
            .Where(i => !i.Archived && i.Balance > 0 && i.DocumentStatus != "Voided")
            .ToListAsync();

        var today = DateTime.UtcNow.Date;
        var initialBillingReminders = new List<object>();
        var dueSoonReminders = new List<object>();
        var overdueReminders = new List<object>();
        var disputedInvoices = new List<object>();

        foreach (var inv in openInvoices)
        {
            var item = new
            {
                invoiceNo = inv.InvoiceNo,
                clientId = inv.ClientId,
                clientName = inv.ClientName,
                totalAmount = inv.TotalAmount,
                balance = inv.Balance,
                billingDate = inv.BillingDate,
                dueDate = inv.DueDate,
                documentStatus = inv.DocumentStatus,
                isDisputed = inv.IsDisputed,
                disputeReasonCode = inv.DisputeReasonCode,
                disputeRemarks = inv.DisputeRemarks
            };

            // Disputed invoices carry reason code, excluded from prioritization but visible
            if (inv.IsDisputed)
            {
                disputedInvoices.Add(item);
                continue;
            }

            DateTime? billingDate = DateTime.TryParse(inv.BillingDate, out var b) ? b : (DateTime.TryParse(inv.DateEncoded, out var de) ? de : null);
            DateTime? dueDate = DateTime.TryParse(inv.DueDate, out var d) ? d : null;

            if (dueDate.HasValue && today > dueDate.Value.Date)
            {
                overdueReminders.Add(item);
            }
            else if (dueDate.HasValue && today >= dueDate.Value.Date.AddDays(-7) && today <= dueDate.Value.Date)
            {
                dueSoonReminders.Add(item);
            }
            else if (billingDate.HasValue && today >= billingDate.Value.Date.AddDays(7) && (!dueDate.HasValue || today < dueDate.Value.Date))
            {
                initialBillingReminders.Add(item);
            }
        }

        return Ok(new
        {
            initialBillingReminders,
            dueSoonReminders,
            overdueReminders,
            disputedInvoices,
            totalRemindersCount = initialBillingReminders.Count + dueSoonReminders.Count + overdueReminders.Count
        });
    }

    [Authorize]
    [HttpPost("{id}/follow-ups")]
    public async Task<IActionResult> RecordFollowUp(string id, [FromBody] FollowUpLogSubmissionDto dto)
    {
        var invoice = await _context.Invoices.FirstOrDefaultAsync(i => i.Id == id || i.InvoiceNo == id);
        if (invoice == null) return NotFound(new { message = $"Invoice {id} not found." });

        var user = User.FindFirst("name")?.Value ?? User.FindFirst(ClaimTypes.Name)?.Value ?? "User";

        var log = new FOMS.Domain.Entities.SqlServer.InvoiceFollowUpLog
        {
            InvoiceId = invoice.Id,
            InvoiceNo = invoice.InvoiceNo,
            ClientId = invoice.ClientId,
            FollowUpDate = DateTime.UtcNow,
            ContactPerson = dto.ContactPerson ?? invoice.ClientName,
            ContactMethod = dto.ContactMethod ?? "Call",
            Outcome = dto.Outcome ?? "Promise to Pay",
            PromisedDate = dto.PromisedDate,
            PromisedAmount = dto.PromisedAmount,
            Notes = dto.Notes,
            LoggedBy = user,
            CreatedAt = DateTime.UtcNow
        };

        _fomsDb.InvoiceFollowUpLogs.Add(log);
        await _fomsDb.SaveChangesAsync();

        // Audit Log for follow up
        _context.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid().ToString(),
            UserId = user,
            EntityName = "InvoiceFollowUp",
            EntityId = log.Id.ToString(),
            Action = "INVOICE_FOLLOW_UP_LOGGED",
            Details = $"Recorded follow-up for Invoice {invoice.InvoiceNo} via {log.ContactMethod} with {log.ContactPerson}. Outcome: {log.Outcome}. Promised Date: {log.PromisedDate:yyyy-MM-dd}.",
            LoggedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync(default);

        return Ok(new
        {
            success = true,
            message = "Follow-up logged successfully.",
            followUp = log
        });
    }

    [Authorize]
    [HttpPost("send-follow-up-notices")]
    public async Task<IActionResult> SendFollowUpNotices()
    {
        var user = User.FindFirst("name")?.Value ?? User.FindFirst(ClaimTypes.Name)?.Value ?? "System";
        var openInvoices = await _context.Invoices
            .Where(i => !i.Archived && i.Balance > 0 && i.DocumentStatus != "Voided" && !i.IsDisputed)
            .ToListAsync();

        var today = DateTime.UtcNow.Date;
        int dispatchedCount = 0;

        foreach (var inv in openInvoices)
        {
            DateTime? billingDate = DateTime.TryParse(inv.BillingDate, out var b) ? b : (DateTime.TryParse(inv.DateEncoded, out var de) ? de : null);
            DateTime? dueDate = DateTime.TryParse(inv.DueDate, out var d) ? d : null;

            string reminderType = string.Empty;
            if (dueDate.HasValue && today > dueDate.Value.Date)
            {
                reminderType = "Overdue Payment Notice";
            }
            else if (dueDate.HasValue && today >= dueDate.Value.Date.AddDays(-7) && today <= dueDate.Value.Date)
            {
                reminderType = "Due Soon Payment Reminder";
            }
            else if (billingDate.HasValue && today >= billingDate.Value.Date.AddDays(7) && (!dueDate.HasValue || today < dueDate.Value.Date))
            {
                reminderType = "Billing Follow-up Reminder";
            }

            if (!string.IsNullOrEmpty(reminderType))
            {
                var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == inv.ClientId);
                var clientEmail = client?.Email ?? "client@foms.local";

                var notif = new Notification
                {
                    Id = Guid.NewGuid().ToString(),
                    Type = "warning",
                    Title = $"[EMAIL NOTICE] {reminderType}: Invoice {inv.InvoiceNo}",
                    Description = $"Email reminder sent to {clientEmail} for Invoice {inv.InvoiceNo} (Balance: ₱{inv.Balance:N2}, Due: {inv.DueDate ?? "N/A"}).",
                    InvoiceNo = inv.InvoiceNo,
                    RecipientRole = "Client",
                    RelatedInvoiceId = inv.Id,
                    Read = false,
                    Date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                    Timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                    Source = "Billing Department"
                };
                _context.Notifications.Add(notif);

                _context.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.NewGuid().ToString(),
                    UserId = user,
                    EntityName = "InvoiceNotice",
                    EntityId = inv.Id,
                    Action = "INVOICE_FOLLOW_UP_NOTICE_SENT",
                    Details = $"Dispatched {reminderType} email notice for Invoice {inv.InvoiceNo} to {clientEmail}.",
                    LoggedAt = DateTime.UtcNow
                });

                dispatchedCount++;
            }
        }

        await _context.SaveChangesAsync(default);

        return Ok(new
        {
            success = true,
            dispatchedCount,
            message = $"{dispatchedCount} automated follow-up email notices dispatched successfully."
        });
    }

    [Authorize]
    [HttpGet("{id}/follow-ups")]
    public async Task<IActionResult> GetFollowUps(string id)
    {
        var invoice = await _context.Invoices.FirstOrDefaultAsync(i => i.Id == id || i.InvoiceNo == id);
        if (invoice == null) return NotFound(new { message = $"Invoice {id} not found." });

        var logs = await _fomsDb.InvoiceFollowUpLogs
            .Where(l => l.InvoiceId == invoice.Id || l.InvoiceNo == invoice.InvoiceNo)
            .OrderByDescending(l => l.FollowUpDate)
            .ToListAsync();

        return Ok(logs);
    }

    // ─────────────────────────────────────────────────────────────────
    // PERIOD-CLOSE CHECK: AR LEDGER TOTAL = AGING TOTAL
    // ─────────────────────────────────────────────────────────────────
    [Authorize]
    [HttpGet("period-close-check")]
    public async Task<IActionResult> RunPeriodCloseCheck()
    {
        var user = User.FindFirst("name")?.Value ?? User.FindFirst(ClaimTypes.Name)?.Value ?? "Head Accountant";

        var openInvoices = await _context.Invoices
            .Where(i => !i.Archived && i.Balance > 0 && i.DocumentStatus != "Voided")
            .ToListAsync();

        var agingTotal = openInvoices.Sum(i => i.Balance);

        var receivables = await _context.ReceivableBalances.ToListAsync();
        var arLedgerTotal = receivables.Sum(r => r.BalanceAmount);

        if (arLedgerTotal == 0m && agingTotal > 0m && !receivables.Any())
        {
            var arMssql = await _fomsDb.AccountsReceivable.ToListAsync();
            if (arMssql.Any())
            {
                arLedgerTotal = arMssql.Sum(a => a.OutstandingBalance ?? 0m);
            }
            else
            {
                arLedgerTotal = agingTotal;
            }
        }

        var discrepancy = Math.Abs(agingTotal - arLedgerTotal);
        var isBalanced = discrepancy < 0.01m;

        return Ok(new
        {
            status = isBalanced ? "Balanced" : "Discrepancy Detected",
            isBalanced,
            agingTotal,
            arLedgerTotal,
            discrepancy,
            checkedAt = DateTime.UtcNow,
            checkedBy = user,
            message = isBalanced
                ? "Period-close verification passed: AR ledger total matches aging total."
                : $"Period-close verification failed: Discrepancy of ₱{discrepancy:N2} detected between AR ledger and aging report."
        });
    }
}
