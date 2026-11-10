using System;
using FOMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FOMS.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261117190000_AddDepartmentControlAndChangeLogs")]
    public partial class AddDepartmentControlAndChangeLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. DDL: Ensure dbo.Departments exists and has columns
            migrationBuilder.Sql(
                """
                SET QUOTED_IDENTIFIER ON;
                SET ANSI_NULLS ON;

                IF OBJECT_ID(N'[dbo].[Departments]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[Departments] (
                        [department_id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_Departments] PRIMARY KEY,
                        [department_code] NVARCHAR(50) NOT NULL CONSTRAINT [UQ_Departments_DepartmentCode] UNIQUE,
                        [department_name] NVARCHAR(100) NOT NULL,
                        [is_active] BIT NOT NULL CONSTRAINT [DF_Departments_IsActive] DEFAULT 1,
                        [created_at] DATETIME2 NOT NULL CONSTRAINT [DF_Departments_CreatedAt] DEFAULT SYSUTCDATETIME(),
                        [is_overhead] BIT NOT NULL CONSTRAINT [DF_Departments_IsOverhead] DEFAULT 0,
                        [is_legacy] BIT NOT NULL CONSTRAINT [DF_Departments_IsLegacy] DEFAULT 0
                    );
                END
                ELSE
                BEGIN
                    IF COL_LENGTH(N'dbo.Departments', N'is_overhead') IS NULL
                    BEGIN
                        ALTER TABLE [dbo].[Departments] ADD [is_overhead] BIT NOT NULL CONSTRAINT [DF_Departments_IsOverhead] DEFAULT 0;
                    END

                    IF COL_LENGTH(N'dbo.Departments', N'is_legacy') IS NULL
                    BEGIN
                        ALTER TABLE [dbo].[Departments] ADD [is_legacy] BIT NOT NULL CONSTRAINT [DF_Departments_IsLegacy] DEFAULT 0;
                    END
                END
                """);

            // 2. DML: Populate canonical and legacy departments
            migrationBuilder.Sql(
                """
                SET QUOTED_IDENTIFIER ON;
                SET ANSI_NULLS ON;

                EXEC(N'
                IF OBJECT_ID(N''[dbo].[Departments]'', N''U'') IS NOT NULL
                BEGIN
                    IF EXISTS (SELECT 1 FROM [dbo].[Departments] WHERE [department_code] = ''DEP-FIN'')
                    BEGIN
                        UPDATE [dbo].[Departments]
                        SET [department_name] = ''Finance & Accounting'', [is_overhead] = 0, [is_legacy] = 0
                        WHERE [department_code] = ''DEP-FIN'';
                    END
                    ELSE
                    BEGIN
                        INSERT INTO [dbo].[Departments] ([department_code], [department_name], [is_active], [created_at], [is_overhead], [is_legacy])
                        VALUES (''DEP-FIN'', ''Finance & Accounting'', 1, SYSUTCDATETIME(), 0, 0);
                    END

                    IF NOT EXISTS (SELECT 1 FROM [dbo].[Departments] WHERE [department_code] = ''DEP-CCS'')
                    BEGIN
                        INSERT INTO [dbo].[Departments] ([department_code], [department_name], [is_active], [created_at], [is_overhead], [is_legacy])
                        VALUES (''DEP-CCS'', ''Coordinator & Customer Service Team'', 1, SYSUTCDATETIME(), 0, 0);
                    END

                    IF NOT EXISTS (SELECT 1 FROM [dbo].[Departments] WHERE [department_code] = ''DEP-DSP'')
                    BEGIN
                        INSERT INTO [dbo].[Departments] ([department_code], [department_name], [is_active], [created_at], [is_overhead], [is_legacy])
                        VALUES (''DEP-DSP'', ''Dispatch Team'', 1, SYSUTCDATETIME(), 0, 0);
                    END

                    IF NOT EXISTS (SELECT 1 FROM [dbo].[Departments] WHERE [department_code] = ''DEP-FWD'')
                    BEGIN
                        INSERT INTO [dbo].[Departments] ([department_code], [department_name], [is_active], [created_at], [is_overhead], [is_legacy])
                        VALUES (''DEP-FWD'', ''Forwarding Team'', 1, SYSUTCDATETIME(), 0, 0);
                    END

                    IF NOT EXISTS (SELECT 1 FROM [dbo].[Departments] WHERE [department_code] = ''DEP-OVH'')
                    BEGIN
                        INSERT INTO [dbo].[Departments] ([department_code], [department_name], [is_active], [created_at], [is_overhead], [is_legacy])
                        VALUES (''DEP-OVH'', ''Company-wide (Overhead)'', 1, SYSUTCDATETIME(), 1, 0);
                    END

                    UPDATE [dbo].[Departments]
                    SET [is_legacy] = 1
                    WHERE [department_code] IN (''DEP-OPS'', ''DEP-FLT'', ''DEP-HR'', ''DEP-IT'', ''DEP-EXEC'');
                END
                ');
                """);

            // 3. Request tables lock columns & Change logs table
            migrationBuilder.Sql(
                """
                SET QUOTED_IDENTIFIER ON;
                SET ANSI_NULLS ON;

                IF OBJECT_ID(N'[dbo].[CashAdvances]', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'dbo.CashAdvances', N'IsDepartmentLocked') IS NULL
                    BEGIN
                        ALTER TABLE [dbo].[CashAdvances] ADD [IsDepartmentLocked] BIT NOT NULL CONSTRAINT [DF_CashAdvances_IsDeptLocked] DEFAULT 0;
                    END

                    IF COL_LENGTH(N'dbo.CashAdvances', N'DepartmentChangeReason') IS NULL
                    BEGIN
                        ALTER TABLE [dbo].[CashAdvances] ADD [DepartmentChangeReason] NVARCHAR(500) NULL;
                    END
                END

                IF OBJECT_ID(N'[dbo].[DirectDisbursements]', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'dbo.DirectDisbursements', N'is_department_locked') IS NULL
                    BEGIN
                        ALTER TABLE [dbo].[DirectDisbursements] ADD [is_department_locked] BIT NOT NULL CONSTRAINT [DF_DirectDisbursements_IsDeptLocked] DEFAULT 0;
                    END

                    IF COL_LENGTH(N'dbo.DirectDisbursements', N'department_change_reason') IS NULL
                    BEGIN
                        ALTER TABLE [dbo].[DirectDisbursements] ADD [department_change_reason] NVARCHAR(500) NULL;
                    END
                END

                IF OBJECT_ID(N'[dbo].[Reimbursements]', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'dbo.Reimbursements', N'is_department_locked') IS NULL
                    BEGIN
                        ALTER TABLE [dbo].[Reimbursements] ADD [is_department_locked] BIT NOT NULL CONSTRAINT [DF_Reimbursements_IsDeptLocked] DEFAULT 0;
                    END

                    IF COL_LENGTH(N'dbo.Reimbursements', N'department_change_reason') IS NULL
                    BEGIN
                        ALTER TABLE [dbo].[Reimbursements] ADD [department_change_reason] NVARCHAR(500) NULL;
                    END
                END

                IF OBJECT_ID(N'[dbo].[DepartmentChangeLogs]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[DepartmentChangeLogs] (
                        [LogId] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_DepartmentChangeLogs] PRIMARY KEY,
                        [RequestType] NVARCHAR(50) NOT NULL,
                        [RequestId] NVARCHAR(50) NOT NULL,
                        [OldDepartmentCode] NVARCHAR(20) NOT NULL,
                        [NewDepartmentCode] NVARCHAR(20) NOT NULL,
                        [Reason] NVARCHAR(500) NOT NULL,
                        [ChangedBy] NVARCHAR(50) NOT NULL,
                        [ChangedAt] DATETIME2 NOT NULL CONSTRAINT [DF_DepartmentChangeLogs_ChangedAt] DEFAULT SYSUTCDATETIME()
                    );

                    CREATE INDEX [IX_DepartmentChangeLogs_Request] ON [dbo].[DepartmentChangeLogs] ([RequestType], [RequestId]);
                    CREATE INDEX [IX_DepartmentChangeLogs_ChangedAt] ON [dbo].[DepartmentChangeLogs] ([ChangedAt]);
                END

                EXEC(N'
                CREATE OR ALTER TRIGGER [dbo].[TR_DepartmentChangeLogs_Immutable]
                ON [dbo].[DepartmentChangeLogs]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    RAISERROR(N''TR_DepartmentChangeLogs_Immutable: dbo.DepartmentChangeLogs is insert-only and immutable. UPDATE and DELETE are prohibited.'', 16, 1);
                    ROLLBACK TRANSACTION;
                END;
                ');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                SET QUOTED_IDENTIFIER ON;
                SET ANSI_NULLS ON;

                IF OBJECT_ID(N'[dbo].[TR_DepartmentChangeLogs_Immutable]', N'TR') IS NOT NULL
                    DROP TRIGGER [dbo].[TR_DepartmentChangeLogs_Immutable];

                IF OBJECT_ID(N'[dbo].[DepartmentChangeLogs]', N'U') IS NOT NULL
                    DROP TABLE [dbo].[DepartmentChangeLogs];

                IF OBJECT_ID(N'[dbo].[CashAdvances]', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'dbo.CashAdvances', N'DepartmentChangeReason') IS NOT NULL
                        ALTER TABLE [dbo].[CashAdvances] DROP COLUMN [DepartmentChangeReason];

                    IF COL_LENGTH(N'dbo.CashAdvances', N'IsDepartmentLocked') IS NOT NULL
                    BEGIN
                        ALTER TABLE [dbo].[CashAdvances] DROP CONSTRAINT [DF_CashAdvances_IsDeptLocked];
                        ALTER TABLE [dbo].[CashAdvances] DROP COLUMN [IsDepartmentLocked];
                    END
                END

                IF OBJECT_ID(N'[dbo].[DirectDisbursements]', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'dbo.DirectDisbursements', N'department_change_reason') IS NOT NULL
                        ALTER TABLE [dbo].[DirectDisbursements] DROP COLUMN [department_change_reason];

                    IF COL_LENGTH(N'dbo.DirectDisbursements', N'is_department_locked') IS NOT NULL
                    BEGIN
                        ALTER TABLE [dbo].[DirectDisbursements] DROP CONSTRAINT [DF_DirectDisbursements_IsDeptLocked];
                        ALTER TABLE [dbo].[DirectDisbursements] DROP COLUMN [is_department_locked];
                    END
                END

                IF OBJECT_ID(N'[dbo].[Reimbursements]', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'dbo.Reimbursements', N'department_change_reason') IS NOT NULL
                        ALTER TABLE [dbo].[Reimbursements] DROP COLUMN [department_change_reason];

                    IF COL_LENGTH(N'dbo.Reimbursements', N'is_department_locked') IS NOT NULL
                    BEGIN
                        ALTER TABLE [dbo].[Reimbursements] DROP CONSTRAINT [DF_Reimbursements_IsDeptLocked];
                        ALTER TABLE [dbo].[Reimbursements] DROP COLUMN [is_department_locked];
                    END
                END

                IF OBJECT_ID(N'[dbo].[Departments]', N'U') IS NOT NULL
                BEGIN
                    DELETE FROM [dbo].[Departments] WHERE [department_code] IN ('DEP-CCS', 'DEP-DSP', 'DEP-FWD', 'DEP-OVH');

                    UPDATE [dbo].[Departments]
                    SET [department_name] = 'Finance'
                    WHERE [department_code] = 'DEP-FIN';

                    IF COL_LENGTH(N'dbo.Departments', N'is_overhead') IS NOT NULL
                    BEGIN
                        ALTER TABLE [dbo].[Departments] DROP CONSTRAINT [DF_Departments_IsOverhead];
                        ALTER TABLE [dbo].[Departments] DROP COLUMN [is_overhead];
                    END

                    IF COL_LENGTH(N'dbo.Departments', N'is_legacy') IS NOT NULL
                    BEGIN
                        ALTER TABLE [dbo].[Departments] DROP CONSTRAINT [DF_Departments_IsLegacy];
                        ALTER TABLE [dbo].[Departments] DROP COLUMN [is_legacy];
                    END
                END
                """);
        }
    }
}
