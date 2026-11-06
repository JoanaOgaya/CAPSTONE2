using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FOMS.Application.DTOs;
using FOMS.Application.Interfaces;

namespace FOMS.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/finance/statements")]
[Route("api/financial-statements")]
public class FinancialStatementsController : ControllerBase
{
    private readonly IFinancialStatementService _statementService;

    public FinancialStatementsController(IFinancialStatementService statementService)
    {
        _statementService = statementService;
    }

    private (string userId, string role) GetUserContext()
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value
                ?? User.FindFirst("role")?.Value
                ?? "Finance Manager";

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? User.FindFirst("sub")?.Value
                  ?? User.FindFirst("name")?.Value
                  ?? "EMP-001";

        return (userId, role);
    }

    private IActionResult? ValidateHeadAccountantExclusion()
    {
        var (_, role) = GetUserContext();
        if (string.Equals(role, "Head Accountant", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(403, new
            {
                success = false,
                message = "Head Accountant is not authorized for financial statements approvals, go-live opening balances, or executive review."
            });
        }
        return null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. GO-LIVE OPENING BALANCES (FIN-20)
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("opening-balances")]
    [HttpGet("/api/finance/opening-balances")]
    public async Task<IActionResult> GetOpeningBalances()
    {
        try
        {
            var balances = await _statementService.GetOpeningBalancesAsync();
            return Ok(new { success = true, data = balances });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("opening-balances")]
    [HttpPost("/api/finance/opening-balances")]
    public async Task<IActionResult> SetOpeningBalances([FromBody] OpeningBalanceBatchDto batch)
    {
        var exclusion = ValidateHeadAccountantExclusion();
        if (exclusion != null) return exclusion;

        var (userId, role) = GetUserContext();
        try
        {
            var results = await _statementService.SetOpeningBalancesAsync(batch, userId, role);
            return Ok(new
            {
                success = true,
                message = "Go-live opening balances successfully recorded and balanced.",
                data = results
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { success = false, message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. STATEMENTS GENERATION
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("income")]
    public async Task<IActionResult> GetIncomeStatement(
        [FromQuery] int? fiscalYear,
        [FromQuery] string? periodType,
        [FromQuery] int? periodNumber)
    {
        try
        {
            int year = fiscalYear ?? DateTime.UtcNow.Year;
            string type = string.IsNullOrWhiteSpace(periodType) ? "Year" : periodType;
            int number = periodNumber ?? 1;

            var result = await _statementService.GenerateIncomeStatementAsync(year, type, number);
            return Ok(new { success = true, data = result });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("balance-sheet")]
    public async Task<IActionResult> GetBalanceSheet(
        [FromQuery] int? fiscalYear,
        [FromQuery] string? periodType,
        [FromQuery] int? periodNumber)
    {
        try
        {
            int year = fiscalYear ?? DateTime.UtcNow.Year;
            string type = string.IsNullOrWhiteSpace(periodType) ? "Year" : periodType;
            int number = periodNumber ?? 1;

            var result = await _statementService.GenerateBalanceSheetAsync(year, type, number);
            return Ok(new { success = true, data = result });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("cash-flow")]
    public async Task<IActionResult> GetCashFlowStatement(
        [FromQuery] int? fiscalYear,
        [FromQuery] string? periodType,
        [FromQuery] int? periodNumber)
    {
        try
        {
            int year = fiscalYear ?? DateTime.UtcNow.Year;
            string type = string.IsNullOrWhiteSpace(periodType) ? "Year" : periodType;
            int number = periodNumber ?? 1;

            var result = await _statementService.GenerateCashFlowStatementAsync(year, type, number);
            return Ok(new { success = true, data = result });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("equity")]
    public async Task<IActionResult> GetEquityStatement(
        [FromQuery] int? fiscalYear,
        [FromQuery] string? periodType,
        [FromQuery] int? periodNumber)
    {
        try
        {
            int year = fiscalYear ?? DateTime.UtcNow.Year;
            string type = string.IsNullOrWhiteSpace(periodType) ? "Year" : periodType;
            int number = periodNumber ?? 1;

            var result = await _statementService.GenerateEquityStatementAsync(year, type, number);
            return Ok(new { success = true, data = result });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("notes")]
    public async Task<IActionResult> GetNotes(
        [FromQuery] int? fiscalYear,
        [FromQuery] string? periodType,
        [FromQuery] int? periodNumber)
    {
        try
        {
            int year = fiscalYear ?? DateTime.UtcNow.Year;
            string type = string.IsNullOrWhiteSpace(periodType) ? "Year" : periodType;
            int number = periodNumber ?? 1;

            var result = await _statementService.GenerateNotesAsync(year, type, number);
            return Ok(new { success = true, data = result });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("afs-package")]
    public async Task<IActionResult> GetAfsPackage(
        [FromQuery] int? fiscalYear,
        [FromQuery] string? periodType,
        [FromQuery] int? periodNumber)
    {
        try
        {
            int year = fiscalYear ?? DateTime.UtcNow.Year;
            string type = string.IsNullOrWhiteSpace(periodType) ? "Year" : periodType;
            int number = periodNumber ?? 1;

            var result = await _statementService.GenerateAfsPackageAsync(year, type, number);
            return Ok(new { success = true, data = result });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. REVIEWS & APPROVALS
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("review")]
    public async Task<IActionResult> ReviewAfsPackage([FromBody] StatementPeriodRequestDto request)
    {
        var exclusion = ValidateHeadAccountantExclusion();
        if (exclusion != null) return exclusion;

        var (userId, role) = GetUserContext();
        try
        {
            var result = await _statementService.ReviewAfsPackageAsync(
                request.FiscalYear, request.PeriodType, request.PeriodNumber, userId, role);
            return Ok(new
            {
                success = true,
                message = "Financial statement package reviewed by Finance Manager.",
                data = result
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("approve")]
    public async Task<IActionResult> ApproveAfsPackage([FromBody] StatementPeriodRequestDto request)
    {
        var exclusion = ValidateHeadAccountantExclusion();
        if (exclusion != null) return exclusion;

        var (userId, role) = GetUserContext();
        try
        {
            var result = await _statementService.ApproveAfsPackageAsync(
                request.FiscalYear, request.PeriodType, request.PeriodNumber, userId, role);
            return Ok(new
            {
                success = true,
                message = "Annual financial statement package approved by CEO/President.",
                data = result
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { success = false, message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("attach-audited")]
    public async Task<IActionResult> AttachAuditedAfs([FromBody] AttachAuditedAfsDto request)
    {
        var exclusion = ValidateHeadAccountantExclusion();
        if (exclusion != null) return exclusion;

        var (userId, role) = GetUserContext();
        try
        {
            var result = await _statementService.AttachAuditedAfsAsync(request, userId, role);
            return Ok(new
            {
                success = true,
                message = "Audited AFS attached successfully as file reference.",
                data = result
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. EXPORT PACKAGES
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("export/excel")]
    public async Task<IActionResult> ExportExcel(
        [FromQuery] int? fiscalYear,
        [FromQuery] string? periodType,
        [FromQuery] int? periodNumber)
    {
        var (userId, _) = GetUserContext();
        int year = fiscalYear ?? DateTime.UtcNow.Year;
        string type = string.IsNullOrWhiteSpace(periodType) ? "Year" : periodType;
        int number = periodNumber ?? 1;

        try
        {
            var bytes = await _statementService.ExportAfsPackageToExcelAsync(year, type, number, userId);
            var filename = $"AFS_Package_{year}_{type}_{number}.xlsx";
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", filename);
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("export/pdf")]
    public async Task<IActionResult> ExportPdf(
        [FromQuery] int? fiscalYear,
        [FromQuery] string? periodType,
        [FromQuery] int? periodNumber)
    {
        var (userId, _) = GetUserContext();
        int year = fiscalYear ?? DateTime.UtcNow.Year;
        string type = string.IsNullOrWhiteSpace(periodType) ? "Year" : periodType;
        int number = periodNumber ?? 1;

        try
        {
            var bytes = await _statementService.ExportAfsPackageToPdfAsync(year, type, number, userId);
            var filename = $"AFS_Package_{year}_{type}_{number}.pdf";
            return File(bytes, "application/pdf", filename);
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }
}
