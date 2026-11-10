using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FOMS.Application.Features;
using FOMS.Application.Interfaces;
using FOMS.Domain.Entities;
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
    private readonly IRepository<CashFlowTransaction> _repository;
    private readonly IApplicationDbContext _context;
    private readonly FomsDbContext _fomsDb;

    public CashFlowController(IRepository<CashFlowTransaction> repository, IApplicationDbContext context, FomsDbContext fomsDb)
    {
        _repository = repository;
        _context = context;
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
            .Where(o => o.Status == "Approved" || o.Status == "Approved by Assistant" || o.Status == "Approved by Finance Manager" || o.Status == "Completed" || o.Status == "Released")
            .SumAsync(t => (decimal?)t.Amount) ?? 0m;

        return Ok(new { Inflow = inflow, Outflow = outflow });
    }

    [HttpGet("net-cash")]
    public async Task<IActionResult> GetNetCash([FromServices] IFinancialStatementService statementService)
    {
        var now = DateTime.UtcNow;
        var cf = await statementService.GenerateCashFlowStatementAsync(now.Year, "Month", now.Month);
        return Ok(new
        {
            beginningBalance = cf.BeginningCash,
            validatedInflows = new
            {
                clientPayments = cf.InflowsFromClientPayments,
                returnedAdvances = cf.InflowsFromReturnedAdvances,
                total = cf.TotalOperatingInflows
            },
            validatedOutflows = new
            {
                cashAdvanceReleases = cf.OutflowsCashAdvancesReleased,
                directDisbursements = cf.OutflowsDirectDisbursements,
                paidReimbursements = cf.OutflowsPaidReimbursements,
                total = cf.TotalOperatingOutflows
            },
            netCash = cf.EndingNetCash,
            cashAccounts = new[]
            {
                new { accountCode = "1010", accountName = "Cash on Hand", accountType = "Asset" },
                new { accountCode = "1020", accountName = "Cash in Bank", accountType = "Asset" }
            }
        });
    }

    [HttpGet("balances")]
    public async Task<IActionResult> GetBalances()
    {
        var balances = await Mediator.Send(new CashFlowFeatures.GetBankBalancesQuery());
        return Ok(balances);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CashFlowTransaction request)
    {
        var addedBy = User.FindFirst("name")?.Value ?? User.FindFirst(ClaimTypes.Name)?.Value ?? "Unknown User";
        var command = new CashFlowFeatures.AddCashFlowCommand(
            Type: request.Type,
            Category: request.Category,
            Amount: request.Amount,
            ReferenceNo: request.ReferenceNo,
            Description: request.Description,
            AddedBy: addedBy
        );
        var result = await Mediator.Send(command);
        return CreatedAtAction(nameof(GetAll), new { id = result.Id }, result);
    }
}
