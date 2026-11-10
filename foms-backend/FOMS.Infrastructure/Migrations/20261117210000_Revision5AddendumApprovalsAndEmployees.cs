using System;
using FOMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FOMS.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261117210000_Revision5AddendumApprovalsAndEmployees")]
    public partial class Revision5AddendumApprovalsAndEmployees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Drop ('DEP-OPS') default constraint on CashAdvances.DepartmentCode
            migrationBuilder.Sql(
                """
                DECLARE @sql1 NVARCHAR(MAX) = N'';
                SELECT @sql1 += N'ALTER TABLE [dbo].[CashAdvances] DROP CONSTRAINT [' + dc.name + N']; '
                FROM sys.default_constraints dc
                JOIN sys.tables t ON dc.parent_object_id = t.object_id
                JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
                WHERE t.name = 'CashAdvances' AND c.name = 'DepartmentCode';
                IF @sql1 <> N'' EXEC sp_executesql @sql1;
                """);

            // 2. Drop ('DEP-OPS') default constraint on DirectDisbursements.department_code
            migrationBuilder.Sql(
                """
                DECLARE @sql2 NVARCHAR(MAX) = N'';
                SELECT @sql2 += N'ALTER TABLE [dbo].[DirectDisbursements] DROP CONSTRAINT [' + dc.name + N']; '
                FROM sys.default_constraints dc
                JOIN sys.tables t ON dc.parent_object_id = t.object_id
                JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
                WHERE t.name = 'DirectDisbursements' AND c.name = 'department_code';
                IF @sql2 <> N'' EXEC sp_executesql @sql2;
                """);

            // 3. Drop ('DEP-OPS') default constraint on Reimbursements.department_code
            migrationBuilder.Sql(
                """
                DECLARE @sql3 NVARCHAR(MAX) = N'';
                SELECT @sql3 += N'ALTER TABLE [dbo].[Reimbursements] DROP CONSTRAINT [' + dc.name + N']; '
                FROM sys.default_constraints dc
                JOIN sys.tables t ON dc.parent_object_id = t.object_id
                JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
                WHERE t.name = 'Reimbursements' AND c.name = 'department_code';
                IF @sql3 <> N'' EXEC sp_executesql @sql3;
                """);

            // 4. Update ROLE-004 Description to canonical "Assistant of Finance Manager"
            migrationBuilder.Sql(
                """
                IF OBJECT_ID('dbo.Roles', 'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH('dbo.Roles', 'role_id') IS NOT NULL
                        EXEC(N'UPDATE [dbo].[Roles] SET [Description] = ''Assistant of Finance Manager'' WHERE [Id] = ''ROLE-004'' OR [role_id] = 4;');
                    ELSE
                        EXEC(N'UPDATE [dbo].[Roles] SET [Description] = ''Assistant of Finance Manager'' WHERE [Id] = ''ROLE-004'';');
                END
                """);

            // 5. Update dbo.Users row 1 to "Finance Manager Test User"
            // 6. Deactivate CLIENT-001 (is_active = 0) without deleting employee or user records
            // Note: row 5 (EMP-005) is deliberately untouched and flagged for user review.
            migrationBuilder.Sql(
                """
                IF OBJECT_ID('dbo.Users', 'U') IS NOT NULL AND COL_LENGTH('dbo.Users', 'user_id') IS NOT NULL
                BEGIN
                    EXEC(N'UPDATE [dbo].[Users] SET [full_name] = ''Finance Manager Test User'' WHERE [user_id] = 1;');
                    EXEC(N'UPDATE [dbo].[Users] SET [is_active] = 0 WHERE [user_id] = 8 AND [employee_number] = ''CLIENT-001'';');
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 1. Re-add ('DEP-OPS') default constraint on CashAdvances.DepartmentCode
            migrationBuilder.Sql(
                """
                IF NOT EXISTS (
                    SELECT 1 FROM sys.default_constraints dc
                    JOIN sys.tables t ON dc.parent_object_id = t.object_id
                    JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
                    WHERE t.name = 'CashAdvances' AND c.name = 'DepartmentCode'
                )
                BEGIN
                    ALTER TABLE [dbo].[CashAdvances] ADD CONSTRAINT [DF_CashAdvances_DepartmentCode] DEFAULT ('DEP-OPS') FOR [DepartmentCode];
                END
                """);

            // 2. Re-add ('DEP-OPS') default constraint on DirectDisbursements.department_code
            migrationBuilder.Sql(
                """
                IF NOT EXISTS (
                    SELECT 1 FROM sys.default_constraints dc
                    JOIN sys.tables t ON dc.parent_object_id = t.object_id
                    JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
                    WHERE t.name = 'DirectDisbursements' AND c.name = 'department_code'
                )
                BEGIN
                    ALTER TABLE [dbo].[DirectDisbursements] ADD CONSTRAINT [DF_DirectDisbursements_DepartmentCode] DEFAULT ('DEP-OPS') FOR [department_code];
                END
                """);

            // 3. Re-add ('DEP-OPS') default constraint on Reimbursements.department_code
            migrationBuilder.Sql(
                """
                IF NOT EXISTS (
                    SELECT 1 FROM sys.default_constraints dc
                    JOIN sys.tables t ON dc.parent_object_id = t.object_id
                    JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
                    WHERE t.name = 'Reimbursements' AND c.name = 'department_code'
                )
                BEGIN
                    ALTER TABLE [dbo].[Reimbursements] ADD CONSTRAINT [DF_Reimbursements_DepartmentCode] DEFAULT ('DEP-OPS') FOR [department_code];
                END
                """);

            // 4. Revert ROLE-004 Description to "Assistant Financial Manager"
            migrationBuilder.Sql(
                """
                IF OBJECT_ID('dbo.Roles', 'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH('dbo.Roles', 'role_id') IS NOT NULL
                        EXEC(N'UPDATE [dbo].[Roles] SET [Description] = ''Assistant Financial Manager'' WHERE [Id] = ''ROLE-004'' OR [role_id] = 4;');
                    ELSE
                        EXEC(N'UPDATE [dbo].[Roles] SET [Description] = ''Assistant Financial Manager'' WHERE [Id] = ''ROLE-004'';');
                END
                """);

            // 5. Revert dbo.Users row 1 to "Financial Manager Test User"
            // 6. Reactivate CLIENT-001
            migrationBuilder.Sql(
                """
                IF OBJECT_ID('dbo.Users', 'U') IS NOT NULL AND COL_LENGTH('dbo.Users', 'user_id') IS NOT NULL
                BEGIN
                    EXEC(N'UPDATE [dbo].[Users] SET [full_name] = ''Financial Manager Test User'' WHERE [user_id] = 1;');
                    EXEC(N'UPDATE [dbo].[Users] SET [is_active] = 1 WHERE [user_id] = 8 AND [employee_number] = ''CLIENT-001'';');
                END
                """);
        }
    }
}
