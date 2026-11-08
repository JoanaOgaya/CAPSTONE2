using System.Threading;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using FOMS.Application.Interfaces;
using FOMS.Domain.Entities;

namespace FOMS.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ShipmentRate> ShipmentRates => Set<ShipmentRate>();
    public DbSet<PaymentAdjustment> PaymentAdjustments => Set<PaymentAdjustment>();
        public DbSet<BankBalance> BankBalances => Set<BankBalance>();
    public DbSet<TransportationExpense> TransportationExpenses => Set<TransportationExpense>();
    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();
    public DbSet<PaymentValidation> PaymentValidations => Set<PaymentValidation>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<UserActivityLog> UserActivityLogs => Set<UserActivityLog>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<ShipmentRecord> ShipmentRecords => Set<ShipmentRecord>();
    public DbSet<ShipmentPricing> ShipmentPricings => Set<ShipmentPricing>();
        public DbSet<StatementOfAccount> StatementOfAccounts => Set<StatementOfAccount>();
    public DbSet<PaymentCollection> PaymentCollections => Set<PaymentCollection>();
    public DbSet<OfficialReceipt> OfficialReceipts => Set<OfficialReceipt>();
    public DbSet<ReceivableBalance> ReceivableBalances => Set<ReceivableBalance>();
    public DbSet<AgingAccount> AgingAccounts => Set<AgingAccount>();
    public DbSet<CashFlowTransaction> CashFlowTransactions => Set<CashFlowTransaction>();
    public DbSet<ChartOfAccount> ChartOfAccounts => Set<ChartOfAccount>();
    public DbSet<GeneralLedgerEntry> GeneralLedgerEntries => Set<GeneralLedgerEntry>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<TrialBalance> TrialBalances => Set<TrialBalance>();
    public DbSet<PayrollRecord> PayrollRecords => Set<PayrollRecord>();
    public DbSet<Payslip> Payslips => Set<Payslip>();
    public DbSet<BankAdviceRecord> BankAdviceRecords => Set<BankAdviceRecord>();
    public DbSet<PaymentConcernTicket> PaymentConcernTickets => Set<PaymentConcernTicket>();
    public DbSet<DeliveryPaymentValidation> DeliveryPaymentValidations => Set<DeliveryPaymentValidation>();
    public DbSet<OfficialReceiptSequence> OfficialReceiptSequences => Set<OfficialReceiptSequence>();
    public DbSet<PayrollDeductionLine> PayrollDeductionLines => Set<PayrollDeductionLine>();
    public DbSet<PaymentHistory> PaymentHistories => Set<PaymentHistory>();
    public DbSet<StarsInvoiceEndorsementLink> StarsInvoiceEndorsementLinks => Set<StarsInvoiceEndorsementLink>();
    public DbSet<AccountingPeriodClose> AccountingPeriodCloses => Set<AccountingPeriodClose>();
    public DbSet<AccountingPeriodCorrectionEntry> AccountingPeriodCorrectionEntries => Set<AccountingPeriodCorrectionEntry>();
    public DbSet<FOMS.Domain.Entities.SqlServer.Department> Departments => Set<FOMS.Domain.Entities.SqlServer.Department>();
    public DbSet<FOMS.Domain.Entities.SqlServer.AnnualBudget> AnnualBudgets => Set<FOMS.Domain.Entities.SqlServer.AnnualBudget>();
    public DbSet<FOMS.Domain.Entities.SqlServer.MonthlyBudgetLine> MonthlyBudgetLines => Set<FOMS.Domain.Entities.SqlServer.MonthlyBudgetLine>();
    public DbSet<FOMS.Domain.Entities.SqlServer.BudgetOverride> BudgetOverrides => Set<FOMS.Domain.Entities.SqlServer.BudgetOverride>();
    public DbSet<FOMS.Domain.Entities.SqlServer.BudgetTransfer> BudgetTransfers => Set<FOMS.Domain.Entities.SqlServer.BudgetTransfer>();
    public DbSet<FOMS.Domain.Entities.SqlServer.ApprovalThresholdSetting> ApprovalThresholdSettings => Set<FOMS.Domain.Entities.SqlServer.ApprovalThresholdSetting>();
    public DbSet<FOMS.Domain.Entities.SqlServer.DirectDisbursement> DirectDisbursements => Set<FOMS.Domain.Entities.SqlServer.DirectDisbursement>();
    public DbSet<FOMS.Domain.Entities.SqlServer.Reimbursement> Reimbursements => Set<FOMS.Domain.Entities.SqlServer.Reimbursement>();
    public DbSet<FOMS.Domain.Entities.SqlServer.GoLiveOpeningBalance> GoLiveOpeningBalances => Set<FOMS.Domain.Entities.SqlServer.GoLiveOpeningBalance>();
    public DbSet<FOMS.Domain.Entities.SqlServer.FinancialStatementApproval> FinancialStatementApprovals => Set<FOMS.Domain.Entities.SqlServer.FinancialStatementApproval>();
    public DbSet<FOMS.Domain.Entities.SqlServer.CashAdvance> CashAdvances => Set<FOMS.Domain.Entities.SqlServer.CashAdvance>();
    public DbSet<FOMS.Domain.Entities.SqlServer.Liquidation> Liquidations => Set<FOMS.Domain.Entities.SqlServer.Liquidation>();
    public DbSet<FOMS.Domain.Entities.SqlServer.LiquidationLineItem> LiquidationLineItems => Set<FOMS.Domain.Entities.SqlServer.LiquidationLineItem>();
    public DbSet<FOMS.Domain.Entities.SqlServer.CashInflow> CashInflows => Set<FOMS.Domain.Entities.SqlServer.CashInflow>();
    public DbSet<FOMS.Domain.Entities.SqlServer.CashOutflow> CashOutflows => Set<FOMS.Domain.Entities.SqlServer.CashOutflow>();
    public DbSet<FOMS.Domain.Entities.SqlServer.Waybill> Waybills => Set<FOMS.Domain.Entities.SqlServer.Waybill>();
    public DbSet<FOMS.Domain.Entities.SqlServer.WaybillDuplicateAlert> WaybillDuplicateAlerts => Set<FOMS.Domain.Entities.SqlServer.WaybillDuplicateAlert>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Client>().Ignore(c => c.IsRealData);

        modelBuilder.Entity<AuditLog>().ToTable("AppAuditLogs");

        // Keep legacy provider records mapped so schema updates preserve their history; no current feature exposes these sets.
        modelBuilder.Entity<SpeedPayTransaction>().ToTable("SpeedPayTransactions");
        modelBuilder.Entity<SpeedPayManualSubmission>().ToTable("SpeedPayManualSubmissions");
        modelBuilder.Entity<PaymentTransaction>().ToTable("PaymentTransactions");
        modelBuilder.Entity<PaymentReceipt>().ToTable("PaymentReceipts");
        modelBuilder.Entity<ProcessedWebhookEvent>().ToTable("ProcessedWebhookEvents");

        modelBuilder.Entity<PaymentTransaction>(entity =>
        {
            entity.HasOne(t => t.Client)
                .WithMany()
                .HasForeignKey(t => t.ClientId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(t => t.ShipmentRecord)
                .WithMany()
                .HasForeignKey(t => t.ShipmentRecordId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PaymentReceipt>(entity =>
        {
            entity.HasOne(r => r.PaymentTransaction)
                .WithOne()
                .HasForeignKey<PaymentReceipt>(r => r.PaymentTransactionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Employee>(entity =>
        {
            entity.Property(e => e.Id).HasMaxLength(450).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Role).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Username).HasMaxLength(100).IsRequired();
            entity.Property(e => e.SystemAccess).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(50).IsRequired();
            entity.Property(e => e.PasswordHash).HasMaxLength(500).IsRequired();
            entity.Property(e => e.Phone).HasMaxLength(20);
            entity.Property(e => e.SalaryRate).HasColumnType("decimal(18,2)");

            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.Username).IsUnique();

            entity.HasOne(e => e.RoleNavigation)
                .WithMany(r => r.Employees)
                .HasForeignKey(e => e.RoleId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.ToTable(tb =>
            {
                tb.HasCheckConstraint("CK_Employees_Id_Pattern", "[Id] LIKE 'EMP-[0-9][0-9][0-9]'");
                tb.HasCheckConstraint("CK_Employees_Email_Format", "[Email] LIKE '%_@__%.__%'");
                tb.HasCheckConstraint("CK_Employees_Status", "[Status] IN ('Active', 'Pending', 'Locked', 'Inactive')");
                tb.HasCheckConstraint("CK_Employees_SalaryRate", "[SalaryRate] IS NULL OR [SalaryRate] >= 0");
            });
        });

        modelBuilder.Entity<RefreshToken>()
            .HasOne<Employee>()
            .WithMany(e => e.RefreshTokens)
            .HasForeignKey(rt => rt.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<GeneralLedgerEntry>()
            .HasOne(e => e.JournalEntry)
            .WithMany(j => j.Entries)
            .HasForeignKey(e => e.JournalEntryId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<GeneralLedgerEntry>()
            .HasOne(e => e.Account)
            .WithMany()
            .HasForeignKey(e => e.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Invoice>()
            .HasOne(b => b.Client)
            .WithMany(c => c.Invoices)
            .HasForeignKey(b => b.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ReceivableBalance>()
            .HasOne(r => r.Invoice)
            .WithMany()
            .HasForeignKey(r => r.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ReceivableBalance>()
            .HasOne(r => r.Client)
            .WithMany(c => c.Receivables)
            .HasForeignKey(r => r.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        foreach (var property in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var prop in property.GetProperties())
            {
                if (prop.ClrType == typeof(decimal) || prop.ClrType == typeof(decimal?))
                {
                    prop.SetPrecision(18);
                    prop.SetScale(2);
                }
            }
        }

        modelBuilder.Entity<Invoice>()
            .Property(i => i.VatRate)
            .HasPrecision(5, 4);

        modelBuilder.Entity<Invoice>()
            .Property(i => i.DiscountRate)
            .HasPrecision(5, 4);

        modelBuilder.Entity<Invoice>()
            .Property(i => i.FuelSurchargeRate)
            .HasPrecision(5, 4);

        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.Property(i => i.DocumentStatus)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(i => i.PaymentStatus)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(i => i.RowVersion)
                .IsRowVersion();

            entity.HasIndex(i => i.AccrualJournalEntryId)
                .IsUnique()
                .HasFilter("[AccrualJournalEntryId] IS NOT NULL");
        });

        modelBuilder.Entity<JournalEntry>(entity =>
        {
            entity.Property(j => j.EntryNumber)
                .HasMaxLength(100)
                .IsRequired();
            entity.Property(j => j.InvoiceId)
                .HasMaxLength(450);
            entity.HasIndex(j => j.EntryNumber)
                .IsUnique();
            entity.HasIndex(j => j.InvoiceId)
                .HasDatabaseName("IX_JournalEntries_InvoiceId_UniquePosting")
                .IsUnique()
                .HasFilter("[InvoiceId] IS NOT NULL AND [Posted] = 1");
        });

        modelBuilder.Entity<AccountingPeriodClose>()
            .HasIndex(p => new { p.PeriodType, p.PeriodStart, p.PeriodEnd })
            .IsUnique();

        modelBuilder.Entity<AccountingPeriodCorrectionEntry>()
            .HasOne<AccountingPeriodClose>()
            .WithMany()
            .HasForeignKey(e => e.PeriodCloseId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ProcessedWebhookEvent>()
            .HasIndex(e => e.EventId)
            .IsUnique();

        // Phase 3: Unique constraints for strict data integrity
        modelBuilder.Entity<Payment>()
            .HasIndex(p => p.OrNumber)
            .IsUnique();

        modelBuilder.Entity<Invoice>()
            .HasIndex(i => i.InvoiceNo)
            .IsUnique();

        // Phase 3: Cascade for deductions
        modelBuilder.Entity<PayrollDeductionLine>()
            .HasOne(d => d.PayrollRecord)
            .WithMany(p => p.DeductionLines)
            .HasForeignKey(d => d.PayrollRecordId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await EnforceClosedPeriodLocksAsync(cancellationToken);
        return await base.SaveChangesAsync(cancellationToken);
    }

    private async Task EnforceClosedPeriodLocksAsync(CancellationToken cancellationToken)
    {
        var periods = await AccountingPeriodCloses
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        if (periods.Count == 0)
            return;

        var collectionInvoiceChanges = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(Invoice.AmountPaid),
            nameof(Invoice.Balance),
            nameof(Invoice.PaymentStatus),
            nameof(Invoice.PaymentValidationStatus),
            nameof(Invoice.LastUpdated),
            nameof(Invoice.UpdatedBy),
            nameof(Invoice.DaysOverdue),
            nameof(Invoice.AgingBucket),
            nameof(Invoice.Archived)
        };

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            DateTime? transactionDate = entry.Entity switch
            {
                Invoice invoice when entry.State != EntityState.Modified ||
                    entry.Properties.Any(property => property.IsModified && !collectionInvoiceChanges.Contains(property.Metadata.Name)) ||
                    ((entry.Property(nameof(Invoice.PaymentStatus)).IsModified ||
                      entry.Property(nameof(Invoice.PaymentValidationStatus)).IsModified) &&
                     !(entry.Property(nameof(Invoice.AmountPaid)).IsModified &&
                       entry.Property(nameof(Invoice.Balance)).IsModified))
                    => DateTime.TryParse(invoice.BillingDate, out var billingDate) ? billingDate.Date : null,
                Payment payment => DateTime.TryParse(payment.PaymentDate, out var paymentDate) ? paymentDate.Date : payment.SubmittedAt.Date,
                CashFlowTransaction cashFlow => cashFlow.TransactionDate.Date,
                JournalEntry journalEntry => journalEntry.Date.Date,
                PaymentCollection collection => collection.CollectedDate.Date,
                OfficialReceipt receipt => receipt.IssuedDate.Date,
                PaymentValidation validation => DateTime.TryParse(validation.DateSubmitted, out var submittedDate) ? submittedDate.Date : null,
                BankBalance balance => DateTime.TryParse(balance.LastReconciled, out var reconciledDate) ? reconciledDate.Date : null,
                _ => null
            };

            if (transactionDate == null)
                continue;

            if (periods.Any(period => transactionDate.Value >= period.PeriodStart.Date &&
                                      transactionDate.Value <= period.PeriodEnd.Date))
                throw new InvalidOperationException(
                    $"Accounting period containing {transactionDate:yyyy-MM-dd} is closed. Record a correcting entry dated after the period instead.");
        }
    }
}
