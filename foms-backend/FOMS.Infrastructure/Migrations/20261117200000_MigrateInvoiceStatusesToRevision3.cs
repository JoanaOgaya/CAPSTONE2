using System;
using FOMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FOMS.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261117200000_MigrateInvoiceStatusesToRevision3")]
    public partial class MigrateInvoiceStatusesToRevision3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                SET QUOTED_IDENTIFIER ON;
                SET ANSI_NULLS ON;

                IF OBJECT_ID(N'[dbo].[Invoices]', N'U') IS NOT NULL
                BEGIN
                    -- 1. Drop existing check constraint if present
                    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Invoices_DocumentStatus')
                    BEGIN
                        ALTER TABLE [dbo].[Invoices] DROP CONSTRAINT [CK_Invoices_DocumentStatus];
                    END

                    -- 2. Map legacy statuses to Revision 3 canonical vocabulary
                    IF COL_LENGTH(N'dbo.Invoices', N'DocumentStatus') IS NOT NULL
                    BEGIN
                        UPDATE [dbo].[Invoices]
                        SET [DocumentStatus] = N'For Review'
                        WHERE [DocumentStatus] IN (N'Pending HA Verification', N'Pending FM Approval', N'Returned to HA');

                        UPDATE [dbo].[Invoices]
                        SET [DocumentStatus] = N'Draft'
                        WHERE [DocumentStatus] = N'Returned to Creator';

                        -- 3. Re-apply strict Revision 3 CHECK constraint
                        ALTER TABLE [dbo].[Invoices] ADD CONSTRAINT [CK_Invoices_DocumentStatus]
                        CHECK ([DocumentStatus] IN (
                            N'Draft', N'For Review', N'Approved', N'Sent', N'Received', N'Disputed', N'Void'
                        ));
                    END
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                SET QUOTED_IDENTIFIER ON;
                SET ANSI_NULLS ON;

                IF OBJECT_ID(N'[dbo].[Invoices]', N'U') IS NOT NULL
                BEGIN
                    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Invoices_DocumentStatus')
                    BEGIN
                        ALTER TABLE [dbo].[Invoices] DROP CONSTRAINT [CK_Invoices_DocumentStatus];
                    END

                    IF COL_LENGTH(N'dbo.Invoices', N'DocumentStatus') IS NOT NULL
                    BEGIN
                        -- Allow legacy values and revert For Review back to Pending HA Verification
                        ALTER TABLE [dbo].[Invoices] ADD CONSTRAINT [CK_Invoices_DocumentStatus]
                        CHECK ([DocumentStatus] IN (
                            N'Draft', N'Pending HA Verification', N'Pending FM Approval',
                            N'Returned to HA', N'Returned to Creator', N'Approved',
                            N'Sent', N'Received', N'Disputed', N'Void', N'For Review'
                        ));

                        UPDATE [dbo].[Invoices]
                        SET [DocumentStatus] = N'Pending HA Verification'
                        WHERE [DocumentStatus] = N'For Review';
                    END
                END
                """);
        }
    }
}
