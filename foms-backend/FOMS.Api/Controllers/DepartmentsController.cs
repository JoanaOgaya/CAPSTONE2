using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FOMS.Application.DTOs;
using FOMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FOMS.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DepartmentsController : ControllerBase
{
    private readonly IDepartmentService _departmentService;

    public DepartmentsController(IDepartmentService departmentService)
    {
        _departmentService = departmentService;
    }

    private string GetCurrentUserId()
    {
        return User.FindFirst("employee_number")?.Value
            ?? User.FindFirst("employeeNumber")?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? "UNKNOWN";
    }

    private string GetCurrentUserRole()
    {
        return User.FindFirst(ClaimTypes.Role)?.Value
            ?? User.FindFirst("role")?.Value
            ?? "Unknown";
    }

    /// <summary>
    /// Gets selectable departments: 4 operational departments for non-FM users, 5 (including Overhead) for Finance Manager.
    /// Excludes legacy departments like DEP-OPS from selection.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<DepartmentDto>>> GetDepartments(CancellationToken ct)
    {
        string role = GetCurrentUserRole();
        var departments = await _departmentService.GetSelectableDepartmentsAsync(role, ct);
        return Ok(departments);
    }

    /// <summary>
    /// Option A suggestion chip (FIN-26): returns the requester's own last department if previously requested.
    /// Never returns Overhead (DEP-OVH) for non-FM users.
    /// </summary>
    [HttpGet("suggestion")]
    public async Task<ActionResult<DepartmentSuggestionDto>> GetDepartmentSuggestion(CancellationToken ct)
    {
        string userId = GetCurrentUserId();
        string role = GetCurrentUserRole();
        var suggestion = await _departmentService.GetDepartmentSuggestionAsync(userId, role, ct);
        return Ok(suggestion);
    }

    /// <summary>
    /// R5.11 item 5: Finance Manager changes department before approval with required reason.
    /// Re-runs budget check, locks after approval, and records immutable change log.
    /// </summary>
    [HttpPost("change")]
    public async Task<ActionResult<DepartmentChangeLogDto>> ChangeDepartment([FromBody] ChangeDepartmentRequest request, CancellationToken ct)
    {
        string userId = GetCurrentUserId();
        string role = GetCurrentUserRole();

        try
        {
            var log = await _departmentService.ChangeRequestDepartmentAsync(request, userId, role, ct);
            return Ok(log);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Gets audit trail of department changes for a specific request.
    /// </summary>
    [HttpGet("change-logs")]
    public async Task<ActionResult<List<DepartmentChangeLogDto>>> GetChangeLogs([FromQuery] string requestType, [FromQuery] string requestId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(requestType) || string.IsNullOrWhiteSpace(requestId))
        {
            return BadRequest(new { message = "RequestType and RequestId query parameters are required." });
        }

        var logs = await _departmentService.GetDepartmentChangeLogsAsync(requestType, requestId, ct);
        return Ok(logs);
    }
}
