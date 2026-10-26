using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FOMS.Domain.Entities.SqlServer;

namespace FOMS.Application.Interfaces;

public interface IFomsDbContext
{
    DbSet<Role> Roles { get; }
    DbSet<User> Users { get; }
    DbSet<Client> Clients { get; }
    DbSet<ClientFinanceSetting> ClientFinanceSettings { get; }
    DbSet<ClientRate> ClientRates { get; }
    DbSet<Invoice> Invoices { get; }
    DbSet<AccountsReceivable> AccountsReceivable { get; }
    DbSet<Payment> Payments { get; }
    DbSet<PaymentHistory> PaymentHistory { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<Waybill> Waybills { get; }
    DbSet<ProofOfDelivery> ProofOfDeliveries { get; }
    DbSet<BillingDocument> BillingDocuments { get; }
    DbSet<InvoiceLineItem> InvoiceLineItems { get; }
    DbSet<InvoiceComputation> InvoiceComputations { get; }
    DbSet<FomsOfficialReceipt> OfficialReceipts { get; }
    DbSet<CashInflow> CashInflows { get; }
    DbSet<CashOutflow> CashOutflows { get; }
    DbSet<CashAdvance> CashAdvances { get; }
    DbSet<Liquidation> Liquidations { get; }
    DbSet<LiquidationLineItem> LiquidationLineItems { get; }
    DbSet<LiquidationDocument> LiquidationDocuments { get; }
    DbSet<FinancialAdjustment> FinancialAdjustments { get; }
    DbSet<CreditMemo> CreditMemos { get; }
    DbSet<RefundRequest> RefundRequests { get; }
    DbSet<Settlement> Settlements { get; }
    DbSet<AIReviewHistory> AIReviewHistories { get; }
    DbSet<AIOutputHistory> AIOutputHistories { get; }
    DbSet<AIScanLog> AIScanLogs { get; }
    DbSet<StarsCollectionTask> StarsCollectionTasks { get; }
    DbSet<Report> Reports { get; }
    DbSet<ApprovalThresholdSetting> ApprovalThresholdSettings { get; }
    DbSet<CashOutflowApprovalRequest> CashOutflowApprovalRequests { get; }
    DbSet<CashOutflowApprovalHistory> CashOutflowApprovalHistories { get; }
    DbSet<Department> Departments { get; }
    DbSet<UserClientMapping> UserClientMappings { get; }
    DbSet<RevokedToken> RevokedTokens { get; }
    DbSet<ExecutiveApprovalRecord> ExecutiveApprovalRecords { get; }
    DbSet<FinanceManagerDelegate> FinanceManagerDelegates { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
