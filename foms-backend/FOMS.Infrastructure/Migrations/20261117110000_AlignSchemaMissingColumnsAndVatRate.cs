using FOMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FOMS.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261117110000_AlignSchemaMissingColumnsAndVatRate")]
public partial class AlignSchemaMissingColumnsAndVatRate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
            IF OBJECT_ID(N'[dbo].[Invoices]', N'U') IS NOT NULL
            BEGIN
                -- Lossless conversion of VatRate to decimal(18,2) to match C# domain entity decimal type
                ALTER TABLE dbo.Invoices ALTER COLUMN VatRate DECIMAL(18,2) NOT NULL;

                -- Missing entity-mapped invoice columns
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Invoices') AND name = N'CtcWaybills')
                    ALTER TABLE dbo.Invoices ADD CtcWaybills NVARCHAR(MAX) NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Invoices') AND name = N'DiscountAmount')
                    ALTER TABLE dbo.Invoices ADD DiscountAmount DECIMAL(18,2) NOT NULL CONSTRAINT DF_Invoices_DiscountAmount DEFAULT (0);

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Invoices') AND name = N'DiscountApprovedBy')
                    ALTER TABLE dbo.Invoices ADD DiscountApprovedBy NVARCHAR(255) NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Invoices') AND name = N'DiscountRate')
                    ALTER TABLE dbo.Invoices ADD DiscountRate DECIMAL(5,4) NOT NULL CONSTRAINT DF_Invoices_DiscountRate DEFAULT (0);

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Invoices') AND name = N'DiscountReason')
                    ALTER TABLE dbo.Invoices ADD DiscountReason NVARCHAR(MAX) NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Invoices') AND name = N'HasCtc')
                    ALTER TABLE dbo.Invoices ADD HasCtc BIT NOT NULL CONSTRAINT DF_Invoices_HasCtc DEFAULT (0);

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Invoices') AND name = N'IsSuspect')
                    ALTER TABLE dbo.Invoices ADD IsSuspect BIT NOT NULL CONSTRAINT DF_Invoices_IsSuspect DEFAULT (0);

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Invoices') AND name = N'SuspectReason')
                    ALTER TABLE dbo.Invoices ADD SuspectReason NVARCHAR(MAX) NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Invoices') AND name = N'VoidReason')
                    ALTER TABLE dbo.Invoices ADD VoidReason NVARCHAR(MAX) NULL;
            END;

            IF OBJECT_ID(N'[dbo].[Clients]', N'U') IS NOT NULL
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Clients') AND name = N'account_handler')
                    ALTER TABLE dbo.Clients ADD account_handler NVARCHAR(MAX) NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Clients') AND name = N'billing_schedule')
                    ALTER TABLE dbo.Clients ADD billing_schedule NVARCHAR(MAX) NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Clients') AND name = N'credit_term_days')
                    ALTER TABLE dbo.Clients ADD credit_term_days INT NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Clients') AND name = N'signed_proposal_url')
                    ALTER TABLE dbo.Clients ADD signed_proposal_url NVARCHAR(MAX) NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Clients') AND name = N'signed_proposal_attached')
                    ALTER TABLE dbo.Clients ADD signed_proposal_attached BIT NOT NULL CONSTRAINT DF_Clients_signed_proposal_attached DEFAULT (0);
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
            -- Revert added constraints and columns
            IF OBJECT_ID(N'[dbo].[Clients]', N'U') IS NOT NULL
            BEGIN
                ALTER TABLE dbo.Clients DROP CONSTRAINT IF EXISTS DF_Clients_signed_proposal_attached;
                IF COL_LENGTH('dbo.Clients', 'signed_proposal_attached') IS NOT NULL ALTER TABLE dbo.Clients DROP COLUMN signed_proposal_attached;
                IF COL_LENGTH('dbo.Clients', 'signed_proposal_url') IS NOT NULL ALTER TABLE dbo.Clients DROP COLUMN signed_proposal_url;
                IF COL_LENGTH('dbo.Clients', 'credit_term_days') IS NOT NULL ALTER TABLE dbo.Clients DROP COLUMN credit_term_days;
                IF COL_LENGTH('dbo.Clients', 'billing_schedule') IS NOT NULL ALTER TABLE dbo.Clients DROP COLUMN billing_schedule;
                IF COL_LENGTH('dbo.Clients', 'account_handler') IS NOT NULL ALTER TABLE dbo.Clients DROP COLUMN account_handler;
            END;

            IF OBJECT_ID(N'[dbo].[Invoices]', N'U') IS NOT NULL
            BEGIN
                ALTER TABLE dbo.Invoices DROP CONSTRAINT IF EXISTS DF_Invoices_DiscountAmount;
                ALTER TABLE dbo.Invoices DROP CONSTRAINT IF EXISTS DF_Invoices_DiscountRate;
                ALTER TABLE dbo.Invoices DROP CONSTRAINT IF EXISTS DF_Invoices_HasCtc;
                ALTER TABLE dbo.Invoices DROP CONSTRAINT IF EXISTS DF_Invoices_IsSuspect;
            END;
            """);
    }
}
