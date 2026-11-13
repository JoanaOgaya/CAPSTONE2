using FOMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FOMS.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261117160000_AddLiquidationsMissingColumns")]
public partial class AddLiquidationsMissingColumns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
            IF OBJECT_ID(N'[dbo].[Liquidations]', N'U') IS NOT NULL
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Liquidations') AND name = N'DueDate')
                    ALTER TABLE dbo.Liquidations ADD DueDate DATETIME2 NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Liquidations') AND name = N'RejectionReason')
                    ALTER TABLE dbo.Liquidations ADD RejectionReason NVARCHAR(MAX) NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Liquidations') AND name = N'Remarks')
                    ALTER TABLE dbo.Liquidations ADD Remarks NVARCHAR(MAX) NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Liquidations') AND name = N'SupervisorPreApproved')
                    ALTER TABLE dbo.Liquidations ADD SupervisorPreApproved BIT NOT NULL CONSTRAINT DF_Liquidations_SupervisorPreApproved DEFAULT (0);

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Liquidations') AND name = N'DepartmentCode')
                    ALTER TABLE dbo.Liquidations ADD DepartmentCode NVARCHAR(50) NOT NULL CONSTRAINT DF_Liquidations_DepartmentCode DEFAULT ('DEP-OPS');

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Liquidations') AND name = N'Category')
                    ALTER TABLE dbo.Liquidations ADD Category NVARCHAR(100) NOT NULL CONSTRAINT DF_Liquidations_Category DEFAULT ('Fuel');

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Liquidations') AND name = N'FiscalYear')
                    ALTER TABLE dbo.Liquidations ADD FiscalYear INT NOT NULL CONSTRAINT DF_Liquidations_FiscalYear DEFAULT (2026);

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Liquidations') AND name = N'Month')
                    ALTER TABLE dbo.Liquidations ADD Month INT NOT NULL CONSTRAINT DF_Liquidations_Month DEFAULT (1);
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
            IF OBJECT_ID(N'[dbo].[Liquidations]', N'U') IS NOT NULL
            BEGIN
                ALTER TABLE dbo.Liquidations DROP CONSTRAINT IF EXISTS DF_Liquidations_SupervisorPreApproved;
                ALTER TABLE dbo.Liquidations DROP CONSTRAINT IF EXISTS DF_Liquidations_DepartmentCode;
                ALTER TABLE dbo.Liquidations DROP CONSTRAINT IF EXISTS DF_Liquidations_Category;
                ALTER TABLE dbo.Liquidations DROP CONSTRAINT IF EXISTS DF_Liquidations_FiscalYear;
                ALTER TABLE dbo.Liquidations DROP CONSTRAINT IF EXISTS DF_Liquidations_Month;

                IF COL_LENGTH('dbo.Liquidations', 'DueDate') IS NOT NULL ALTER TABLE dbo.Liquidations DROP COLUMN DueDate;
                IF COL_LENGTH('dbo.Liquidations', 'RejectionReason') IS NOT NULL ALTER TABLE dbo.Liquidations DROP COLUMN RejectionReason;
                IF COL_LENGTH('dbo.Liquidations', 'Remarks') IS NOT NULL ALTER TABLE dbo.Liquidations DROP COLUMN Remarks;
                IF COL_LENGTH('dbo.Liquidations', 'SupervisorPreApproved') IS NOT NULL ALTER TABLE dbo.Liquidations DROP COLUMN SupervisorPreApproved;
                IF COL_LENGTH('dbo.Liquidations', 'DepartmentCode') IS NOT NULL ALTER TABLE dbo.Liquidations DROP COLUMN DepartmentCode;
                IF COL_LENGTH('dbo.Liquidations', 'Category') IS NOT NULL ALTER TABLE dbo.Liquidations DROP COLUMN Category;
                IF COL_LENGTH('dbo.Liquidations', 'FiscalYear') IS NOT NULL ALTER TABLE dbo.Liquidations DROP COLUMN FiscalYear;
                IF COL_LENGTH('dbo.Liquidations', 'Month') IS NOT NULL ALTER TABLE dbo.Liquidations DROP COLUMN Month;
            END;
            """);
    }
}
