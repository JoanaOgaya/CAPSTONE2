using System;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using FOMS.Infrastructure.Persistence;
using Xunit;

namespace FOMS.Tests;

[Trait("Category", "Integration")]
public class FromScratchMigrationCheckTests
{
    private const string MasterConn = "Server=localhost\\SQLEXPRESS;Database=master;Trusted_Connection=True;TrustServerCertificate=True;Connection Timeout=30;";

    [Fact]
    public async Task FromScratchMigration_AllMigrationsApplySuccessfully_OnFreshDatabase()
    {
        var dbName = $"FOMS_Scratch_Mig_{Guid.NewGuid():N}";
        var scratchConn = $"Server=localhost\\SQLEXPRESS;Database={dbName};Trusted_Connection=True;TrustServerCertificate=True;Connection Timeout=30;";

        using (var master = new SqlConnection(MasterConn))
        {
            await master.OpenAsync();
            using var cmd = master.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE [{dbName}];";
            await cmd.ExecuteNonQueryAsync();
        }

        try
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(scratchConn)
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
                .Options;

            using var ctx = new ApplicationDbContext(options);
            await ctx.Database.MigrateAsync();

            // Verify table and column exist
            using var checkConn = new SqlConnection(scratchConn);
            await checkConn.OpenAsync();
            using var checkCmd = checkConn.CreateCommand();
            checkCmd.CommandText = @"
                SELECT 
                    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Payments') AND name = 'RowVersion') AS HasPaymentRowVersion,
                    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.OfficialReceipts') AND name = 'PaymentId') AS HasReceiptPaymentId,
                    (SELECT COUNT(*) FROM sys.indexes WHERE name = 'IX_OfficialReceipts_PaymentId_Unique') AS HasUniqueIndex;
            ";
            using var reader = await checkCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1, reader.GetInt32(0));
            Assert.Equal(1, reader.GetInt32(1));
            Assert.Equal(1, reader.GetInt32(2));
        }
        finally
        {
            try
            {
                using var master = new SqlConnection(MasterConn);
                await master.OpenAsync();
                using var dropCmd = master.CreateCommand();
                dropCmd.CommandText = $@"
                    ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{dbName}];
                ";
                await dropCmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }
}
