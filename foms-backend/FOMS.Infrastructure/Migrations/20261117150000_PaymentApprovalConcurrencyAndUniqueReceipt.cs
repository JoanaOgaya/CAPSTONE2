using FOMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FOMS.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261117150000_PaymentApprovalConcurrencyAndUniqueReceipt")]
    public partial class PaymentApprovalConcurrencyAndUniqueReceipt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;

                IF OBJECT_ID(N'[dbo].[Payments]', N'U') IS NOT NULL
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Payments]') AND name = N'Flow')
                    BEGIN
                        ALTER TABLE [dbo].[Payments] ADD [Flow] nvarchar(max) NOT NULL CONSTRAINT [DF_Payments_Flow] DEFAULT N'inflow';
                    END;

                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Payments]') AND name = N'RowVersion')
                    BEGIN
                        ALTER TABLE [dbo].[Payments] ADD [RowVersion] rowversion;
                    END;
                END;

                IF OBJECT_ID(N'[dbo].[OfficialReceipts]', N'U') IS NOT NULL
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[OfficialReceipts]') AND name = N'PaymentId')
                    BEGIN
                        ALTER TABLE [dbo].[OfficialReceipts] ADD [PaymentId] nvarchar(450) NULL;
                    END;
                END;
                """);

            migrationBuilder.Sql(
                """
                SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;

                IF OBJECT_ID(N'[dbo].[OfficialReceipts]', N'U') IS NOT NULL
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[OfficialReceipts]') AND name = N'IX_OfficialReceipts_PaymentId_Unique')
                    BEGIN
                        EXEC(N'CREATE UNIQUE NONCLUSTERED INDEX [IX_OfficialReceipts_PaymentId_Unique] ON [dbo].[OfficialReceipts]([PaymentId]) WHERE [PaymentId] IS NOT NULL;');
                    END;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;

                IF OBJECT_ID(N'[dbo].[OfficialReceipts]', N'U') IS NOT NULL
                BEGIN
                    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[OfficialReceipts]') AND name = N'IX_OfficialReceipts_PaymentId_Unique')
                    BEGIN
                        DROP INDEX [IX_OfficialReceipts_PaymentId_Unique] ON [dbo].[OfficialReceipts];
                    END;

                    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[OfficialReceipts]') AND name = N'PaymentId')
                    BEGIN
                        ALTER TABLE [dbo].[OfficialReceipts] DROP COLUMN [PaymentId];
                    END;
                END;

                IF OBJECT_ID(N'[dbo].[Payments]', N'U') IS NOT NULL
                BEGIN
                    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Payments]') AND name = N'RowVersion')
                    BEGIN
                        ALTER TABLE [dbo].[Payments] DROP COLUMN [RowVersion];
                    END;
                END;
                """);
        }
    }
}
