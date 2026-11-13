using System.Collections.Generic;
using System.Threading.Tasks;
using FOMS.Application.DTOs;
using FOMS.Domain.Entities.SqlServer;

namespace FOMS.Application.Interfaces;

public interface IBudgetService
{
    Task<List<DepartmentDto>> GetSharedDepartmentsAsync();
    Task<AnnualBudgetDto?> GetAnnualBudgetAsync(int fiscalYear);
    Task<AnnualBudgetDto> CreateOrUpdateDraftBudgetAsync(CreateOrUpdateDraftBudgetRequest request, string userId, string userRole);
    Task<AnnualBudgetDto> SubmitBudgetAsync(int annualBudgetId, string userId, string userRole);
    Task<AnnualBudgetDto> ApproveAnnualBudgetAsync(int annualBudgetId, string userId, string userRole);
    Task<AnnualBudgetDto> RejectAnnualBudgetAsync(int annualBudgetId, string reason, string userId, string userRole);
    Task<AnnualBudgetDto> AllocateBudgetAsync(AllocateBudgetRequest request, string userId, string userRole);
    
    Task<BudgetAvailabilityResponse> CheckAvailabilityAsync(CheckBudgetAvailabilityRequest request);
    Task<BudgetOverride> RequestBudgetOverrideAsync(RequestBudgetOverrideRequest request, string userId, string userRole);
    Task<BudgetOverride> ApproveBudgetOverrideAsync(int overrideId, string userId, string userRole);
    Task<BudgetOverride> RejectBudgetOverrideAsync(int overrideId, string reason, string userId, string userRole);

    Task<BudgetTransfer> RequestBudgetTransferAsync(RequestBudgetTransferRequest request, string userId, string userRole);
    Task<BudgetTransfer> ApproveBudgetTransferAsync(int transferId, string userId, string userRole);
    Task<BudgetTransfer> RejectBudgetTransferAsync(int transferId, string reason, string userId, string userRole);

    Task<WeeklyBudgetReportDto> GetWeeklyBudgetReportAsync(int fiscalYear, int month);
}
