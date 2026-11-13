using FOMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FOMS.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261117120000_SetRateColumnsPrecisionTo5_4")]
public partial class SetRateColumnsPrecisionTo5_4 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
            IF OBJECT_ID(N'[dbo].[Invoices]', N'U') IS NOT NULL
            BEGIN
                ALTER TABLE dbo.Invoices ALTER COLUMN VatRate DECIMAL(5,4) NOT NULL;
                ALTER TABLE dbo.Invoices ALTER COLUMN DiscountRate DECIMAL(5,4) NOT NULL;
                IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Invoices') AND name = N'FuelSurchargeRate')
                    ALTER TABLE dbo.Invoices ALTER COLUMN FuelSurchargeRate DECIMAL(5,4) NOT NULL;
                IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Invoices') AND name = N'discount_rate')
                    ALTER TABLE dbo.Invoices ALTER COLUMN discount_rate DECIMAL(5,4) NULL;
            END;

            IF OBJECT_ID(N'[dbo].[ClientRates]', N'U') IS NOT NULL
            BEGIN
                IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.ClientRates') AND name = N'fuel_surcharge_rate')
                    ALTER TABLE dbo.ClientRates ALTER COLUMN fuel_surcharge_rate DECIMAL(5,4) NULL;
                IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.ClientRates') AND name = N'valuation_rate')
                    ALTER TABLE dbo.ClientRates ALTER COLUMN valuation_rate DECIMAL(5,4) NULL;
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
            IF OBJECT_ID(N'[dbo].[Invoices]', N'U') IS NOT NULL
            BEGIN
                ALTER TABLE dbo.Invoices ALTER COLUMN VatRate DECIMAL(18,2) NOT NULL;
                IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Invoices') AND name = N'FuelSurchargeRate')
                    ALTER TABLE dbo.Invoices ALTER COLUMN FuelSurchargeRate DECIMAL(9,6) NOT NULL;
            END;

            IF OBJECT_ID(N'[dbo].[ClientRates]', N'U') IS NOT NULL
            BEGIN
                IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.ClientRates') AND name = N'fuel_surcharge_rate')
                    ALTER TABLE dbo.ClientRates ALTER COLUMN fuel_surcharge_rate DECIMAL(18,4) NULL;
                IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.ClientRates') AND name = N'valuation_rate')
                    ALTER TABLE dbo.ClientRates ALTER COLUMN valuation_rate DECIMAL(18,4) NULL;
            END;
            """);
    }
}
