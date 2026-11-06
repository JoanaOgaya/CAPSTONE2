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
[Route("api/budget")]
[Route("budget")]
public class BudgetController : ControllerBase
{
    private readonly IBudgetService _budgetService;

    public BudgetController(IBudgetService budgetService)
    {
        _budgetService = budgetService;
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

    private IActionResult? ValidateBudgetChainExclusion()
    {
        var (_, role) = GetUserContext();
        if (string.Equals(role, "Head Accountant", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(403, new
            {
                success = false,
                message = "Head Accountant is not in the budget chain."
            });
        }
        return null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. Shared Departments with DMS
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("departments")]
    public async Task<IActionResult> GetSharedDepartments()
    {
        var exclusion = ValidateBudgetChainExclusion();
        if (exclusion != null) return exclusion;

        var departments = await _budgetService.GetSharedDepartmentsAsync();
        return Ok(new { success = true, data = departments });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. Annual Budget View (Rolling Annual Total)
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("annual/{fiscalYear}")]
    public async Task<IActionResult> GetAnnualBudget(int fiscalYear)
    {
        var exclusion = ValidateBudgetChainExclusion();
        if (exclusion != null) return exclusion;

        var budget = await _budgetService.GetAnnualBudgetAsync(fiscalYear);
        if (budget == null)
            return NotFound(new { success = false, message = $"Annual budget for FY {fiscalYear} not found." });

        return Ok(new { success = true, data = budget });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. Draft Budget Creation / Modification (Dept Estimate per Category per Month)
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("draft")]
    public async Task<IActionResult> CreateOrUpdateDraftBudget([FromBody] CreateOrUpdateDraftBudgetRequest request)
    {
        var exclusion = ValidateBudgetChainExclusion();
        if (exclusion != null) return exclusion;

        var (userId, role) = GetUserContext();

        try
        {
            var budget = await _budgetService.CreateOrUpdateDraftBudgetAsync(request, userId, role);
            return Ok(new { success = true, message = "Draft budget saved successfully.", data = budget });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. Submit Budget (Finance Manager Reviews & Submits)
    // ─────────────────────────────────────────────────────────────────────────
    public class BudgetActionRequest
    {
        public int AnnualBudgetId { get; set; }
    }

    [HttpPost("submit")]
    public async Task<IActionResult> SubmitBudget([FromBody] BudgetActionRequest request)
    {
        var exclusion = ValidateBudgetChainExclusion();
        if (exclusion != null) return exclusion;

        var (userId, role) = GetUserContext();

        try
        {
            var budget = await _budgetService.SubmitBudgetAsync(request.AnnualBudgetId, userId, role);
            return Ok(new { success = true, message = "Annual budget submitted for CEO/President approval.", data = budget });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { success = false, message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5. Approve Annual Budget (CEO / President Approves)
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("approve")]
    public async Task<IActionResult> ApproveAnnualBudget([FromBody] BudgetActionRequest request)
    {
        var exclusion = ValidateBudgetChainExclusion();
        if (exclusion != null) return exclusion;

        var (userId, role) = GetUserContext();

        try
        {
            var budget = await _budgetService.ApproveAnnualBudgetAsync(request.AnnualBudgetId, userId, role);
            return Ok(new { success = true, message = "Annual budget approved by CEO/President.", data = budget });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { success = false, message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 6. Reject Annual Budget (CEO / President Rejects with Reason)
    // ─────────────────────────────────────────────────────────────────────────
    public class RejectAnnualBudgetRequest
    {
        public int AnnualBudgetId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    [HttpPost("reject")]
    public async Task<IActionResult> RejectAnnualBudget([FromBody] RejectAnnualBudgetRequest request)
    {
        var exclusion = ValidateBudgetChainExclusion();
        if (exclusion != null) return exclusion;

        var (userId, role) = GetUserContext();

        try
        {
            var budget = await _budgetService.RejectAnnualBudgetAsync(request.AnnualBudgetId, request.Reason, userId, role);
            return Ok(new { success = true, message = "Annual budget rejected and returned to Draft.", data = budget });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { success = false, message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 7. Allocate Budget by Department & Category (Finance Manager)
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("allocate")]
    public async Task<IActionResult> AllocateBudget([FromBody] AllocateBudgetRequest request)
    {
        var exclusion = ValidateBudgetChainExclusion();
        if (exclusion != null) return exclusion;

        var (userId, role) = GetUserContext();

        try
        {
            var budget = await _budgetService.AllocateBudgetAsync(request, userId, role);
            return Ok(new { success = true, message = "Budget allocated successfully.", data = budget });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { success = false, message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 8. Check Availability & Soft Block Controls
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("check-availability")]
    public async Task<IActionResult> CheckAvailability([FromBody] CheckBudgetAvailabilityRequest request)
    {
        var exclusion = ValidateBudgetChainExclusion();
        if (exclusion != null) return exclusion;

        var result = await _budgetService.CheckAvailabilityAsync(request);
        return Ok(new { success = true, data = result });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 9. Request Budget Override (Soft Block Resolution)
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("override/request")]
    public async Task<IActionResult> RequestBudgetOverride([FromBody] RequestBudgetOverrideRequest request)
    {
        var exclusion = ValidateBudgetChainExclusion();
        if (exclusion != null) return exclusion;

        var (userId, role) = GetUserContext();

        try
        {
            var budgetOverride = await _budgetService.RequestBudgetOverrideAsync(request, userId, role);
            return Ok(new { success = true, message = "Budget override requested.", data = budgetOverride });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { success = false, message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 10. Approve Budget Override (FM <= Threshold, CEO > Threshold)
    // ─────────────────────────────────────────────────────────────────────────
    public class OverrideActionRequest
    {
        public int OverrideId { get; set; }
    }

    [HttpPost("override/approve")]
    public async Task<IActionResult> ApproveBudgetOverride([FromBody] OverrideActionRequest request)
    {
        var exclusion = ValidateBudgetChainExclusion();
        if (exclusion != null) return exclusion;

        var (userId, role) = GetUserContext();

        try
        {
            var budgetOverride = await _budgetService.ApproveBudgetOverrideAsync(request.OverrideId, userId, role);
            return Ok(new { success = true, message = "Budget override approved.", data = budgetOverride });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { success = false, message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 11. Reject Budget Override
    // ─────────────────────────────────────────────────────────────────────────
    public class RejectOverrideActionRequest
    {
        public int OverrideId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    [HttpPost("override/reject")]
    public async Task<IActionResult> RejectBudgetOverride([FromBody] RejectOverrideActionRequest request)
    {
        var exclusion = ValidateBudgetChainExclusion();
        if (exclusion != null) return exclusion;

        var (userId, role) = GetUserContext();

        try
        {
            var budgetOverride = await _budgetService.RejectBudgetOverrideAsync(request.OverrideId, request.Reason, userId, role);
            return Ok(new { success = true, message = "Budget override rejected.", data = budgetOverride });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { success = false, message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 12. Request Budget Transfer / Carry-Over (Finance Manager Requests)
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("transfer/request")]
    public async Task<IActionResult> RequestBudgetTransfer([FromBody] RequestBudgetTransferRequest request)
    {
        var exclusion = ValidateBudgetChainExclusion();
        if (exclusion != null) return exclusion;

        var (userId, role) = GetUserContext();

        try
        {
            var transfer = await _budgetService.RequestBudgetTransferAsync(request, userId, role);
            return Ok(new { success = true, message = "Budget transfer requested.", data = transfer });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { success = false, message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 13. Approve Budget Transfer / Carry-Over (CEO / President Approves)
    // ─────────────────────────────────────────────────────────────────────────
    public class TransferActionRequest
    {
        public int TransferId { get; set; }
    }

    [HttpPost("transfer/approve")]
    public async Task<IActionResult> ApproveBudgetTransfer([FromBody] TransferActionRequest request)
    {
        var exclusion = ValidateBudgetChainExclusion();
        if (exclusion != null) return exclusion;

        var (userId, role) = GetUserContext();

        try
        {
            var transfer = await _budgetService.ApproveBudgetTransferAsync(request.TransferId, userId, role);
            return Ok(new { success = true, message = "Budget transfer approved. Company total budget remains unchanged.", data = transfer });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { success = false, message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 14. Reject Budget Transfer
    // ─────────────────────────────────────────────────────────────────────────
    public class RejectTransferActionRequest
    {
        public int TransferId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    [HttpPost("transfer/reject")]
    public async Task<IActionResult> RejectBudgetTransfer([FromBody] RejectTransferActionRequest request)
    {
        var exclusion = ValidateBudgetChainExclusion();
        if (exclusion != null) return exclusion;

        var (userId, role) = GetUserContext();

        try
        {
            var transfer = await _budgetService.RejectBudgetTransferAsync(request.TransferId, request.Reason, userId, role);
            return Ok(new { success = true, message = "Budget transfer rejected.", data = transfer });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { success = false, message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 15. Weekly Report (Actual vs Monthly Budget)
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("reports/weekly")]
    public async Task<IActionResult> GetWeeklyBudgetReport([FromQuery] int fiscalYear, [FromQuery] int month)
    {
        var exclusion = ValidateBudgetChainExclusion();
        if (exclusion != null) return exclusion;

        if (fiscalYear <= 0) fiscalYear = DateTime.UtcNow.Year;
        if (month <= 0 || month > 12) month = DateTime.UtcNow.Month;

        var report = await _budgetService.GetWeeklyBudgetReportAsync(fiscalYear, month);
        return Ok(new { success = true, data = report });
    }

    [HttpGet("reports/weekly/export")]
    [HttpGet("reports/weekly/excel")]
    public async Task<IActionResult> ExportWeeklyBudgetReportExcel([FromQuery] int fiscalYear, [FromQuery] int month)
    {
        var exclusion = ValidateBudgetChainExclusion();
        if (exclusion != null) return exclusion;

        var (userId, _) = GetUserContext();
        if (fiscalYear <= 0) fiscalYear = DateTime.UtcNow.Year;
        if (month <= 0 || month > 12) month = DateTime.UtcNow.Month;

        var report = await _budgetService.GetWeeklyBudgetReportAsync(fiscalYear, month);

        using var workbook = new ClosedXML.Excel.XLWorkbook();
        var ws = workbook.Worksheets.Add($"Weekly_Budget_{fiscalYear}_{month:D2}");

        ws.Cell(1, 1).Value = $"Weekly Budget Report - Fiscal Year {fiscalYear}, Month {month}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;

        ws.Cell(3, 1).Value = "Department Code";
        ws.Cell(3, 2).Value = "Department Name";
        ws.Cell(3, 3).Value = "Monthly Budget (₱)";
        ws.Cell(3, 4).Value = "Actual Spend (₱)";
        ws.Cell(3, 5).Value = "Remaining Budget (₱)";
        ws.Cell(3, 6).Value = "Utilization (%)";
        ws.Row(3).Style.Font.Bold = true;

        int row = 4;
        foreach (var dept in report.Departments)
        {
            ws.Cell(row, 1).Value = dept.DepartmentCode;
            ws.Cell(row, 2).Value = dept.DepartmentName;
            ws.Cell(row, 3).Value = dept.MonthlyBudget;
            ws.Cell(row, 4).Value = dept.ActualSpend;
            ws.Cell(row, 5).Value = dept.Remaining;
            ws.Cell(row, 6).Value = dept.UtilizationPercent;
            row++;
        }

        ws.Cell(row, 1).Value = "TOTAL";
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 3).Value = report.TotalMonthlyBudget;
        ws.Cell(row, 4).Value = report.TotalActualSpend;
        ws.Cell(row, 5).Value = report.TotalRemaining;
        ws.Cell(row, 6).Value = report.OverallUtilizationPercent;
        ws.Row(row).Style.Font.Bold = true;

        ws.Columns().AdjustToContents();

        using var ms = new System.IO.MemoryStream();
        workbook.SaveAs(ms);
        var bytes = ms.ToArray();

        // Audit Log for export
        var audit = new FOMS.Domain.Entities.AuditLog
        {
            LoggedAt = DateTime.UtcNow,
            UserId = userId,
            EntityName = "BudgetReport",
            EntityId = $"{fiscalYear}-{month}",
            Action = "BUDGET_REPORT_EXPORT_EXCEL",
            Details = $"Exported Weekly Budget Report for FY {fiscalYear} Month {month} to Excel (.xlsx)."
        };
        // Save audit
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Weekly_Budget_Report_{fiscalYear}_{month:D2}.xlsx");
    }
}
