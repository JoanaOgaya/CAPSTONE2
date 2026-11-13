using System;
using FOMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FOMS.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261117160000_AddPositionAndRoleNormalization")]
    public partial class AddPositionAndRoleNormalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;

                -- 1. Additive columns on dbo.Employees
                IF OBJECT_ID(N'[dbo].[Employees]', N'U') IS NOT NULL
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Employees]') AND name = N'Position')
                    BEGIN
                        ALTER TABLE [dbo].[Employees] ADD [Position] nvarchar(100) NULL;
                    END;

                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Employees]') AND name = N'MustChangePassword')
                    BEGIN
                        ALTER TABLE [dbo].[Employees] ADD [MustChangePassword] bit NOT NULL CONSTRAINT [DF_Employees_MustChangePassword] DEFAULT 0;
                    END;

                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Employees]') AND name = N'IsTemporaryPassword')
                    BEGIN
                        ALTER TABLE [dbo].[Employees] ADD [IsTemporaryPassword] bit NOT NULL CONSTRAINT [DF_Employees_IsTemporaryPassword] DEFAULT 0;
                    END;

                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Employees]') AND name = N'TemporaryPasswordExpiresAt')
                    BEGIN
                        ALTER TABLE [dbo].[Employees] ADD [TemporaryPasswordExpiresAt] datetime2 NULL;
                    END;
                END;

                -- 2. Normalize role names in dbo.Roles
                IF OBJECT_ID(N'[dbo].[Roles]', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'dbo.Roles', N'Name') IS NOT NULL
                    BEGIN
                        EXEC sp_executesql N'UPDATE [dbo].[Roles] SET [Name] = N''Finance Manager'' WHERE LOWER(LTRIM(RTRIM([Name]))) = N''financial manager''';
                        EXEC sp_executesql N'UPDATE [dbo].[Roles] SET [Name] = N''Assistant of Finance Manager'' WHERE LOWER(LTRIM(RTRIM([Name]))) IN (N''assistant financial manager'', N''assistant finance manager'', N''assistant of financial manager'')';
                    END;

                    IF COL_LENGTH(N'dbo.Roles', N'role_name') IS NOT NULL
                    BEGIN
                        EXEC sp_executesql N'UPDATE [dbo].[Roles] SET [role_name] = N''Finance Manager'' WHERE LOWER(LTRIM(RTRIM([role_name]))) = N''financial manager''';
                        EXEC sp_executesql N'UPDATE [dbo].[Roles] SET [role_name] = N''Assistant of Finance Manager'' WHERE LOWER(LTRIM(RTRIM([role_name]))) IN (N''assistant financial manager'', N''assistant finance manager'', N''assistant of financial manager'')';
                    END;
                END;

                -- 3. Normalize role names in dbo.Employees and populate Position for finance roles only
                IF OBJECT_ID(N'[dbo].[Employees]', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'dbo.Employees', N'Role') IS NOT NULL
                    BEGIN
                        EXEC sp_executesql N'UPDATE [dbo].[Employees] SET [Role] = N''Finance Manager'' WHERE LOWER(LTRIM(RTRIM([Role]))) = N''financial manager''';
                        EXEC sp_executesql N'UPDATE [dbo].[Employees] SET [Role] = N''Assistant of Finance Manager'' WHERE LOWER(LTRIM(RTRIM([Role]))) IN (N''assistant financial manager'', N''assistant finance manager'', N''assistant of financial manager'')';
                    END;

                    IF COL_LENGTH(N'dbo.Employees', N'Position') IS NOT NULL
                    BEGIN
                        EXEC sp_executesql N'UPDATE [dbo].[Employees] SET [Position] = CASE 
                            WHEN [Role] = N''Finance Manager'' THEN N''Finance Manager''
                            WHEN [Role] = N''Head Accountant'' THEN N''Head Accountant''
                            WHEN [Role] = N''Accountant'' THEN N''Accountant''
                            WHEN [Role] = N''Assistant of Finance Manager'' THEN N''Assistant of Finance Manager''
                            ELSE NULL
                        END';
                    END;
                END;

                -- 4. Normalize role_name in dbo.ApprovalThresholdSettings
                IF OBJECT_ID(N'[dbo].[ApprovalThresholdSettings]', N'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.ApprovalThresholdSettings', N'role_name') IS NOT NULL
                BEGIN
                    EXEC sp_executesql N'UPDATE [dbo].[ApprovalThresholdSettings] SET [role_name] = N''Assistant of Finance Manager'' WHERE LOWER(LTRIM(RTRIM([role_name]))) IN (N''assistant financial manager'', N''assistant finance manager'', N''assistant of financial manager'')';
                    EXEC sp_executesql N'UPDATE [dbo].[ApprovalThresholdSettings] SET [role_name] = N''Finance Manager'' WHERE LOWER(LTRIM(RTRIM([role_name]))) = N''financial manager''';
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;

                -- Revert role names in dbo.ApprovalThresholdSettings
                IF OBJECT_ID(N'[dbo].[ApprovalThresholdSettings]', N'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.ApprovalThresholdSettings', N'role_name') IS NOT NULL
                BEGIN
                    EXEC sp_executesql N'UPDATE [dbo].[ApprovalThresholdSettings] SET [role_name] = N''Assistant Financial Manager'' WHERE [role_name] = N''Assistant of Finance Manager''';
                    EXEC sp_executesql N'UPDATE [dbo].[ApprovalThresholdSettings] SET [role_name] = N''Financial Manager'' WHERE [role_name] = N''Finance Manager''';
                END;

                -- Revert role names in dbo.Employees
                IF OBJECT_ID(N'[dbo].[Employees]', N'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.Employees', N'Role') IS NOT NULL
                BEGIN
                    EXEC sp_executesql N'UPDATE [dbo].[Employees] SET [Role] = N''Financial Manager'' WHERE [Role] = N''Finance Manager''';
                    EXEC sp_executesql N'UPDATE [dbo].[Employees] SET [Role] = N''Assistant Financial Manager'' WHERE [Role] = N''Assistant of Finance Manager''';
                END;

                -- Revert role names in dbo.Roles
                IF OBJECT_ID(N'[dbo].[Roles]', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'dbo.Roles', N'Name') IS NOT NULL
                    BEGIN
                        EXEC sp_executesql N'UPDATE [dbo].[Roles] SET [Name] = N''Financial Manager'' WHERE [Name] = N''Finance Manager''';
                        EXEC sp_executesql N'UPDATE [dbo].[Roles] SET [Name] = N''Assistant Financial Manager'' WHERE [Name] = N''Assistant of Finance Manager''';
                    END;
                    IF COL_LENGTH(N'dbo.Roles', N'role_name') IS NOT NULL
                    BEGIN
                        EXEC sp_executesql N'UPDATE [dbo].[Roles] SET [role_name] = N''Financial Manager'' WHERE [role_name] = N''Finance Manager''';
                        EXEC sp_executesql N'UPDATE [dbo].[Roles] SET [role_name] = N''Assistant Financial Manager'' WHERE [role_name] = N''Assistant of Finance Manager''';
                    END;
                END;

                -- Drop additive columns from dbo.Employees
                IF OBJECT_ID(N'[dbo].[Employees]', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'dbo.Employees', N'Position') IS NOT NULL
                    BEGIN
                        ALTER TABLE [dbo].[Employees] DROP COLUMN [Position];
                    END;

                    IF COL_LENGTH(N'dbo.Employees', N'TemporaryPasswordExpiresAt') IS NOT NULL
                    BEGIN
                        ALTER TABLE [dbo].[Employees] DROP COLUMN [TemporaryPasswordExpiresAt];
                    END;

                    IF COL_LENGTH(N'dbo.Employees', N'IsTemporaryPassword') IS NOT NULL
                    BEGIN
                        DECLARE @dfIsTemp sysname;
                        SELECT @dfIsTemp = dc.name
                        FROM sys.default_constraints dc
                        JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
                        WHERE dc.parent_object_id = OBJECT_ID(N'[dbo].[Employees]') AND c.name = N'IsTemporaryPassword';
                        IF @dfIsTemp IS NOT NULL
                            EXEC(N'ALTER TABLE [dbo].[Employees] DROP CONSTRAINT [' + @dfIsTemp + '];');
                        ALTER TABLE [dbo].[Employees] DROP COLUMN [IsTemporaryPassword];
                    END;

                    IF COL_LENGTH(N'dbo.Employees', N'MustChangePassword') IS NOT NULL
                    BEGIN
                        DECLARE @dfMustChange sysname;
                        SELECT @dfMustChange = dc.name
                        FROM sys.default_constraints dc
                        JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
                        WHERE dc.parent_object_id = OBJECT_ID(N'[dbo].[Employees]') AND c.name = N'MustChangePassword';
                        IF @dfMustChange IS NOT NULL
                            EXEC(N'ALTER TABLE [dbo].[Employees] DROP CONSTRAINT [' + @dfMustChange + '];');
                        ALTER TABLE [dbo].[Employees] DROP COLUMN [MustChangePassword];
                    END;
                END;
                """);
        }
    }
}
