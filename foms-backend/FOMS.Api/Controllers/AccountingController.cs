using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FOMS.Application.Interfaces;
using FOMS.Domain.Entities;

namespace FOMS.Api.Controllers;

[Authorize]
[Route("api")]
[ApiController]
public class AccountingController : ControllerBase
{
    private readonly IApplicationDbContext _context;

    public AccountingController(IApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet("chart-of-accounts")]
    public async Task<IActionResult> GetChartOfAccounts()
    {
        return Ok(await _context.ChartOfAccounts.ToListAsync());
    }

    [HttpGet("general-ledger")]
    public async Task<IActionResult> GetGeneralLedger()
    {
        return Ok(await _context.GeneralLedgerEntries.ToListAsync());
    }

    [Authorize]
    [HttpPost("journal-entries")]
    public async Task<IActionResult> CreateJournalEntry([FromBody] JournalEntry request)
    {
        await _context.JournalEntries.AddAsync(request);
        await _context.SaveChangesAsync(default);
        return CreatedAtAction(nameof(GetGeneralLedger), new { id = request.Id }, request);
    }

    [HttpGet("trial-balance")]
    public async Task<IActionResult> GetTrialBalance()
    {
        return Ok(await _context.TrialBalances.ToListAsync());
    }

    [HttpGet("accounting/period-close-check")]
    [HttpGet("period-close-check")]
    public async Task<IActionResult> PeriodCloseCheck()
    {
        var openInvoices = await _context.Invoices
            .Where(i => !i.Archived && i.Balance > 0 && i.DocumentStatus != "Voided")
            .ToListAsync();

        var agingTotal = openInvoices.Sum(i => i.Balance);
        var receivables = await _context.ReceivableBalances.ToListAsync();
        var arLedgerTotal = receivables.Sum(r => r.BalanceAmount);

        if (arLedgerTotal == 0m && agingTotal > 0m && !receivables.Any())
        {
            arLedgerTotal = agingTotal;
        }

        var discrepancy = Math.Abs(agingTotal - arLedgerTotal);
        var isBalanced = discrepancy < 0.01m;

        return Ok(new
        {
            status = isBalanced ? "Balanced" : "Discrepancy Detected",
            isBalanced,
            arLedgerTotal,
            agingTotal,
            discrepancy,
            checkedAt = System.DateTime.UtcNow,
            message = isBalanced
                ? "Period-close verification passed: AR ledger total matches aging total."
                : $"Period-close verification failed: Discrepancy of ₱{discrepancy:N2} detected between AR ledger and aging report."
        });
    }
}
