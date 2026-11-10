using System;
using System.Security.Claims;
using System.Threading.Tasks;
using FOMS.Application.DTOs;
using FOMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FOMS.Api.Controllers;

public class CreateCeoApprovalRecordForm
{
    public string ItemType { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime ApprovalDate { get; set; } = DateTime.UtcNow.Date;
    public string? Notes { get; set; }
    public string? RejectionReason { get; set; }
    public Microsoft.AspNetCore.Http.IFormFile? Attachment { get; set; }
}

public class CreateCeoApprovalCorrectionForm
{
    public int OriginalRecordId { get; set; }
    public string ItemType { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime ApprovalDate { get; set; } = DateTime.UtcNow.Date;
    public string? Notes { get; set; }
    public string? RejectionReason { get; set; }
    public string CorrectionReason { get; set; } = string.Empty;
    public Microsoft.AspNetCore.Http.IFormFile? Attachment { get; set; }
}


[ApiController]
[Route("api/ceo-approvals")]
[Authorize]
public class CeoApprovalsController : ControllerBase
{
    private readonly ICeoApprovalGateService _gateService;

    public CeoApprovalsController(ICeoApprovalGateService gateService)
    {
        _gateService = gateService;
    }

    private (string EmployeeId, string Role) GetUserInfo()
    {
        var employeeId = User.FindFirstValue("employee_id") ??
                         User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                         User.Identity?.Name ?? "Unknown";

        var role = User.FindFirstValue(ClaimTypes.Role) ??
                   User.FindFirstValue("role") ?? string.Empty;

        return (employeeId, role);
    }

    [HttpGet]
    public async Task<IActionResult> GetRecords([FromQuery] string? itemType, [FromQuery] string? status, [FromQuery] int? fiscalYear)
    {
        var records = await _gateService.GetRecordsAsync(itemType, status, fiscalYear);
        return Ok(records);
    }

    [HttpGet("{recordId:int}")]
    public async Task<IActionResult> GetRecordById(int recordId)
    {
        var record = await _gateService.GetRecordByIdAsync(recordId);
        if (record == null) return NotFound(new { message = $"CEO approval record {recordId} not found." });
        return Ok(record);
    }

    [HttpGet("export")]
    public async Task<IActionResult> ExportExcel([FromQuery] string? itemType, [FromQuery] string? status)
    {
        var bytes = await _gateService.ExportRecordsToExcelAsync(itemType, status);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"ceo_approval_records_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx");
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> RecordApproval([FromForm] CreateCeoApprovalRecordForm form)
    {
        var (employeeId, role) = GetUserInfo();
        try
        {
            byte[]? fileBytes = null;
            if (form.Attachment != null)
            {
                using var ms = new System.IO.MemoryStream();
                await form.Attachment.CopyToAsync(ms);
                fileBytes = ms.ToArray();
            }

            var request = new CreateCeoApprovalRecordRequest
            {
                ItemType = form.ItemType,
                ItemId = form.ItemId,
                Status = form.Status,
                ApprovalDate = form.ApprovalDate,
                Notes = form.Notes,
                RejectionReason = form.RejectionReason,
                AttachmentBytes = fileBytes,
                AttachmentFileName = form.Attachment?.FileName
            };

            var created = await _gateService.RecordApprovalAsync(request, employeeId, role);
            return CreatedAtAction(nameof(GetRecordById), new { recordId = created.RecordId }, created);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("correct")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> RecordCorrection([FromForm] CreateCeoApprovalCorrectionForm form)
    {
        var (employeeId, role) = GetUserInfo();
        try
        {
            byte[]? fileBytes = null;
            if (form.Attachment != null)
            {
                using var ms = new System.IO.MemoryStream();
                await form.Attachment.CopyToAsync(ms);
                fileBytes = ms.ToArray();
            }

            var request = new CreateCeoApprovalCorrectionRequest
            {
                OriginalRecordId = form.OriginalRecordId,
                ItemType = form.ItemType,
                ItemId = form.ItemId,
                Status = form.Status,
                ApprovalDate = form.ApprovalDate,
                Notes = form.Notes,
                RejectionReason = form.RejectionReason,
                CorrectionReason = form.CorrectionReason,
                AttachmentBytes = fileBytes,
                AttachmentFileName = form.Attachment?.FileName
            };

            var created = await _gateService.RecordCorrectionAsync(request, employeeId, role);
            return CreatedAtAction(nameof(GetRecordById), new { recordId = created.RecordId }, created);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("{recordId:int}/confirm")]
    public async Task<IActionResult> ConfirmRecord(int recordId, [FromBody] ConfirmCeoApprovalRequest request)
    {
        var (employeeId, role) = GetUserInfo();
        try
        {
            var conf = await _gateService.ConfirmRecordAsync(recordId, request.Kind, employeeId, role);
            return Ok(conf);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpGet("{recordId:int}/attachment")]
    public async Task<IActionResult> DownloadAttachment(int recordId)
    {
        var (employeeId, role) = GetUserInfo();
        try
        {
            var (bytes, contentType, fileName) = await _gateService.DownloadAttachmentAsync(recordId, employeeId, role);
            return File(bytes, contentType, fileName);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
        catch (System.IO.FileNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpGet("gate-check")]
    public async Task<IActionResult> GateCheck(
        [FromQuery] string itemType, [FromQuery] string itemId, [FromQuery] decimal? amount, [FromQuery] DateTime? requestDate)
    {
        try
        {
            var result = await _gateService.CheckGateAsync(itemType, itemId, amount, requestDate);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("threshold")]
    public async Task<IActionResult> GetThreshold([FromQuery] DateTime? effectiveDate)
    {
        var threshold = await _gateService.GetCurrentThresholdAsync(effectiveDate);
        return Ok(threshold);
    }

    [HttpPut("threshold")]
    public async Task<IActionResult> UpdateThreshold([FromBody] UpdateCeoThresholdRequest request)
    {
        var (employeeId, role) = GetUserInfo();
        try
        {
            var updated = await _gateService.UpdateThresholdAsync(request, employeeId, role);
            return Ok(updated);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpGet("threshold/history")]
    public async Task<IActionResult> GetThresholdHistory()
    {
        var history = await _gateService.GetThresholdHistoryAsync();
        return Ok(history);
    }
}
