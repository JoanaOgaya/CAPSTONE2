using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FOMS.Domain.Constants;
using FOMS.Domain.Entities.SqlServer;
using FOMS.Infrastructure.Persistence;

namespace FOMS.Api.Controllers;

public class LiquidationDto
{
    public string? Id { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? SubmittedByName { get; set; }
    public string? SubmittedBy { get; set; }
    public string? SubmittedByEmployeeId { get; set; }
    public decimal? TotalAmount { get; set; }
    public decimal? Amount { get; set; }
    public decimal? LeftoverCash { get; set; }
    public decimal? ReimbursementAmount { get; set; }
    public string? Status { get; set; }
    public string? Purpose { get; set; }
    public string? Notes { get; set; }
    public string? Remarks { get; set; }
    public string? Action { get; set; }
}

[Authorize]
[ApiController]
[Route("api/liquidations")]
[Route("api/v1/liquidations")]
[Route("liquidations")]
public class LiquidationsController : ControllerBase
{
    private readonly FomsDbContext _fomsDb;
    private readonly ApplicationDbContext? _appDb;

    public LiquidationsController(FomsDbContext fomsDb, ApplicationDbContext? appDb = null)
    {
        _fomsDb = fomsDb;
        _appDb = appDb;
    }

    private ApplicationDbContext? GetAppDb() =>
        _appDb ?? HttpContext?.RequestServices?.GetService<ApplicationDbContext>();

