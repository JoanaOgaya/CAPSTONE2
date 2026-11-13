using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using FOMS.Api.Controllers;
using FOMS.Infrastructure.Persistence;
using Xunit;
using Payment = FOMS.Domain.Entities.Payment;
using Invoice = FOMS.Domain.Entities.Invoice;
using Client = FOMS.Domain.Entities.Client;
using InvoiceDocumentStatus = FOMS.Domain.Entities.InvoiceDocumentStatus;

namespace FOMS.Tests;

[Trait("Category", "Integration")]
public class RealSqlServerPaymentConcurrencyTests
{
    private const string MasterConnString = "Server=localhost\\SQLEXPRESS;Database=master;Trusted_Connection=True;TrustServerCertificate=True;Connection Timeout=30;";

    private sealed class UnprotectedApplicationDbContext : ApplicationDbContext
    {
        public UnprotectedApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Payment>().Ignore(p => p.RowVersion);
            modelBuilder.Entity<Invoice>().Ignore(i => i.RowVersion);
        }
    }

    private static ApplicationDbContext CreateRealSqlServerContext(string connString, bool ignoreRowVersion = false)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connString)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return ignoreRowVersion ? new UnprotectedApplicationDbContext(options) : new ApplicationDbContext(options);
    }

    private static FomsDbContext CreateRealMssqlDbContext(string connString)
    {
        var options = new DbContextOptionsBuilder<FomsDbContext>()
            .UseSqlServer(connString)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new FomsDbContext(options);
    }

    private static ClaimsPrincipal FmPrincipal()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "EMP-001"),
            new(ClaimTypes.Name, "Finance Manager User"),
            new(ClaimTypes.Role, "Finance Manager"),
            new("role", "Finance Manager"),
            new("employeeNumber", "EMP-001"),
            new("employee_id", "EMP-001")
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RealSqlServer_PaymentApprovalDeterministicRace_ProvesDoubleReceiptWithoutProtection_AndSingleReceiptWith409WhenProtected()
    {
        var dbName = $"FOMS_Test_Pay_Conc_{Guid.NewGuid():N}";
        var disposableConnString = $"Server=localhost\\SQLEXPRESS;Database={dbName};Trusted_Connection=True;TrustServerCertificate=True;Connection Timeout=30;";

        // 1. Create uniquely-named disposable database (never touches FOMS_DB)
        using (var masterConn = new SqlConnection(MasterConnString))
        {
            await masterConn.OpenAsync();
            using var cmd = masterConn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE [{dbName}];";
            await cmd.ExecuteNonQueryAsync();
        }

        try
        {
            // 2. Run EF Core migrations on the fresh disposable database
            using (var migrationContext = CreateRealSqlServerContext(disposableConnString))
            {
                await migrationContext.Database.MigrateAsync();
            }

            // 3. Seed test records
            var unprotPayId = "PAY-CONC-UNPROT-01";
            var unprotInvId = "BI-UNPROT-001";
            var protPayId = "PAY-CONC-PROT-01";
            var protInvId = "BI-PROT-001";

            using (var seedContext = CreateRealSqlServerContext(disposableConnString))
            {
                seedContext.Clients.Add(new Client
                {
                    Id = "CLI-CONC-01",
                    ClientCode = "CLI-CONC-01",
                    Name = "Concurrency Client",
                    Email = "concurrency@test.com",
                    IsActive = true
                });

                seedContext.Invoices.AddRange(
                    new Invoice
                    {
                        Id = unprotInvId,
                        InvoiceNo = unprotInvId,
                        ClientId = "CLI-CONC-01",
                        ClientName = "Concurrency Client",
                        DocumentStatus = InvoiceDocumentStatus.Approved,
                        BillingDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                        DueDate = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd"),
                        Description = "Concurrency test invoice",
                        WaybillIds = "WB-CONC-001",
                        PaymentStatus = "Unpaid",
                        EncodedBy = "EMP-003",
                        DateEncoded = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                        LastUpdated = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                        UpdatedBy = "EMP-003",
                        TotalAmount = 2000m,
                        Subtotal = 1785.71m,
                        VatAmount = 214.29m,
                        AmountPaid = 0m,
                        Balance = 2000m
                    },
                    new Invoice
                    {
                        Id = protInvId,
                        InvoiceNo = protInvId,
                        ClientId = "CLI-CONC-01",
                        ClientName = "Concurrency Client",
                        DocumentStatus = InvoiceDocumentStatus.Approved,
                        BillingDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                        DueDate = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd"),
                        Description = "Concurrency test invoice",
                        WaybillIds = "WB-CONC-001",
                        PaymentStatus = "Unpaid",
                        EncodedBy = "EMP-003",
                        DateEncoded = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                        LastUpdated = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                        UpdatedBy = "EMP-003",
                        TotalAmount = 2000m,
                        Subtotal = 1785.71m,
                        VatAmount = 214.29m,
                        AmountPaid = 0m,
                        Balance = 2000m
                    }
                );

                seedContext.Payments.AddRange(
                    new Payment
                    {
                        Id = unprotPayId,
                        InvoiceId = unprotInvId,
                        InvoiceNo = unprotInvId,
                        ClientId = "CLI-CONC-01",
                        ClientName = "Concurrency Client",
                        OrNumber = "OR-CONC-UNPROT-01",
                        Amount = 1000m,
                        PaymentStatus = "Validated",
                        PaymentMethod = "Bank Transfer",
                        ReferenceNumber = "REF-UNPROT-01",
                        RecordedBy = "EMP-003",
                        RecordedByEmployeeId = "EMP-003"
                    },
                    new Payment
                    {
                        Id = protPayId,
                        InvoiceId = protInvId,
                        InvoiceNo = protInvId,
                        ClientId = "CLI-CONC-01",
                        ClientName = "Concurrency Client",
                        OrNumber = "OR-CONC-PROT-01",
                        Amount = 1000m,
                        PaymentStatus = "Validated",
                        PaymentMethod = "Bank Transfer",
                        ReferenceNumber = "REF-PROT-01",
                        RecordedBy = "EMP-003",
                        RecordedByEmployeeId = "EMP-003"
                    }
                );

                await seedContext.SaveChangesAsync();
            }

            // ─────────────────────────────────────────────────────────────────
            // PART A: UNPROTECTED RACE (RowVersion ignored + unique index dropped)
            // Both contexts load BEFORE either saves -> both succeed and double-issue receipts
            // ─────────────────────────────────────────────────────────────────
            using (var sqlConn = new SqlConnection(disposableConnString))
            {
                await sqlConn.OpenAsync();
                using var dropIdxCmd = sqlConn.CreateCommand();
                dropIdxCmd.CommandText = @"
                    SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
                    DROP INDEX IF EXISTS IX_OfficialReceipts_PaymentId_Unique ON dbo.OfficialReceipts;
                ";
                await dropIdxCmd.ExecuteNonQueryAsync();
            }

            using (var unprotCtxA = CreateRealSqlServerContext(disposableConnString, ignoreRowVersion: true))
            using (var unprotMssqlA = CreateRealMssqlDbContext(disposableConnString))
            using (var unprotCtxB = CreateRealSqlServerContext(disposableConnString, ignoreRowVersion: true))
            using (var unprotMssqlB = CreateRealMssqlDbContext(disposableConnString))
            {
                var ctrlA = new FinancePaymentsController(unprotCtxA, unprotMssqlA, new Mock<IMediator>().Object)
                {
                    ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = FmPrincipal() } }
                };
                var ctrlB = new FinancePaymentsController(unprotCtxB, unprotMssqlB, new Mock<IMediator>().Object)
                {
                    ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = FmPrincipal() } }
                };

                // Deterministic race: when ctrlA is about to save, ctrlB reads the database before ctrlA commits
                Task<IActionResult>? taskB = null;
                unprotCtxA.SavingChanges += (sender, args) =>
                {
                    taskB ??= ctrlB.ApprovePayment(unprotPayId, new FinancePaymentsController.ValidationActionRequest("approve"));
                };

                var resA = await ctrlA.ApprovePayment(unprotPayId, new FinancePaymentsController.ValidationActionRequest("approve"));
                var resB = await taskB!;

                // Without RowVersion and without unique index, BOTH approvals succeed (200 OK)
                Assert.IsType<OkObjectResult>(resA);
                Assert.IsType<OkObjectResult>(resB);
            }

            using (var verifyUnprotCtx = CreateRealSqlServerContext(disposableConnString))
            {
                var receiptCount = await verifyUnprotCtx.OfficialReceipts.CountAsync(r => r.PaymentId == unprotPayId);
                // Proves TWO receipts were issued for the same payment when unprotected!
                Assert.Equal(2, receiptCount);
            }

            // Clean up Part A records and restore the unique index before Part B
            using (var sqlConn = new SqlConnection(disposableConnString))
            {
                await sqlConn.OpenAsync();
                using var createIdxCmd = sqlConn.CreateCommand();
                createIdxCmd.CommandText = @"
                    SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
                    DELETE FROM dbo.OfficialReceipts WHERE PaymentId = 'PAY-CONC-UNPROT-01';
                    CREATE UNIQUE NONCLUSTERED INDEX IX_OfficialReceipts_PaymentId_Unique
                    ON dbo.OfficialReceipts(PaymentId)
                    WHERE PaymentId IS NOT NULL;
                ";
                await createIdxCmd.ExecuteNonQueryAsync();
            }

            // ─────────────────────────────────────────────────────────────────
            // PART B: PROTECTED RACE (RowVersion enabled + IX_OfficialReceipts_PaymentId_Unique)
            // Both contexts load BEFORE either saves -> one succeeds (200), one gets 409 Conflict, exactly 1 receipt
            // ─────────────────────────────────────────────────────────────────
            using (var protCtxA = CreateRealSqlServerContext(disposableConnString, ignoreRowVersion: false))
            using (var protMssqlA = CreateRealMssqlDbContext(disposableConnString))
            using (var protCtxB = CreateRealSqlServerContext(disposableConnString, ignoreRowVersion: false))
            using (var protMssqlB = CreateRealMssqlDbContext(disposableConnString))
            {
                var ctrlA = new FinancePaymentsController(protCtxA, protMssqlA, new Mock<IMediator>().Object)
                {
                    ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = FmPrincipal() } }
                };
                var ctrlB = new FinancePaymentsController(protCtxB, protMssqlB, new Mock<IMediator>().Object)
                {
                    ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = FmPrincipal() } }
                };

                // Deterministic race: when ctrlA is about to save, ctrlB reads the database before ctrlA commits
                Task<IActionResult>? taskB = null;
                protCtxA.SavingChanges += (sender, args) =>
                {
                    taskB ??= ctrlB.ApprovePayment(protPayId, new FinancePaymentsController.ValidationActionRequest("approve"));
                };

                var resA = await ctrlA.ApprovePayment(protPayId, new FinancePaymentsController.ValidationActionRequest("approve"));
                var resB = await taskB!;

                // One succeeds (200 OK) and the other fails with 409 Conflict
                var results = new[] { resA, resB };
                Assert.Contains(results, r => r is OkObjectResult);
                Assert.Contains(results, r => r is ConflictObjectResult || (r is ObjectResult obj && obj.StatusCode == StatusCodes.Status409Conflict));
            }

            using (var verifyProtCtx = CreateRealSqlServerContext(disposableConnString))
            {
                var receiptCount = await verifyProtCtx.OfficialReceipts.CountAsync(r => r.PaymentId == protPayId);
                // Exactly 1 receipt issued
                Assert.Equal(1, receiptCount);

                var invoice = await verifyProtCtx.Invoices.FirstAsync(i => i.Id == protInvId);
                // Invoice balance reduced exactly once ($2000 - $1000 = $1000)
                Assert.Equal(1000m, invoice.Balance);
                Assert.Equal(1000m, invoice.AmountPaid);
            }
        }
        finally
        {
            // 4. Drop disposable database (clean teardown)
            try
            {
                using var masterConn = new SqlConnection(MasterConnString);
                await masterConn.OpenAsync();
                using var killCmd = masterConn.CreateCommand();
                killCmd.CommandText = $@"
                    ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{dbName}];
                ";
                await killCmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }
}
