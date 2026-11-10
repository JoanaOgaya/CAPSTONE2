using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using FOMS.Api.Controllers;
using FOMS.Domain.Entities;
using FOMS.Infrastructure.Persistence;
using Xunit;

namespace FOMS.Tests;

[Trait("Category", "Integration")]
public class RealSqlServerConcurrencyAndDoublePostingTests
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
            new("name", "Finance Manager User")
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RealSqlServer_DeterministicRace_ProvesDoublePostingWithoutProtection_AndSinglePostingWith409WhenProtected()
    {
        var dbName = $"FOMS_Test_Conc_{Guid.NewGuid():N}";
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
            // 2. Run ALL EF Core migrations on the fresh disposable database
            using (var migrationContext = CreateRealSqlServerContext(disposableConnString))
            {
                await migrationContext.Database.MigrateAsync();
            }

            // 3. Seed clients and two test invoices (one for unprotected race, one for protected race)
            var unprotectedInvId = "BI-UNPROT-001";
            var unprotectedInvNo = "BI-UNPROT-001";
            var protectedInvId = "BI-PROT-001";
            var protectedInvNo = "BI-PROT-001";

            using (var seedContext = CreateRealSqlServerContext(disposableConnString))
            {
                seedContext.Clients.AddRange(
                    new Client
                    {
                        Id = "CL-001",
                        ClientCode = "CL-001",
                        Name = "Concurrency Test Corp",
                        BusinessName = "Concurrency Test Corp",
                        ContactPerson = "John Doe",
                        ContactNumber = "09123456789",
                        Email = "client@test.com",
                        Address = "123 Test St",
                        DmsClientId = "DMS-CL-001"
                    },
                    new Client
                    {
                        Id = "CA-001",
                        ClientCode = "CA-001",
                        Name = "Concurrency Test Client Account",
                        BusinessName = "Concurrency Test Client Account",
                        ContactPerson = "John Doe",
                        ContactNumber = "09123456789",
                        Email = "client-ca@test.com",
                        Address = "123 Test St",
                        DmsClientId = "DMS-CA-001"
                    }
                );

                seedContext.Invoices.AddRange(
                    new Invoice
                    {
                        Id = unprotectedInvId,
                        InvoiceNo = unprotectedInvNo,
                        ClientId = "CA-001",
                        ClientName = "Concurrency Test Client Account",
                        BillingDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                        DueDate = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd"),
                        Description = "Unprotected concurrency test invoice",
                        WaybillIds = "WB-CONC-001",
                        DocumentStatus = InvoiceDocumentStatus.Approved,
                        PaymentStatus = InvoicePaymentStatus.Unpaid,
                        TotalAmount = 1000m,
                        Subtotal = 892.86m,
                        VatAmount = 107.14m,
                        Balance = 1000m,
                        EncodedBy = "EMP-003",
                        DateEncoded = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                        LastUpdated = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                        UpdatedBy = "EMP-003"
                    },
                    new Invoice
                    {
                        Id = protectedInvId,
                        InvoiceNo = protectedInvNo,
                        ClientId = "CA-001",
                        ClientName = "Concurrency Test Client Account",
                        BillingDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                        DueDate = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd"),
                        Description = "Protected concurrency test invoice",
                        WaybillIds = "WB-CONC-002",
                        DocumentStatus = InvoiceDocumentStatus.Approved,
                        PaymentStatus = InvoicePaymentStatus.Unpaid,
                        TotalAmount = 1000m,
                        Subtotal = 892.86m,
                        VatAmount = 107.14m,
                        Balance = 1000m,
                        EncodedBy = "EMP-003",
                        DateEncoded = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                        LastUpdated = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                        UpdatedBy = "EMP-003"
                    }
                );

                await seedContext.SaveChangesAsync();
            }

            // ─────────────────────────────────────────────────────────────────
            // PART A: UNPROTECTED RACE (RowVersion ignored + unique index dropped)
            // Both contexts load the invoice BEFORE either saves -> both succeed and AR posts TWICE
            // ─────────────────────────────────────────────────────────────────
            using (var sqlConn = new SqlConnection(disposableConnString))
            {
                await sqlConn.OpenAsync();
                using var dropIdxCmd = sqlConn.CreateCommand();
                dropIdxCmd.CommandText = @"
                    SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
                    DROP INDEX IF EXISTS IX_JournalEntries_InvoiceId_UniquePosting ON dbo.JournalEntries;
                    DROP INDEX IF EXISTS IX_Invoices_AccrualJournalEntryId ON dbo.Invoices;
                ";
                await dropIdxCmd.ExecuteNonQueryAsync();
            }

            using (var unprotContextA = CreateRealSqlServerContext(disposableConnString, ignoreRowVersion: true))
            using (var unprotMssqlA = CreateRealMssqlDbContext(disposableConnString))
            using (var unprotContextB = CreateRealSqlServerContext(disposableConnString, ignoreRowVersion: true))
            using (var unprotMssqlB = CreateRealMssqlDbContext(disposableConnString))
            {
                // Deterministic race: BOTH contexts load the invoice in PendingFmApproval before either saves
                var loadedUnprotA = await unprotContextA.Invoices.FirstAsync(i => i.Id == unprotectedInvId);
                var loadedUnprotB = await unprotContextB.Invoices.FirstAsync(i => i.Id == unprotectedInvId);
                Assert.Equal(InvoiceDocumentStatus.Approved, loadedUnprotA.DocumentStatus);
                Assert.Equal(InvoiceDocumentStatus.Approved, loadedUnprotB.DocumentStatus);

                var unprotCtrlA = new InvoicesController(unprotContextA, unprotMssqlA)
                {
                    ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = FmPrincipal() } }
                };
                var unprotCtrlB = new InvoicesController(unprotContextB, unprotMssqlB)
                {
                    ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = FmPrincipal() } }
                };

                var unprotResA = await unprotCtrlA.TransitionInvoice(unprotectedInvId, "send", new InvoiceTransitionRequest());
                var unprotResB = await unprotCtrlB.TransitionInvoice(unprotectedInvId, "send", new InvoiceTransitionRequest());

                // Without RowVersion and without unique index, BOTH approvals succeed (200 OK)
                Assert.IsType<OkObjectResult>(unprotResA);
                Assert.IsType<OkObjectResult>(unprotResB);
            }

            using (var verifyUnprotCtx = CreateRealSqlServerContext(disposableConnString))
            {
                var unprotJeCount = await verifyUnprotCtx.JournalEntries.CountAsync(j => j.InvoiceId == unprotectedInvId);
                // Proves AR posted TWICE when unprotected!
                Assert.Equal(2, unprotJeCount);
            }

            // Clean up duplicate rows from Part A and restore the unique indexes before Part B
            using (var sqlConn = new SqlConnection(disposableConnString))
            {
                await sqlConn.OpenAsync();
                using var createIdxCmd = sqlConn.CreateCommand();
                createIdxCmd.CommandText = @"
                    SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
                    DELETE FROM dbo.JournalEntries WHERE InvoiceId = 'BI-UNPROT-001';

                    CREATE UNIQUE NONCLUSTERED INDEX IX_JournalEntries_InvoiceId_UniquePosting
                    ON dbo.JournalEntries(InvoiceId)
                    WHERE InvoiceId IS NOT NULL AND Posted = 1;

                    CREATE UNIQUE NONCLUSTERED INDEX IX_Invoices_AccrualJournalEntryId
                    ON dbo.Invoices(AccrualJournalEntryId)
                    WHERE AccrualJournalEntryId IS NOT NULL;
                ";
                await createIdxCmd.ExecuteNonQueryAsync();
            }

            // ─────────────────────────────────────────────────────────────────
            // PART B: PROTECTED RACE (RowVersion enabled + IX_JournalEntries_InvoiceId_UniquePosting enabled)
            // Both contexts load the invoice BEFORE either saves -> one succeeds (200), one gets 409 Conflict, AR posts ONCE
            // ─────────────────────────────────────────────────────────────────
            using var contextA = CreateRealSqlServerContext(disposableConnString, ignoreRowVersion: false);
            using var mssqlA = CreateRealMssqlDbContext(disposableConnString);
            using var contextB = CreateRealSqlServerContext(disposableConnString, ignoreRowVersion: false);
            using var mssqlB = CreateRealMssqlDbContext(disposableConnString);

            // Deterministic race: BOTH contexts load the invoice in PendingFmApproval before either saves
            var invA = await contextA.Invoices.FirstAsync(i => i.Id == protectedInvId);
            var invB = await contextB.Invoices.FirstAsync(i => i.Id == protectedInvId);

            Assert.NotNull(invA.RowVersion);
            Assert.NotNull(invB.RowVersion);
            Assert.Equal(InvoiceDocumentStatus.Approved, invA.DocumentStatus);
            Assert.Equal(InvoiceDocumentStatus.Approved, invB.DocumentStatus);

            var controllerA = new InvoicesController(contextA, mssqlA)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = FmPrincipal() } }
            };
            var controllerB = new InvoicesController(contextB, mssqlB)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = FmPrincipal() } }
            };

            // Context A saves first -> 200 OK
            var resultA = await controllerA.TransitionInvoice(protectedInvId, "send", new InvoiceTransitionRequest());
            Assert.IsType<OkObjectResult>(resultA);

            // Context B had already loaded invB in PendingFmApproval before A saved, and now attempts to save -> 409 Conflict
            var resultB = await controllerB.TransitionInvoice(protectedInvId, "send", new InvoiceTransitionRequest());
            var conflict = Assert.IsType<ObjectResult>(resultB);
            Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);

            // Verify AR posted ONCE for protected invoice
            using var verifyContext = CreateRealSqlServerContext(disposableConnString);
            var finalInv = await verifyContext.Invoices.FirstAsync(i => i.Id == protectedInvId);
            Assert.Equal(InvoiceDocumentStatus.Sent, finalInv.DocumentStatus);
            Assert.True(finalInv.AccrualRevenueRecognized);
            Assert.NotNull(finalInv.AccrualJournalEntryId);

            var protJeCount = await verifyContext.JournalEntries
                .CountAsync(j => j.InvoiceId == protectedInvId);
            Assert.Equal(1, protJeCount);

            // Also verify that even if RowVersion were bypassed, IX_JournalEntries_InvoiceId_UniquePosting alone blocks a 2nd posted JE for the same InvoiceId
            var duplicateJe = new JournalEntry
            {
                Id = Guid.NewGuid().ToString(),
                EntryNumber = $"JE-ACCRUAL-DUP-{Guid.NewGuid():N}"[..24],
                InvoiceId = protectedInvId,
                Date = DateTime.UtcNow,
                Description = "Duplicate Accrual Entry for same InvoiceId",
                Posted = true
            };
            verifyContext.JournalEntries.Add(duplicateJe);
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => verifyContext.SaveChangesAsync());
            Assert.NotNull(ex.InnerException);
            Assert.Contains("IX_JournalEntries_InvoiceId_UniquePosting", ex.InnerException.Message);
        }
        finally
        {
            // Drop the disposable database completely
            SqlConnection.ClearAllPools();
            using var masterConn = new SqlConnection(MasterConnString);
            await masterConn.OpenAsync();
            using var cmd = masterConn.CreateCommand();
            cmd.CommandText = $@"
                IF DB_ID('{dbName}') IS NOT NULL 
                BEGIN 
                    ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; 
                    DROP DATABASE [{dbName}]; 
                END;";
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
