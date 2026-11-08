using FOMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FOMS.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261117140000_AddSubmitterEmployeeIdColumnsForSod")]
public partial class AddSubmitterEmployeeIdColumnsForSod : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Batch 1: Ensure tables and submitter/recorder employee ID columns exist
        migrationBuilder.Sql(
            """
            SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;

            IF OBJECT_ID(N'[dbo].[Payments]', N'U') IS NOT NULL
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Payments') AND name = N'RecordedByEmployeeId')
                BEGIN
                    ALTER TABLE dbo.Payments ADD RecordedByEmployeeId NVARCHAR(50) NULL;
                END;
            END;

            IF OBJECT_ID(N'[dbo].[CashAdvances]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[CashAdvances] (
                    [AdvanceId] NVARCHAR(50) NOT NULL PRIMARY KEY,
                    [CourierName] NVARCHAR(100) NULL,
                    [TripReference] NVARCHAR(100) NULL,
                    [AmountGiven] DECIMAL(18,2) NOT NULL DEFAULT 0,
                    [ReleaseDate] DATETIME2 NULL,
                    [ReleasedBy] NVARCHAR(100) NULL,
                    [SubmittedByEmployeeId] NVARCHAR(50) NULL,
                    [Status] NVARCHAR(30) NOT NULL DEFAULT N'Pending Finance Review',
                    [Notes] NVARCHAR(500) NULL,
                    [ApprovedBy] NVARCHAR(100) NULL,
                    [ApprovedAt] DATETIME2 NULL,
                    [DisbursedBy] NVARCHAR(100) NULL,
                    [DisbursedAt] DATETIME2 NULL,
                    [CreatedAt] DATETIME2 NULL DEFAULT SYSUTCDATETIME(),
                    [DepartmentCode] NVARCHAR(50) NOT NULL DEFAULT N'DEP-OPS',
                    [Category] NVARCHAR(100) NOT NULL DEFAULT N'Fuel',
                    [FiscalYear] INT NOT NULL DEFAULT 2026,
                    [Month] INT NOT NULL DEFAULT 1,
                    [LocationType] NVARCHAR(50) NOT NULL DEFAULT N'NCR',
                    [DueDate] DATETIME2 NULL,
                    [ReturnDate] DATETIME2 NULL,
                    [RequiresCeoApproval] BIT NOT NULL DEFAULT 0,
                    [RejectionReason] NVARCHAR(500) NULL
                );
            END
            ELSE IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.CashAdvances') AND name = N'SubmittedByEmployeeId')
            BEGIN
                ALTER TABLE dbo.CashAdvances ADD SubmittedByEmployeeId NVARCHAR(50) NULL;
            END;

            IF OBJECT_ID(N'[dbo].[Liquidations]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[Liquidations] (
                    [liquidation_id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [reference_number] NVARCHAR(100) NULL,
                    [cash_advance_id] INT NULL,
                    [submitted_by] INT NULL,
                    [submitted_by_name] NVARCHAR(200) NULL,
                    [submitted_by_employee_id] NVARCHAR(50) NULL,
                    [total_amount] DECIMAL(18,2) NOT NULL DEFAULT 0,
                    [leftover_cash] DECIMAL(18,2) NULL,
                    [reimbursement_amount] DECIMAL(18,2) NULL,
                    [status] NVARCHAR(100) NULL,
                    [validated_by] INT NULL,
                    [validated_at] DATETIME2 NULL,
                    [date_submitted] DATETIME2 NULL,
                    [is_deleted] BIT NULL DEFAULT 0,
                    [created_at] DATETIME2 NULL DEFAULT SYSUTCDATETIME(),
                    [DueDate] DATETIME2 NULL,
                    [RejectionReason] NVARCHAR(500) NULL,
                    [Remarks] NVARCHAR(MAX) NULL,
                    [SupervisorPreApproved] BIT NOT NULL DEFAULT 0,
                    [DepartmentCode] NVARCHAR(50) NOT NULL DEFAULT N'DEP-OPS',
                    [Category] NVARCHAR(100) NOT NULL DEFAULT N'Fuel',
                    [FiscalYear] INT NOT NULL DEFAULT 2026,
                    [Month] INT NOT NULL DEFAULT 1
                );
            END
            ELSE IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Liquidations') AND name = N'submitted_by_employee_id')
            BEGIN
                ALTER TABLE dbo.Liquidations ADD submitted_by_employee_id NVARCHAR(50) NULL;
            END;

            IF OBJECT_ID(N'[dbo].[AIReviewHistory]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[AIReviewHistory] (
                    [review_id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [document_number] NVARCHAR(100) NULL,
                    [final_decision] NVARCHAR(100) NULL,
                    [reviewer] NVARCHAR(100) NULL,
                    [submitted_by_employee_id] NVARCHAR(50) NULL,
                    [decision_reason] NVARCHAR(500) NULL,
                    [reviewed_date] DATETIME2 NULL,
                    [is_deleted] BIT NULL DEFAULT 0,
                    [created_at] DATETIME2 NULL DEFAULT SYSUTCDATETIME()
                );
            END
            ELSE IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.AIReviewHistory') AND name = N'submitted_by_employee_id')
            BEGIN
                ALTER TABLE dbo.AIReviewHistory ADD submitted_by_employee_id NVARCHAR(50) NULL;
            END;
            """);

        // Batch 2: Backfill employee IDs from existing submitter/recorder columns and dbo.Employees where possible
        migrationBuilder.Sql(
            """
            SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;

            IF OBJECT_ID(N'[dbo].[Payments]', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.Payments', N'RecordedByEmployeeId') IS NOT NULL
               AND COL_LENGTH(N'dbo.Payments', N'RecordedBy') IS NOT NULL
            BEGIN
                EXEC sp_executesql N'
                    UPDATE p
                    SET p.RecordedByEmployeeId = CASE
                        WHEN p.RecordedBy LIKE N''EMP-%'' THEN LTRIM(RTRIM(p.RecordedBy))
                        WHEN e.Id IS NOT NULL THEN e.Id
                        ELSE NULL
                    END
                    FROM dbo.Payments p
                    LEFT JOIN dbo.Employees e
                        ON e.Id = p.RecordedBy
                        OR e.Username = p.RecordedBy
                        OR e.Name = p.RecordedBy
                    WHERE p.RecordedByEmployeeId IS NULL;
                ';
            END;

            IF OBJECT_ID(N'[dbo].[CashAdvances]', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.CashAdvances', N'SubmittedByEmployeeId') IS NOT NULL
               AND COL_LENGTH(N'dbo.CashAdvances', N'ReleasedBy') IS NOT NULL
            BEGIN
                EXEC sp_executesql N'
                    UPDATE ca
                    SET ca.SubmittedByEmployeeId = CASE
                        WHEN ca.ReleasedBy LIKE N''EMP-%'' THEN LTRIM(RTRIM(ca.ReleasedBy))
                        WHEN e.Id IS NOT NULL THEN e.Id
                        ELSE NULL
                    END
                    FROM dbo.CashAdvances ca
                    LEFT JOIN dbo.Employees e
                        ON e.Id = ca.ReleasedBy
                        OR e.Username = ca.ReleasedBy
                        OR e.Name = ca.ReleasedBy
                    WHERE ca.SubmittedByEmployeeId IS NULL;
                ';
            END;

            IF OBJECT_ID(N'[dbo].[Liquidations]', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.Liquidations', N'submitted_by_employee_id') IS NOT NULL
               AND COL_LENGTH(N'dbo.Liquidations', N'submitted_by_name') IS NOT NULL
            BEGIN
                EXEC sp_executesql N'
                    UPDATE l
                    SET l.submitted_by_employee_id = CASE
                        WHEN l.submitted_by_name LIKE N''EMP-%'' THEN LTRIM(RTRIM(l.submitted_by_name))
                        WHEN e.Id IS NOT NULL THEN e.Id
                        ELSE NULL
                    END
                    FROM dbo.Liquidations l
                    LEFT JOIN dbo.Employees e
                        ON e.Id = l.submitted_by_name
                        OR e.Username = l.submitted_by_name
                        OR e.Name = l.submitted_by_name
                    WHERE l.submitted_by_employee_id IS NULL;
                ';
            END;

            IF OBJECT_ID(N'[dbo].[AIReviewHistory]', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.AIReviewHistory', N'submitted_by_employee_id') IS NOT NULL
               AND COL_LENGTH(N'dbo.AIReviewHistory', N'reviewer') IS NOT NULL
            BEGIN
                EXEC sp_executesql N'
                    UPDATE r
                    SET r.submitted_by_employee_id = CASE
                        WHEN r.reviewer LIKE N''EMP-%'' THEN LTRIM(RTRIM(r.reviewer))
                        WHEN e.Id IS NOT NULL THEN e.Id
                        ELSE NULL
                    END
                    FROM dbo.AIReviewHistory r
                    LEFT JOIN dbo.Employees e
                        ON e.Id = r.reviewer
                        OR e.Username = r.reviewer
                        OR e.Name = r.reviewer
                    WHERE r.submitted_by_employee_id IS NULL;
                ';
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;

            IF OBJECT_ID(N'[dbo].[AIReviewHistory]', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.AIReviewHistory', N'submitted_by_employee_id') IS NOT NULL
            BEGIN
                ALTER TABLE dbo.AIReviewHistory DROP COLUMN submitted_by_employee_id;
            END;

            IF OBJECT_ID(N'[dbo].[Liquidations]', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.Liquidations', N'submitted_by_employee_id') IS NOT NULL
            BEGIN
                ALTER TABLE dbo.Liquidations DROP COLUMN submitted_by_employee_id;
            END;

            IF OBJECT_ID(N'[dbo].[CashAdvances]', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.CashAdvances', N'SubmittedByEmployeeId') IS NOT NULL
            BEGIN
                ALTER TABLE dbo.CashAdvances DROP COLUMN SubmittedByEmployeeId;
            END;

            IF OBJECT_ID(N'[dbo].[Payments]', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.Payments', N'RecordedByEmployeeId') IS NOT NULL
            BEGIN
                ALTER TABLE dbo.Payments DROP COLUMN RecordedByEmployeeId;
            END;
            """);
    }
}
