using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using FOMS.Application.Interfaces;
using FOMS.Domain.Entities;
using FOMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FOMS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/finance/period-closes")]
public class PeriodCloseController : ControllerBase
{
    private readonly IApplicationDbContext _context;
    private readonly FomsDbContext _fomsDb;

    public PeriodCloseController(IApplicationDbContext context, FomsDbContext fomsDb)
    {
        _context = context;
        _fomsDb = fomsDb;
    }

    [HttpGet]
    public async Task<IActionResult> GetClosedPeriods()
    {
        if (!CanViewPeriodCloses())
            return Forbid();

        var periods = await _context.AccountingPeriodCloses
            .AsNoTracking()
            .OrderByDescending(period => period.PeriodStart)
            .ToListAsync();
        return Ok(periods);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetClosedPeriod(string id)
    {
        if (!CanViewPeriodCloses())
            return Forbid();

        var period = await _context.AccountingPeriodCloses
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id);
        if (period == null)
            return NotFound(new { message = "Closed accounting period not found." });

        var corrections = await _context.AccountingPeriodCorrectionEntries
            .AsNoTracking()
            .Where(item => item.PeriodCloseId == period.Id)
            .OrderBy(item => item.EntryDate)
            .ToListAsync();
        return Ok(new { period, corrections });
    }

