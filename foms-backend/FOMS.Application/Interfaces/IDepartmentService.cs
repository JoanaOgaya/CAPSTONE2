using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FOMS.Application.DTOs;

namespace FOMS.Application.Interfaces;

public interface IDepartmentService
{
    /// <summary>
    /// Gets selectable departments: 4 operational departments for general users, 5 (including Overhead) for Finance Manager.
    /// Excludes legacy departments like DEP-OPS from selection.
    /// </summary>
    Task<List<DepartmentDto>> GetSelectableDepartmentsAsync(string userRole, CancellationToken ct = default);

    /// <summary>
    /// Option A suggestion chip: returns the requester's own last department if previously requested.
    /// Never returns Overhead (DEP-OVH) for non-FM users.
    /// </summary>
    Task<DepartmentSuggestionDto> GetDepartmentSuggestionAsync(string userId, string userRole, CancellationToken ct = default);

    /// <summary>
    /// R5.11 item 5: Finance Manager changes department before approval with required reason.
    /// Re-runs budget check, verifies request is not locked, updates committed reservation if applicable, and writes immutable change log.
    /// </summary>
    Task<DepartmentChangeLogDto> ChangeRequestDepartmentAsync(ChangeDepartmentRequest request, string changedByEmployeeId, string userRole, CancellationToken ct = default);

    /// <summary>
    /// Gets audit trail of department changes for a specific request.
    /// </summary>
    Task<List<DepartmentChangeLogDto>> GetDepartmentChangeLogsAsync(string requestType, string requestId, CancellationToken ct = default);

    /// <summary>
    /// Exports department change logs to an Excel (.xlsx) workbook.
    /// </summary>
    Task<byte[]> ExportDepartmentChangeLogsToExcelAsync(string? requestType = null, string? requestId = null, CancellationToken ct = default);

    /// <summary>
    /// Validates and normalizes department for a new or modified request.
    /// Enforces non-empty department, rejects DEP-OPS as legacy, and restricts DEP-OVH to Finance Manager.
    /// </summary>
    Task<string> ValidateAndNormalizeDepartmentAsync(string? departmentCode, string userRole, bool isNewRequest = true, CancellationToken ct = default);
}
