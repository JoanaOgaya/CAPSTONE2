using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace FOMS.Application.DTOs;

public class OpeningBalanceInputDto
{
    [Required]
    public string AccountCode { get; set; } = string.Empty;
    [Required]
    public string AccountName { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string DepartmentCode { get; set; } = "DEP-FIN";
    public string Category { get; set; } = "General";
    public DateTime? AsOfDate { get; set; }
}

public class OpeningBalanceBatchDto
{
    public DateTime AsOfDate { get; set; } = DateTime.UtcNow;
    public List<OpeningBalanceInputDto> Balances { get; set; } = new();
}

public class IncomeStatementLineDto
{
    public string LineItem { get; set; } = string.Empty;
    public decimal AccrualAmount { get; set; }
    public decimal CashAmount { get; set; }
    public decimal PreviousPeriodAmount { get; set; }
    public decimal PreviousYearAmount { get; set; }
    public decimal BudgetAmount { get; set; }
    public decimal BudgetVariance => AccrualAmount - BudgetAmount;
}

public class IncomeStatementDto
{
    public int FiscalYear { get; set; }
    public string PeriodType { get; set; } = "Year"; // Month or Year
    public int PeriodNumber { get; set; } = 1;
    public decimal TotalAccrualRevenue { get; set; }
    public decimal TotalCashRevenue { get; set; }
    public decimal TotalAccrualExpenses { get; set; }
    public decimal TotalCashExpenses { get; set; }
    public decimal NetIncomeAccrual => TotalAccrualRevenue - TotalAccrualExpenses;
    public decimal NetIncomeCash => TotalCashRevenue - TotalCashExpenses;

    public List<IncomeStatementLineDto> Revenues { get; set; } = new();
    public List<IncomeStatementLineDto> Expenses { get; set; } = new();
}

public class BalanceSheetDto
{
    public int FiscalYear { get; set; }
    public DateTime AsOfDate { get; set; }

    // Assets
    public decimal CashAndCashEquivalents { get; set; }
    public decimal AccountsReceivable { get; set; }
    public decimal AdvancesToEmployees { get; set; }
    public decimal TotalAssets => CashAndCashEquivalents + AccountsReceivable + AdvancesToEmployees;

    // Liabilities
    public decimal AccountsPayable { get; set; }
    public decimal TotalLiabilities => AccountsPayable;

    // Equity
    public decimal BeginningEquity { get; set; }
    public decimal CurrentPeriodNetIncome { get; set; }
    public decimal EndingEquity => BeginningEquity + CurrentPeriodNetIncome;

    public decimal TotalLiabilitiesAndEquity => TotalLiabilities + EndingEquity;
    public bool IsBalanced => Math.Abs(TotalAssets - TotalLiabilitiesAndEquity) < 0.01m;
}

public class CashFlowStatementDto
{
    public int FiscalYear { get; set; }
    public string PeriodType { get; set; } = "Year";
    public int PeriodNumber { get; set; } = 1;

    // Direct Method - Operating Activities
    public decimal InflowsFromClientPayments { get; set; }
    public decimal InflowsFromReturnedAdvances { get; set; }
    public decimal TotalOperatingInflows => InflowsFromClientPayments + InflowsFromReturnedAdvances;

    public decimal OutflowsCashAdvancesReleased { get; set; }
    public decimal OutflowsDirectDisbursements { get; set; }
    public decimal OutflowsPaidReimbursements { get; set; }
    public decimal TotalOperatingOutflows => OutflowsCashAdvancesReleased + OutflowsDirectDisbursements + OutflowsPaidReimbursements;

    public decimal NetCashFromOperating => TotalOperatingInflows - TotalOperatingOutflows;

    // Investing & Financing
    public decimal NetCashFromInvesting { get; set; } = 0.00m;
    public decimal NetCashFromFinancing { get; set; } = 0.00m;

    public decimal NetCashChange => NetCashFromOperating + NetCashFromInvesting + NetCashFromFinancing;
    public decimal BeginningCash { get; set; }
    public decimal EndingNetCash => BeginningCash + NetCashChange;
}

public class EquityStatementDto
{
    public int FiscalYear { get; set; }
    public decimal BeginningEquity { get; set; }
    public decimal NetIncomeAccrual { get; set; }
    public decimal NetIncomeCash { get; set; }
    public decimal EndingEquityAccrual => BeginningEquity + NetIncomeAccrual;
    public decimal EndingEquityCash => BeginningEquity + NetIncomeCash;
}

public class ClientRevenueNoteDto
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public int InvoicesCount { get; set; }
    public decimal AccrualAmount { get; set; }
    public decimal CashAmount { get; set; }
    public decimal PercentShare { get; set; }
}

public class CategoryExpenseNoteDto
{
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal PercentShare { get; set; }
}

public class DepartmentExpenseNoteDto
{
    public string DepartmentCode { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal PercentShare { get; set; }
}

public class FinancialNotesDto
{
    public List<ClientRevenueNoteDto> RevenuePerClient { get; set; } = new();
    public List<CategoryExpenseNoteDto> ExpensePerCategory { get; set; } = new();
    public List<DepartmentExpenseNoteDto> ExpensePerDepartment { get; set; } = new();
}

public class AfsPackageDto
{
    public int FiscalYear { get; set; }
    public string PeriodType { get; set; } = "Year";
    public int PeriodNumber { get; set; } = 1;
    public string Status { get; set; } = "Draft"; // Draft, Reviewed, Approved
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? AuditedAfsFileUrl { get; set; }
    public string? AuditedAfsFileName { get; set; }

    public IncomeStatementDto IncomeStatement { get; set; } = new();
    public BalanceSheetDto BalanceSheet { get; set; } = new();
    public CashFlowStatementDto CashFlowStatement { get; set; } = new();
    public EquityStatementDto EquityStatement { get; set; } = new();
    public FinancialNotesDto Notes { get; set; } = new();
}

public class AttachAuditedAfsDto
{
    public int FiscalYear { get; set; }
    public string PeriodType { get; set; } = "Year";
    [Required]
    public string FileUrl { get; set; } = string.Empty;
    public string? FileName { get; set; }
}

public class StatementPeriodRequestDto
{
    public int FiscalYear { get; set; } = DateTime.UtcNow.Year;
    public string PeriodType { get; set; } = "Year";
    public int PeriodNumber { get; set; } = 1;
}
