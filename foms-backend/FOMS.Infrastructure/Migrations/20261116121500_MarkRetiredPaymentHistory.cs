using FOMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FOMS.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261116121500_MarkRetiredPaymentHistory")]
public partial class MarkRetiredPaymentHistory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'[dbo].[RefundRequests]', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.RefundRequests', N'IsLegacyHistory') IS NULL
                ALTER TABLE [dbo].[RefundRequests] ADD [IsLegacyHistory] bit NOT NULL
                    CONSTRAINT [DF_RefundRequests_IsLegacyHistory] DEFAULT (1) WITH VALUES;

            IF OBJECT_ID(N'[dbo].[PaymentTransactions]', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.PaymentTransactions', N'IsLegacyHistory') IS NULL
                ALTER TABLE [dbo].[PaymentTransactions] ADD [IsLegacyHistory] bit NOT NULL
                    CONSTRAINT [DF_PaymentTransactions_IsLegacyHistory] DEFAULT (1) WITH VALUES;

            IF OBJECT_ID(N'[dbo].[PaymentReceipts]', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.PaymentReceipts', N'IsLegacyHistory') IS NULL
                ALTER TABLE [dbo].[PaymentReceipts] ADD [IsLegacyHistory] bit NOT NULL
                    CONSTRAINT [DF_PaymentReceipts_IsLegacyHistory] DEFAULT (1) WITH VALUES;

            IF OBJECT_ID(N'[dbo].[ProcessedWebhookEvents]', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.ProcessedWebhookEvents', N'IsLegacyHistory') IS NULL
                ALTER TABLE [dbo].[ProcessedWebhookEvents] ADD [IsLegacyHistory] bit NOT NULL
                    CONSTRAINT [DF_ProcessedWebhookEvents_IsLegacyHistory] DEFAULT (1) WITH VALUES;

            IF OBJECT_ID(N'[dbo].[SpeedPayTransactions]', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.SpeedPayTransactions', N'IsLegacyHistory') IS NULL
                ALTER TABLE [dbo].[SpeedPayTransactions] ADD [IsLegacyHistory] bit NOT NULL
                    CONSTRAINT [DF_SpeedPayTransactions_IsLegacyHistory] DEFAULT (1) WITH VALUES;

            IF OBJECT_ID(N'[dbo].[SpeedPayManualSubmissions]', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.SpeedPayManualSubmissions', N'IsLegacyHistory') IS NULL
                ALTER TABLE [dbo].[SpeedPayManualSubmissions] ADD [IsLegacyHistory] bit NOT NULL
                    CONSTRAINT [DF_SpeedPayManualSubmissions_IsLegacyHistory] DEFAULT (1) WITH VALUES;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Keep legacy history markers and records when rolling back application code.
    }
}
