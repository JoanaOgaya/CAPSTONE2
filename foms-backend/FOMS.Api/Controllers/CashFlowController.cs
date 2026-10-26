using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FOMS.Domain.Entities.SqlServer;
using FOMS.Infrastructure.Persistence;

namespace FOMS.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/cash-flow")]
[Route("api/v1/cashflow")]
[Route("api/v1/cash-flow")]
[Route("cash-flow")]
public class CashFlowController : ApiControllerBase
{
    private readonly FomsDbContext _fomsDb;

    public CashFlowController(FomsDbContext fomsDb)
    {
        _fomsDb = fomsDb;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var inflows = await _fomsDb.CashInflows.AsNoTracking().ToListAsync();
        var outflows = await _fomsDb.CashOutflows.AsNoTracking().ToListAsync();

        var records = new List<object>();
        foreach (var inf in inflows)
        {
            records.Add(new
            {
                id = $"IN-{inf.InflowId}",
                type = "Inflow",
                amount = inf.Amount,
                sourceReference = inf.ReferenceNumber ?? inf.Source ?? $"Inflow {inf.InflowId}",
                date = (inf.TransactionDate ?? inf.CreatedAt ?? DateTime.UtcNow).ToString("yyyy-MM-dd"),
                recordedBy = "Finance Team",
                category = inf.Source ?? "Collection",
                status = inf.Status ?? "Validated"
            });
        }
        foreach (var outf in outflows)
        {
            records.Add(new
            {
                id = !string.IsNullOrEmpty(outf.CashOutflowId) ? outf.CashOutflowId : $"OUT-{outf.OutflowId}",
                type = "Outflow",
                amount = outf.Amount,
                sourceReference = outf.SourceReference ?? outf.Description ?? outf.Category ?? outf.CashOutflowId ?? $"Outflow {outf.OutflowId}",
                date = (outf.TransactionDate ?? outf.CreatedAt ?? DateTime.UtcNow).ToString("yyyy-MM-dd"),
                recordedBy = outf.RecordedBy ?? outf.SubmittedBy ?? "Finance Team",
                category = outf.Category ?? "Disbursement",
                status = outf.Status ?? "Approved"
            });
        }

        return Ok(records);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary()
    {
        var inflow = await _fomsDb.CashInflows.SumAsync(t => (decimal?)t.Amount) ?? 0m;
        var outflow = await _fomsDb.CashOutflows
            .Where(o => o.Status == "Approved" || o.Status == "Approved by Assistant" || o.Status == "Approved by Financial Manager" || o.Status == "Completed")
            .SumAsync(t => (decimal?)t.Amount) ?? 0m;

        return Ok(new { Inflow = inflow, Outflow = outflow });
    }

    [HttpGet("balances")]
    public IActionResult GetBalances()
    {
        var balances = new[]
        {
            new { bank = "BDO Unibank", accountNumber = "**** 4521", balance = 3450000.00m, status = "Active" },
            new { bank = "Bank of the Philippine Islands (BPI)", accountNumber = "**** 8832", balance = 1820500.50m, status = "Active" },
            new { bank = "Metrobank", accountNumber = "**** 9012", balance = 940200.00m, status = "Active" }
        };
        return Ok(balances);
    }
}