    private (bool isAuthenticated, string role, string username, string empId) GetCallerInfo()
    {
        if (User?.Identity?.IsAuthenticated != true)
        {
            return (false, string.Empty, string.Empty, string.Empty);
        }

        var role = FomsRoles.GetClaimRole(User);
        var username = FomsRoles.GetClaimUsername(User);
        var empId = FomsRoles.GetClaimEmployeeId(User);
        return (true, role, username, empId);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var (isAuthenticated, role, _, _) = GetCallerInfo();
        if (!isAuthenticated)
            return Unauthorized(new { message = "Authentication required." });
        if (!FomsRoles.IsRole(role, FomsRoles.AllCanonicalRoles))
            return StatusCode(403, new { message = "Forbidden: Missing or unrecognized role claim." });

        var query = _fomsDb.Liquidations.AsNoTracking().Where(l => l.IsDeleted != true && l.Status != "Deleted");
        var total = await query.CountAsync();
        var liquidations = await query
            .OrderByDescending(l => l.DateSubmitted ?? l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var liqIds = liquidations.Select(l => l.LiquidationId).ToList();
        var lineItems = await _fomsDb.LiquidationLineItems.AsNoTracking()
            .Where(li => liqIds.Contains(li.LiquidationId))
            .ToListAsync();
        var docs = await _fomsDb.LiquidationDocuments.AsNoTracking()
            .Where(d => liqIds.Contains(d.LiquidationId))
            .ToListAsync();

        var items = liquidations.Select(l => new
        {
            id = l.LiquidationId.ToString(),
            liquidationId = l.LiquidationId,
            reference = l.ReferenceNumber ?? $"LIQ-{l.LiquidationId:D4}",
            referenceNumber = l.ReferenceNumber ?? $"LIQ-{l.LiquidationId:D4}",
            submittedBy = l.SubmittedByName ?? $"Employee {l.SubmittedBy}",
            submittedByName = l.SubmittedByName ?? $"Employee {l.SubmittedBy}",
            submittedByEmployeeId = l.SubmittedByEmployeeId,
            submittedAt = (l.DateSubmitted ?? l.CreatedAt ?? DateTime.UtcNow).ToString("o"),
            dateSubmitted = (l.DateSubmitted ?? l.CreatedAt ?? DateTime.UtcNow).ToString("yyyy-MM-dd"),
            amount = l.TotalAmount,
            totalAmount = l.TotalAmount,
            status = l.Status ?? "Pending Validation",
            leftoverCash = l.LeftoverCash ?? 0m,
            reimbursementAmount = l.ReimbursementAmount ?? 0m,
            expenses = lineItems.Where(li => li.LiquidationId == l.LiquidationId).Select(li => new
            {
                id = li.LineItemId.ToString(),
                type = "Expense",
                description = li.Description,
                amount = li.Amount
            }).ToList(),
            documents = docs.Where(d => d.LiquidationId == l.LiquidationId).Select(d => new
            {
                name = Path.GetFileName(d.FilePath ?? "receipt.pdf"),
                url = d.FilePath ?? "#",
                size = "1.2 MB"
            }).ToList()
        }).ToList();

        return Ok(new
        {
            items,
            total,
            page,
            pageSize
        });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var (isAuthenticated, role, _, _) = GetCallerInfo();
        if (!isAuthenticated)
            return Unauthorized(new { message = "Authentication required." });
        if (!FomsRoles.IsRole(role, FomsRoles.AllCanonicalRoles))
            return StatusCode(403, new { message = "Forbidden: Missing or unrecognized role claim." });

        var item = await _fomsDb.Liquidations.AsNoTracking().FirstOrDefaultAsync(l => l.LiquidationId == id && l.IsDeleted != true);
        if (item == null) return NotFound(new { message = "Liquidation not found" });

        var lineItems = await _fomsDb.LiquidationLineItems.Where(li => li.LiquidationId == id).AsNoTracking().ToListAsync();
        var docs = await _fomsDb.LiquidationDocuments.Where(d => d.LiquidationId == id).AsNoTracking().ToListAsync();

        return Ok(new
        {
            id = item.LiquidationId.ToString(),
            item.LiquidationId,
            reference = item.ReferenceNumber ?? $"LIQ-{item.LiquidationId:D4}",
            referenceNumber = item.ReferenceNumber ?? $"LIQ-{item.LiquidationId:D4}",
            submittedBy = item.SubmittedByName ?? $"Employee {item.SubmittedBy}",
            submittedByName = item.SubmittedByName ?? $"Employee {item.SubmittedBy}",
            submittedByEmployeeId = item.SubmittedByEmployeeId,
            amount = item.TotalAmount,
            totalAmount = item.TotalAmount,
            leftoverCash = item.LeftoverCash ?? 0m,
            reimbursementAmount = item.ReimbursementAmount ?? 0m,
            status = item.Status ?? "Pending Validation",
            dateSubmitted = (item.DateSubmitted ?? item.CreatedAt ?? DateTime.UtcNow).ToString("yyyy-MM-dd"),
            lineItems,
            expenses = lineItems.Select(li => new
            {
                id = li.LineItemId.ToString(),
                type = "Expense",
                description = li.Description,
                amount = li.Amount
            }).ToList(),
            documents = docs.Select(d => new
            {
                name = Path.GetFileName(d.FilePath ?? "receipt.pdf"),
                url = d.FilePath ?? "#",
                size = "1.2 MB"
            }).ToList()
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] LiquidationDto dto)
    {
        var (isAuthenticated, role, username, empId) = GetCallerInfo();
        if (!isAuthenticated)
            return Unauthorized(new { message = "Authentication required." });

        if (!FomsRoles.IsRole(role, FomsRoles.AssistantFinanceManager, FomsRoles.HeadAccountant))
        {
            return StatusCode(403, new { message = $"Forbidden: Role '{role}' is not authorized to create liquidation vouchers. Only Assistant of Finance Manager (EMP-004) or Head Accountant (EMP-002) may initiate liquidations." });
        }

        var item = new Liquidation
        {
            ReferenceNumber = dto.ReferenceNumber ?? $"LIQ-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..4].ToUpper()}",
            SubmittedByName = dto.SubmittedByName ?? dto.SubmittedBy ?? (!string.IsNullOrWhiteSpace(username) ? username : "Employee"),
            SubmittedByEmployeeId = !string.IsNullOrWhiteSpace(dto.SubmittedByEmployeeId) ? dto.SubmittedByEmployeeId : (!string.IsNullOrWhiteSpace(empId) ? empId : "EMP-004"),
            TotalAmount = dto.TotalAmount ?? dto.Amount ?? 0m,
            LeftoverCash = dto.LeftoverCash ?? 0m,
            ReimbursementAmount = dto.ReimbursementAmount ?? 0m,
            Status = "Pending Validation",
            CreatedAt = DateTime.UtcNow,
            DateSubmitted = DateTime.UtcNow,
            IsDeleted = false
        };

        _fomsDb.Liquidations.Add(item);
        await _fomsDb.SaveChangesAsync();

        _fomsDb.AuditLogs.Add(new AuditLog
        {
            action = "CREATE_LIQUIDATION",
            module = "Liquidations",
            role = role,
            affected_record_id = item.LiquidationId,
            remarks = $"Submitted liquidation {item.ReferenceNumber} for {item.SubmittedByName} (EmpId: {item.SubmittedByEmployeeId}): ₱{item.TotalAmount:N2}",
            created_at = DateTime.UtcNow
        });
        await _fomsDb.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = item.LiquidationId }, item);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] LiquidationDto dto)
    {
        var (isAuthenticated, role, _, empId) = GetCallerInfo();
        if (!isAuthenticated)
            return Unauthorized(new { message = "Authentication required." });

        if (!FomsRoles.IsRole(role, FomsRoles.AssistantFinanceManager, FomsRoles.FinanceManager, FomsRoles.HeadAccountant))
        {
            return StatusCode(403, new { message = $"Forbidden: Role '{role}' is not authorized to update or validate liquidations." });
        }

        var item = await _fomsDb.Liquidations.FirstOrDefaultAsync(l => l.LiquidationId == id && l.IsDeleted != true);
        if (item == null) return NotFound(new { message = "Liquidation not found" });

        if (dto.TotalAmount.HasValue || dto.Amount.HasValue)
            item.TotalAmount = dto.TotalAmount ?? dto.Amount!.Value;

        if (dto.LeftoverCash.HasValue)
            item.LeftoverCash = dto.LeftoverCash.Value;

        if (dto.ReimbursementAmount.HasValue)
            item.ReimbursementAmount = dto.ReimbursementAmount.Value;

        if (!string.IsNullOrEmpty(dto.Status))
        {
            var targetStatus = dto.Status.Trim();
            if (string.Equals(targetStatus, "Endorsed", StringComparison.OrdinalIgnoreCase))
            {
                return await Act(id, "endorse", dto);
            }
            if (string.Equals(targetStatus, "Approved", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(targetStatus, "Validated", StringComparison.OrdinalIgnoreCase))
            {
                return await Act(id, "approve", dto);
            }
            if (string.Equals(targetStatus, "Rejected", StringComparison.OrdinalIgnoreCase))
            {
                return await Act(id, "reject", dto);
            }
            if (string.Equals(targetStatus, "Returned", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(targetStatus, "For Correction", StringComparison.OrdinalIgnoreCase))
            {
                return await Act(id, "return", dto);
            }
        }

        await _fomsDb.SaveChangesAsync();
        return Ok(item);
    }

    [HttpPost("{id}/{actionName}")]
    public async Task<IActionResult> Act(int id, string actionName, [FromBody] LiquidationDto? dto)
    {
        var (isAuthenticated, role, username, empId) = GetCallerInfo();
        if (!isAuthenticated)
            return Unauthorized(new { message = "Authentication required." });

        if (!FomsRoles.IsRole(role, FomsRoles.AssistantFinanceManager, FomsRoles.FinanceManager, FomsRoles.HeadAccountant))
        {
            return StatusCode(403, new { message = $"Forbidden: Role '{role}' is not authorized to act on liquidations." });
        }

        var item = await _fomsDb.Liquidations.FirstOrDefaultAsync(l => l.LiquidationId == id && l.IsDeleted != true);
        if (item == null) return NotFound(new { message = "Liquidation not found" });

        string effectiveAction = actionName.ToLowerInvariant();
        if (effectiveAction == "act")
        {
            var raw = dto?.Action ?? dto?.Status ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(raw))
            {
                effectiveAction = raw.ToLowerInvariant();
            }
        }

        var submitterEmpId = !string.IsNullOrWhiteSpace(item.SubmittedByEmployeeId)
            ? item.SubmittedByEmployeeId
            : item.SubmittedByName;

        bool isTier1Action = string.Equals(effectiveAction, "endorse", StringComparison.OrdinalIgnoreCase) ||
                             (string.Equals(effectiveAction, "validate", StringComparison.OrdinalIgnoreCase) && !FomsRoles.IsFinanceManager(role));

        bool isTier2Action = string.Equals(effectiveAction, "approve", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(effectiveAction, "signoff", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(effectiveAction, "sign_off", StringComparison.OrdinalIgnoreCase) ||
                             (string.Equals(effectiveAction, "validate", StringComparison.OrdinalIgnoreCase) && FomsRoles.IsFinanceManager(role));

        if (isTier1Action)
        {
            // Strict Tier 1: Assistant of Finance Manager (or Head Accountant) endorsement
            if (!FomsRoles.IsAssistantFinanceManager(role) && !FomsRoles.IsHeadAccountant(role))
            {
                return StatusCode(403, new { message = $"Forbidden: Role '{role}' is not authorized to endorse liquidations. Only Assistant of Finance Manager (EMP-004) can endorse." });
            }

            if (FomsRoles.IsSameEmployeeId(submitterEmpId, empId))
            {
                return StatusCode(403, new { message = "Separation of duties violation: Submitter cannot endorse their own liquidation." });
            }

            if (!string.Equals(item.Status, "Pending Validation", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(item.Status, "Submitted", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = $"Cannot endorse liquidation in status '{item.Status}'. Must be 'Pending Validation' or 'Submitted'." });
            }

            item.Status = "Endorsed";
            item.ValidatedAt = DateTime.UtcNow;
            item.ValidatedBy = FomsRoles.IsHeadAccountant(role) ? 2 : 4;
            if (!string.IsNullOrWhiteSpace(dto?.Notes ?? dto?.Remarks))
            {
                item.Remarks = dto?.Notes ?? dto?.Remarks;
            }

            // CRITICAL: NO cash outflow, NO cash inflow, NO ledger entries on Tier 1 endorsement!
            await _fomsDb.SaveChangesAsync();

            _fomsDb.AuditLogs.Add(new AuditLog
            {
                action = "ENDORSE_LIQUIDATION",
                module = "Liquidations",
                role = role,
                affected_record_id = item.LiquidationId,
                remarks = $"Tier 1 verified supporting documents and endorsed liquidation {item.ReferenceNumber} to Finance Manager.",
                created_at = DateTime.UtcNow
            });
            await _fomsDb.SaveChangesAsync();

            return Ok(item);
        }

        if (isTier2Action)
        {
            // Strict Tier 2: Finance Manager final sign-off
            if (!FomsRoles.IsFinanceManager(role))
            {
                return StatusCode(403, new { message = $"Forbidden: Role '{role}' is not authorized to approve liquidations. Only Finance Manager (EMP-001) can perform final sign-off." });
            }

                if (FomsRoles.IsSameEmployeeId(submitterEmpId, empId))
                {
                    return StatusCode(403, new { message = "Separation of duties violation: Submitter cannot approve their own liquidation." });
                }

                // STRICT SKIPPED TIER CHECK:
                // Must be Endorsed by Assistant FM first!
                if (!string.Equals(item.Status, "Endorsed", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(item.Status, "Approved", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(item.Status, "Validated", StringComparison.OrdinalIgnoreCase))
                    {
                        // Idempotent: already approved
                        return Ok(item);
                    }

                    return BadRequest(new
                    {
                        message = "Two-tier endorsement required: Liquidation must be endorsed by Assistant Finance Manager (EMP-004) before final Finance Manager sign-off."
                    });
                }

                item.Status = "Approved";
                item.ValidatedAt = DateTime.UtcNow;
                item.ValidatedBy = 1;
                                                if (!string.IsNullOrWhiteSpace(dto?.Notes ?? dto?.Remarks))
                {
                    item.Remarks = dto?.Notes ?? dto?.Remarks;
                }

                // Recognize Cash Outflow, Inflow in Treasury and Journal Entries in Ledger ONCE ONLY!
                await PostLiquidationLedgerAndTreasuryAsync(item, empId);

                await _fomsDb.SaveChangesAsync();

                _fomsDb.AuditLogs.Add(new AuditLog
                {
                    action = "LIQUIDATION_APPROVED",
                    module = "Liquidations",
                    role = role,
                    affected_record_id = item.LiquidationId,
                    remarks = $"Finance Manager signed off on liquidation {item.ReferenceNumber}. Outflow/inflow and ledger adjustments posted to Treasury.",
                    created_at = DateTime.UtcNow
                });
                await _fomsDb.SaveChangesAsync();

                return Ok(item);
            }

        if (effectiveAction is "reject" or "return" or "for_correction" or "for-correction")
        {

                if (!FomsRoles.IsAssistantFinanceManager(role) && !FomsRoles.IsFinanceManager(role) && !FomsRoles.IsHeadAccountant(role))
                {
                    return StatusCode(403, new { message = $"Forbidden: Role '{role}' is not authorized to reject or return liquidations." });
                }

                if (FomsRoles.IsSameEmployeeId(submitterEmpId, empId))
                {
                    return StatusCode(403, new { message = "Separation of duties violation: Submitter cannot reject or return their own liquidation." });
                }

                item.Status = effectiveAction == "reject" ? "Rejected" : "Returned";
                item.ValidatedAt = DateTime.UtcNow;
                if (!string.IsNullOrWhiteSpace(dto?.Notes ?? dto?.Remarks))
                {
                    item.Remarks = dto?.Notes ?? dto?.Remarks;
                }

                // CRITICAL: NO cash outflow, NO cash inflow, NO ledger entries on rejection/return!
                await _fomsDb.SaveChangesAsync();

                _fomsDb.AuditLogs.Add(new AuditLog
                {
                    action = $"LIQUIDATION_{item.Status.ToUpperInvariant()}",
                    module = "Liquidations",
                    role = role,
                    affected_record_id = item.LiquidationId,
                    remarks = $"Liquidation {item.ReferenceNumber} marked as {item.Status}. No ledger or treasury entries posted.",
                    created_at = DateTime.UtcNow
                });
                await _fomsDb.SaveChangesAsync();

                return Ok(item);
        }

        return BadRequest(new { message = $"Unrecognized liquidation action '{actionName}'." });
    }

    private async Task PostLiquidationLedgerAndTreasuryAsync(Liquidation item, string approverEmpId)
    {
        var appDb = GetAppDb();

        // 1. Post Cash Inflow in MSSQL & Treasury if leftover cash is returned (once only)
        if ((item.LeftoverCash ?? 0) > 0)
        {
            var existingInflow = await _fomsDb.CashInflows
                .FirstOrDefaultAsync(i => i.ReferenceNumber == item.ReferenceNumber);
            if (existingInflow == null)
            {
                _fomsDb.CashInflows.Add(new CashInflow
                {
                    Amount = item.LeftoverCash!.Value,
                    Source = $"Liquidation {item.ReferenceNumber} Cash Return",
                    ReferenceNumber = item.ReferenceNumber,
                    Status = "Completed",
                    TransactionDate = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                });
            }

            if (appDb != null)
            {
                var existingTreasuryInflow = await appDb.CashFlowTransactions
                    .FirstOrDefaultAsync(c => c.ReferenceNo == item.ReferenceNumber && c.Type == "Inflow");
                if (existingTreasuryInflow == null)
                {
                    appDb.CashFlowTransactions.Add(new FOMS.Domain.Entities.CashFlowTransaction
                    {
                        Date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                        TransactionDate = DateTime.UtcNow,
                        Type = "Inflow",
                        Category = "Liquidation Leftover Return",
                        Amount = item.LeftoverCash!.Value,
                        ReferenceNo = item.ReferenceNumber ?? $"LIQ-{item.LiquidationId}",
                        Description = $"Excess cash returned from Liquidation {item.ReferenceNumber} ({item.SubmittedByName}). Approved by: {approverEmpId}."
                    });
                }
            }
        }

        // 2. Post Cash Outflow in MSSQL & Treasury if courier is due reimbursement (once only)
        if ((item.ReimbursementAmount ?? 0) > 0)
        {
            var existingOutflow = await _fomsDb.CashOutflows
                .FirstOrDefaultAsync(o => o.SourceReference == item.ReferenceNumber);
            if (existingOutflow == null)
            {
                _fomsDb.CashOutflows.Add(new CashOutflow
                {
                    CashOutflowId = $"CO-LIQ-{item.LiquidationId:D5}",
                    SourceReference = item.ReferenceNumber,
                    RecordedBy = approverEmpId,
                    Category = "Courier Expense Reimbursement",
                    Description = $"Liquidation {item.ReferenceNumber} Reimbursement for {item.SubmittedByName ?? "Courier"}",
                    Amount = item.ReimbursementAmount!.Value,
                    Status = "Completed",
                    OutflowDate = DateTime.UtcNow,
                    TransactionDate = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                });
            }

            if (appDb != null)
            {
                var existingTreasuryOutflow = await appDb.CashFlowTransactions
                    .FirstOrDefaultAsync(c => c.ReferenceNo == item.ReferenceNumber && c.Type == "Outflow");
                if (existingTreasuryOutflow == null)
                {
                    appDb.CashFlowTransactions.Add(new FOMS.Domain.Entities.CashFlowTransaction
                    {
                        Date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                        TransactionDate = DateTime.UtcNow,
                        Type = "Outflow",
                        Category = "Courier Expense Reimbursement",
                        Amount = item.ReimbursementAmount!.Value,
                        ReferenceNo = item.ReferenceNumber ?? $"LIQ-{item.LiquidationId}",
                        Description = $"Reimbursement for Liquidation {item.ReferenceNumber} ({item.SubmittedByName}). Approved by: {approverEmpId}."
                    });
                }
            }
        }

        // 3. Update matching Cash Advance to Liquidated
        if (item.CashAdvanceId.HasValue)
        {
            var adv = await _fomsDb.CashAdvances.FirstOrDefaultAsync(a => a.AdvanceId == item.CashAdvanceId.Value.ToString() || a.AdvanceId == $"CA-{item.CashAdvanceId}");
            if (adv != null)
            {
                adv.Status = "Liquidated";
            }
        }
        else
        {
            var adv = await _fomsDb.CashAdvances.FirstOrDefaultAsync(a => a.CourierName == item.SubmittedByName && a.Status != "Liquidated");
            if (adv != null)
            {
                adv.Status = "Liquidated";
            }
        }

        // 4. Ledger Adjustments (General Ledger & Journal Entries) - once only
        if (appDb != null)
        {
            string entryNum = $"JE-LIQ-{item.LiquidationId:D5}";
            var existingJe = await appDb.JournalEntries
                .Include(j => j.Entries)
                .FirstOrDefaultAsync(j => j.EntryNumber == entryNum || (item.ReferenceNumber != null && j.Description.Contains(item.ReferenceNumber)));

            if (existingJe == null)
            {
                await EnsureChartOfAccountAsync(appDb, "101", "Cash on Hand / In Bank", "Asset", "Debit");
                await EnsureChartOfAccountAsync(appDb, "105", "Advances to Officers and Employees", "Asset", "Debit");
                await EnsureChartOfAccountAsync(appDb, "501", "Operating & Delivery Expenses", "Expense", "Debit");

                var je = new FOMS.Domain.Entities.JournalEntry
                {
                    EntryNumber = entryNum,
                    Date = DateTime.UtcNow,
                    Description = $"Liquidation settlement and expense endorsement for {item.ReferenceNumber} ({item.SubmittedByName}). Approved by: {approverEmpId}.",
                    Posted = true
                };

                decimal totalExpenses = item.TotalAmount;
                decimal leftover = item.LeftoverCash ?? 0m;
                decimal reimb = item.ReimbursementAmount ?? 0m;
                decimal advanceSettled = totalExpenses + leftover - reimb;
                if (advanceSettled < 0) advanceSettled = 0m;

                if (totalExpenses > 0)
                {
                    je.Entries.Add(new FOMS.Domain.Entities.GeneralLedgerEntry
                    {
                        JournalEntry = je,
                        AccountId = "501",
                        Debit = totalExpenses,
                        Credit = 0m
                    });
                }

                if (leftover > 0)
                {
                    je.Entries.Add(new FOMS.Domain.Entities.GeneralLedgerEntry
                    {
                        JournalEntry = je,
                        AccountId = "101",
                        Debit = leftover,
                        Credit = 0m
                    });
                }

                if (advanceSettled > 0)
                {
                    je.Entries.Add(new FOMS.Domain.Entities.GeneralLedgerEntry
                    {
                        JournalEntry = je,
                        AccountId = "105",
                        Debit = 0m,
                        Credit = advanceSettled
                    });
                }

                if (reimb > 0)
                {
                    je.Entries.Add(new FOMS.Domain.Entities.GeneralLedgerEntry
                    {
                        JournalEntry = je,
                        AccountId = "101",
                        Debit = 0m,
                        Credit = reimb
                    });
                }

                // Guarantee double-entry balance: total debits == total credits
                decimal totalDebits = je.Entries.Sum(e => e.Debit);
                decimal totalCredits = je.Entries.Sum(e => e.Credit);
                if (totalDebits != totalCredits)
                {
                    decimal diff = totalDebits - totalCredits;
                    var last = je.Entries.LastOrDefault();
                    if (last != null)
                    {
                        if (diff > 0) last.Credit += diff;
                        else last.Debit += (-diff);
                    }
                }

                appDb.JournalEntries.Add(je);
                await appDb.SaveChangesAsync();
            }
        }
    }

    private static async Task EnsureChartOfAccountAsync(ApplicationDbContext appDb, string id, string name, string type, string normalBalance)
    {
        var acc = await appDb.ChartOfAccounts.FirstOrDefaultAsync(a => a.Id == id || a.AccountCode == id);
        if (acc == null)
        {
            appDb.ChartOfAccounts.Add(new FOMS.Domain.Entities.ChartOfAccount
            {
                Id = id,
                AccountCode = id,
                Name = name,
                Type = type,
                NormalBalance = normalBalance
            });
            await appDb.SaveChangesAsync();
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var (isAuthenticated, role, _, _) = GetCallerInfo();
        if (!isAuthenticated)
            return Unauthorized(new { message = "Authentication required." });
        if (!FomsRoles.IsRole(role, FomsRoles.AssistantFinanceManager, FomsRoles.FinanceManager, FomsRoles.HeadAccountant))
            return StatusCode(403, new { message = $"Forbidden: Role '{role}' is not authorized to delete liquidations." });

        var item = await _fomsDb.Liquidations.FirstOrDefaultAsync(l => l.LiquidationId == id && l.IsDeleted != true);
        if (item == null) return NotFound(new { message = "Liquidation not found" });

        item.IsDeleted = true;
        item.Status = "Deleted";
        await _fomsDb.SaveChangesAsync();

        _fomsDb.AuditLogs.Add(new AuditLog
        {
            action = "DELETE_LIQUIDATION",
            module = "Liquidations",
            role = role,
            affected_record_id = item.LiquidationId,
            remarks = $"Soft-deleted liquidation {item.ReferenceNumber}.",
            created_at = DateTime.UtcNow
        });
        await _fomsDb.SaveChangesAsync();

        return Ok(new { success = true, message = "Liquidation soft-deleted successfully." });
    }
}
