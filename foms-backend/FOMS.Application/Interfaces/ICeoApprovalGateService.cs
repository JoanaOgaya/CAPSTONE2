using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FOMS.Application.DTOs;

namespace FOMS.Application.Interfaces;

public interface ICeoApprovalGateService
{
    Task<CeoApprovalGateCheckResult> CheckGateAsync(
        string itemType, 
        string itemId, 
        decimal? amount = null, 
        DateTime? requestDate = null, 
        CancellationToken ct = default);

    Task EnsureGateSatisfiedAsync(
        string itemType, 
        string itemId, 
        decimal? amount = null, 
        DateTime? requestDate = null, 
        CancellationToken ct = default);

    Task<CeoApprovalRecordDto> RecordApprovalAsync(
        CreateCeoApprovalRecordRequest request, 
        string enteredByEmployeeId, 
        string userRole, 
        CancellationToken ct = default);

    Task<CeoApprovalRecordDto> RecordCorrectionAsync(
        CreateCeoApprovalCorrectionRequest request, 
        string enteredByEmployeeId, 
        string userRole, 
        CancellationToken ct = default);

    Task<CeoApprovalConfirmationDto> ConfirmRecordAsync(
        int recordId, 
        string kind, 
        string confirmedByEmployeeId, 
        string userRole, 
        string? note = null,
        CancellationToken ct = default);

    Task<CeoApprovalThresholdDto> GetCurrentThresholdAsync(
        DateTime? effectiveDate = null, 
        CancellationToken ct = default);

    Task<CeoApprovalThresholdDto> UpdateThresholdAsync(
        UpdateCeoThresholdRequest request, 
        string updatedByEmployeeId, 
        string userRole, 
        CancellationToken ct = default);

    Task<List<CeoApprovalRecordDto>> GetRecordsAsync(
        string? itemType = null, 
        string? status = null, 
        int? fiscalYear = null, 
        CancellationToken ct = default);

    Task<CeoApprovalRecordDto?> GetRecordByIdAsync(
        int recordId, 
        CancellationToken ct = default);

    Task<(byte[] Bytes, string ContentType, string FileName)> DownloadAttachmentAsync(
        int recordId, 
        string employeeId, 
        string userRole, 
        CancellationToken ct = default);

    Task<List<CeoApprovalThresholdChangeLogDto>> GetThresholdHistoryAsync(
        CancellationToken ct = default);

    Task<byte[]> ExportRecordsToExcelAsync(
        string? itemType = null, 
        string? status = null, 
        CancellationToken ct = default);
}