    [HttpPost]
    public async Task<IActionResult> ClosePeriod([FromBody] ClosePeriodRequest request)
    {
        if (!IsFinanceManager())
            return Forbid();
        if (request == null)
            return BadRequest(new { message = "Close checklist is required." });

        var periodType = string.Equals(request.PeriodType, "Month", StringComparison.OrdinalIgnoreCase)
            ? "Month"
            : string.Equals(request.PeriodType, "Year", StringComparison.OrdinalIgnoreCase)
                ? "Year"
                : request.PeriodType;
        if (!IsValidPeriod(periodType, request.PeriodStart, request.PeriodEnd))
            return BadRequest(new { message = "A close must cover one complete calendar month or calendar year." });

        if (string.IsNullOrWhiteSpace(request.BankStatementProofReference))
            return BadRequest(new { message = "A stored bank-statement proof reference is required." });

        if (!request.BankBookBalance.HasValue || !request.BankStatementBalance.HasValue ||
            Math.Abs(request.BankBookBalance.Value - request.BankStatementBalance.Value) > 0.01m)
            return BadRequest(new { message = "The bank book balance must match the statement balance before period close." });

        if (!request.CashBookBalance.HasValue || !request.CountedCashBalance.HasValue ||
            Math.Abs(request.CashBookBalance.Value - request.CountedCashBalance.Value) > 0.01m)
            return BadRequest(new { message = "The cash book balance must match the counted cash before period close." });

        var existingClose = await _context.AccountingPeriodCloses
            .AnyAsync(period => period.PeriodType == periodType &&
                                period.PeriodStart == request.PeriodStart.Date &&
                                period.PeriodEnd == request.PeriodEnd.Date);
        if (existingClose)
            return Conflict(new { message = "This accounting period is already closed." });

        var periodStart = request.PeriodStart.Date;
        var periodEnd = request.PeriodEnd.Date;
        var paymentRecords = await _context.Payments.AsNoTracking().ToListAsync();
        var periodPayments = paymentRecords
            .Where(payment => DateTime.TryParse(payment.PaymentDate, out var paymentDate) &&
                              paymentDate.Date >= periodStart && paymentDate.Date <= periodEnd)
            .ToList();

        if (request.ListedExceptions == null || request.ListedExceptions.Any(item => item == null))
            return BadRequest(new { message = "Listed exceptions must be provided as a list." });
        if (request.ListedExceptions.Any(item =>
                string.IsNullOrWhiteSpace(item!.PaymentId) || string.IsNullOrWhiteSpace(item.Reason)))
            return BadRequest(new { message = "Each listed exception requires a payment ID and a reason." });
        if (request.ListedExceptions
            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.PaymentId))
            .Select(item => item!)
            .GroupBy(item => item.PaymentId, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
            return BadRequest(new { message = "A payment exception may only be listed once." });

        var listedExceptions = request.ListedExceptions
            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.PaymentId) && !string.IsNullOrWhiteSpace(item.Reason))
            .Select(item => item!)
            .ToDictionary(item => item.PaymentId, item => item.Reason.Trim(), StringComparer.OrdinalIgnoreCase);
        var exceptionPayments = periodPayments
            .Where(payment => string.Equals(payment.PaymentStatus, "Exception", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var unlistedExceptions = exceptionPayments
            .Where(payment => !listedExceptions.ContainsKey(payment.Id))
            .Select(payment => payment.Id)
            .ToList();
        if (unlistedExceptions.Count > 0)
            return BadRequest(new { message = "Resolve or list every payment exception with a reason before closing the period.", paymentIds = unlistedExceptions });

        var unknownExceptionIds = listedExceptions.Keys
            .Except(exceptionPayments.Select(payment => payment.Id), StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (unknownExceptionIds.Count > 0)
            return BadRequest(new { message = "Only unresolved payment exceptions from this period may be listed.", paymentIds = unknownExceptionIds });

        var pendingPayments = periodPayments
            .Where(payment =>
                !string.Equals(payment.PaymentStatus, "Validated", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(payment.PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(payment.PaymentStatus, "Exception", StringComparison.OrdinalIgnoreCase))
            .Select(payment => payment.Id)
            .ToList();
        if (pendingPayments.Count > 0)
            return BadRequest(new { message = "All non-exception payments in the period must be Finance Manager validated.", paymentIds = pendingPayments });

        var liquidations = await _fomsDb.Liquidations
            .AsNoTracking()
            .Where(item => item.IsDeleted != true)
            .ToListAsync();
        var periodLiquidations = liquidations
            .Where(item =>
            {
                var submittedAt = item.DateSubmitted ?? item.CreatedAt;
                return submittedAt.HasValue && submittedAt.Value.Date >= periodStart && submittedAt.Value.Date <= periodEnd;
            })
            .ToList();
        var pendingLiquidations = periodLiquidations
            .Where(item =>
                !string.Equals(item.Status, "Validated", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(item.Status, "Endorsed", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.LiquidationId)
            .ToList();
        if (pendingLiquidations.Count > 0)
            return BadRequest(new { message = "All liquidations in the period must be validated by the Assistant of Finance Manager.", liquidationIds = pendingLiquidations });

        var receivableTotal = await _context.ReceivableBalances.SumAsync(item => (decimal?)item.BalanceAmount) ?? 0m;
        var agingTotal = await _context.AgingAccounts.SumAsync(item => (decimal?)item.CurrentAmount) ?? 0m;
        if (Math.Abs(receivableTotal - agingTotal) > 0.01m)
            return BadRequest(new
            {
                message = "The AR ledger total must equal the aging report total before period close.",
                receivableTotal,
                agingTotal
            });

        var closedBy = User.FindFirst("employee_number")?.Value
                       ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? User.Identity?.Name
                       ?? string.Empty;
        if (string.IsNullOrWhiteSpace(closedBy))
            return BadRequest(new { message = "The Finance Manager identity could not be determined for the close audit." });

        var close = new AccountingPeriodClose
        {
            PeriodType = periodType,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            ClosedBy = closedBy,
            ClosedAt = DateTime.UtcNow,
            ValidatedPaymentCount = periodPayments.Count - exceptionPayments.Count,
            ValidatedLiquidationCount = periodLiquidations.Count,
            ReceivableLedgerTotal = receivableTotal,
            AgingReportTotal = agingTotal,
            BankBookBalance = request.BankBookBalance!.Value,
            BankStatementBalance = request.BankStatementBalance!.Value,
            BankStatementProofReference = request.BankStatementProofReference.Trim(),
            CashBookBalance = request.CashBookBalance!.Value,
            CountedCashBalance = request.CountedCashBalance!.Value,
            ListedExceptionsJson = JsonSerializer.Serialize(listedExceptions)
        };
        _context.AccountingPeriodCloses.Add(close);
        _context.AuditLogs.Add(new FOMS.Domain.Entities.AuditLog
        {
            UserId = closedBy,
            EntityName = nameof(AccountingPeriodClose),
            EntityId = close.Id,
            Action = "PERIOD_CLOSED",
            Details = $"Closed {request.PeriodType} period {periodStart:yyyy-MM-dd} to {periodEnd:yyyy-MM-dd}; " +
                      $"payments validated: {close.ValidatedPaymentCount}; liquidations validated: {close.ValidatedLiquidationCount}; " +
                      $"exceptions listed: {listedExceptions.Count}; bank statement proof: {close.BankStatementProofReference}."
        });
        await _context.SaveChangesAsync(default);

        return CreatedAtAction(nameof(GetClosedPeriod), new { id = close.Id }, close);
    }

    [HttpPost("{periodCloseId}/corrections")]
    public async Task<IActionResult> RecordCorrection(string periodCloseId, [FromBody] CorrectionEntryRequest request)
    {
        if (!IsFinanceManager())
            return Forbid();

        var period = await _context.AccountingPeriodCloses
            .FirstOrDefaultAsync(item => item.Id == periodCloseId);
        if (period == null)
            return NotFound(new { message = "Closed accounting period not found." });

        if (request.EntryDate.Date <= period.PeriodEnd.Date ||
            string.IsNullOrWhiteSpace(request.Description) ||
            string.IsNullOrWhiteSpace(request.DebitAccount) ||
            string.IsNullOrWhiteSpace(request.CreditAccount) ||
            request.Amount <= 0m ||
            string.Equals(request.DebitAccount.Trim(), request.CreditAccount.Trim(), StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "A correcting entry requires a later date, description, distinct debit/credit accounts, and a positive balanced amount." });

        var createdBy = User.FindFirst("employee_number")?.Value
                        ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                        ?? User.Identity?.Name
                        ?? string.Empty;
        if (string.IsNullOrWhiteSpace(createdBy))
            return BadRequest(new { message = "The Finance Manager identity could not be determined for the correction audit." });

        var correction = new AccountingPeriodCorrectionEntry
        {
            PeriodCloseId = period.Id,
            EntryDate = request.EntryDate.Date,
            Description = request.Description.Trim(),
            DebitAccount = request.DebitAccount.Trim(),
            CreditAccount = request.CreditAccount.Trim(),
            Amount = request.Amount,
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow
        };
        _context.AccountingPeriodCorrectionEntries.Add(correction);
        _context.AuditLogs.Add(new FOMS.Domain.Entities.AuditLog
        {
            UserId = createdBy,
            EntityName = nameof(AccountingPeriodCorrectionEntry),
            EntityId = correction.Id,
            Action = "PERIOD_CORRECTING_ENTRY",
            Details = $"Correction for {period.PeriodType} period {period.PeriodStart:yyyy-MM-dd} to {period.PeriodEnd:yyyy-MM-dd}: " +
                      $"{correction.DebitAccount} Dr / {correction.CreditAccount} Cr {correction.Amount:N2}. {correction.Description}"
        });
        await _context.SaveChangesAsync(default);
        return Ok(correction);
    }

    private bool IsFinanceManager() =>
        string.Equals(User.FindFirst(ClaimTypes.Role)?.Value, "Finance Manager", StringComparison.OrdinalIgnoreCase);

    private bool CanViewPeriodCloses()
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        return string.Equals(role, "Finance Manager", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(role, "Head Accountant", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidPeriod(string periodType, DateTime start, DateTime end)
    {
        if (start.Date > end.Date)
            return false;

        return periodType switch
        {
            string type when string.Equals(type, "Month", StringComparison.OrdinalIgnoreCase) => start.Day == 1 &&
                       start.Year == end.Year && start.Month == end.Month &&
                       end.Day == DateTime.DaysInMonth(end.Year, end.Month),
            string type when string.Equals(type, "Year", StringComparison.OrdinalIgnoreCase) => start.Month == 1 && start.Day == 1 &&
                      end.Month == 12 && end.Day == 31 && start.Year == end.Year,
            _ => false
        };
    }
}

public sealed class ClosePeriodRequest
{
    public string PeriodType { get; set; } = string.Empty;
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public decimal? BankBookBalance { get; set; }
    public decimal? BankStatementBalance { get; set; }
    public string BankStatementProofReference { get; set; } = string.Empty;
    public decimal? CashBookBalance { get; set; }
    public decimal? CountedCashBalance { get; set; }
    public List<PaymentExceptionListing?> ListedExceptions { get; set; } = [];
}

public sealed class PaymentExceptionListing
{
    public string PaymentId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class CorrectionEntryRequest
{
    public DateTime EntryDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public string DebitAccount { get; set; } = string.Empty;
    public string CreditAccount { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
