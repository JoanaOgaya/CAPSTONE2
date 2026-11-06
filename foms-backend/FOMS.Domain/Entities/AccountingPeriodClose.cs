using System;

namespace FOMS.Domain.Entities;

public class AccountingPeriodClose
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string PeriodType { get; set; } = string.Empty;
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public string ClosedBy { get; set; } = string.Empty;
    public DateTime ClosedAt { get; set; } = DateTime.UtcNow;
    public int ValidatedPaymentCount { get; set; }
    public int ValidatedLiquidationCount { get; set; }
    public decimal ReceivableLedgerTotal { get; set; }
    public decimal AgingReportTotal { get; set; }
    public decimal BankBookBalance { get; set; }
    public decimal BankStatementBalance { get; set; }
    public string BankStatementProofReference { get; set; } = string.Empty;
    public decimal CashBookBalance { get; set; }
    public decimal CountedCashBalance { get; set; }
    public string ListedExceptionsJson { get; set; } = "[]";
}

public class AccountingPeriodCorrectionEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string PeriodCloseId { get; set; } = string.Empty;
    public DateTime EntryDate { get; set; } = DateTime.UtcNow;
    public string Description { get; set; } = string.Empty;
    public string DebitAccount { get; set; } = string.Empty;
    public string CreditAccount { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
