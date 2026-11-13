using System.Collections.Generic;
using System.Threading.Tasks;
using FOMS.Application.DTOs;

namespace FOMS.Application.Interfaces;

public interface IFinancialStatementService
{
    // Go-live Opening Balances
    Task<List<OpeningBalanceInputDto>> GetOpeningBalancesAsync();
    Task<List<OpeningBalanceInputDto>> SetOpeningBalancesAsync(OpeningBalanceBatchDto batch, string userId, string userRole);

    // Statements Generation
    Task<IncomeStatementDto> GenerateIncomeStatementAsync(int fiscalYear, string periodType, int periodNumber);
    Task<BalanceSheetDto> GenerateBalanceSheetAsync(int fiscalYear, string periodType, int periodNumber);
    Task<CashFlowStatementDto> GenerateCashFlowStatementAsync(int fiscalYear, string periodType, int periodNumber);
    Task<EquityStatementDto> GenerateEquityStatementAsync(int fiscalYear, string periodType, int periodNumber);
    Task<FinancialNotesDto> GenerateNotesAsync(int fiscalYear, string periodType, int periodNumber);
    Task<AfsPackageDto> GenerateAfsPackageAsync(int fiscalYear, string periodType, int periodNumber);

    // Approvals
    Task<AfsPackageDto> ReviewAfsPackageAsync(int fiscalYear, string periodType, int periodNumber, string userId, string userRole);
    Task<AfsPackageDto> ApproveAfsPackageAsync(int fiscalYear, string periodType, int periodNumber, string userId, string userRole);
    Task<AfsPackageDto> AttachAuditedAfsAsync(AttachAuditedAfsDto dto, string userId, string userRole);

    // Export Packages
    Task<byte[]> ExportAfsPackageToExcelAsync(int fiscalYear, string periodType, int periodNumber, string userId);
    Task<byte[]> ExportAfsPackageToPdfAsync(int fiscalYear, string periodType, int periodNumber, string userId);
}
