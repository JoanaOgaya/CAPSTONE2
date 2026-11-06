using System.Collections.Generic;
using System.Threading.Tasks;
using FOMS.Application.DTOs;
using FOMS.Domain.Entities.SqlServer;

namespace FOMS.Application.Interfaces;

public interface IDisbursementAndReimbursementService
{
    // Cash Advance Lifecycle
    Task<CashAdvanceResponseDto> RequestCashAdvanceAsync(RequestCashAdvanceDto dto, string userId, string userRole);
    Task<CashAdvanceResponseDto> ApproveCashAdvanceAsync(string advanceId, string userId, string userRole);
    Task<CashAdvanceResponseDto> ReleaseCashAdvanceAsync(string advanceId, string userId, string userRole);
    Task<CashAdvanceResponseDto> RejectCashAdvanceAsync(string advanceId, string reason, string userId, string userRole);
    Task<CashAdvanceResponseDto> CancelCashAdvanceAsync(string advanceId, string reason, string userId, string userRole);

    // Direct Disbursement (CSH-12)
    Task<DirectDisbursementResponseDto> RequestDirectDisbursementAsync(RequestDirectDisbursementDto dto, string userId, string userRole);
    Task<DirectDisbursementResponseDto> ApproveDirectDisbursementAsync(int disbursementId, string userId, string userRole);
    Task<DirectDisbursementResponseDto> ReleaseDirectDisbursementAsync(int disbursementId, string userId, string userRole);
    Task<DirectDisbursementResponseDto> RejectDirectDisbursementAsync(int disbursementId, string reason, string userId, string userRole);
    Task<DirectDisbursementResponseDto> CancelDirectDisbursementAsync(int disbursementId, string reason, string userId, string userRole);

    // Reimbursement Lifecycle
    Task<ReimbursementResponseDto> SubmitReimbursementAsync(SubmitReimbursementDto dto, string userId, string userRole);
    Task<ReimbursementResponseDto> ValidateReimbursementAsync(int reimbursementId, string userId, string userRole);
    Task<ReimbursementResponseDto> ApproveReimbursementAsync(int reimbursementId, string userId, string userRole);
    Task<ReimbursementResponseDto> PayReimbursementAsync(int reimbursementId, string userId, string userRole);
    Task<ReimbursementResponseDto> RejectReimbursementAsync(int reimbursementId, string reason, string userId, string userRole);

    // Liquidation Lifecycle
    Task<LiquidationResponseDto> SubmitLiquidationAsync(SubmitLiquidationDto dto, string userId, string userRole);
    Task<LiquidationResponseDto> ValidateLiquidationAsync(ValidateLiquidationDto dto, string userId, string userRole);
    Task<LiquidationResponseDto> RejectLiquidationAsync(RejectLiquidationDto dto, string userId, string userRole);
    Task<LiquidationResponseDto> MarkForCorrectionLiquidationAsync(int liquidationId, string remarks, string userId, string userRole);
}
