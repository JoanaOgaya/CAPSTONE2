using System;
using FOMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FOMS.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261117180000_RefineCeoApprovalConfirmationsAndImmutability")]
    public partial class RefineCeoApprovalConfirmationsAndImmutability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                SET QUOTED_IDENTIFIER ON;
                SET ANSI_NULLS ON;

                -- 1. Drop confirmation columns and their FK constraints from dbo.CeoApprovalRecords
                IF OBJECT_ID(N'[dbo].[FK_CeoApprovalRecords_ConfirmedBy]', N'F') IS NOT NULL
                    ALTER TABLE [dbo].[CeoApprovalRecords] DROP CONSTRAINT [FK_CeoApprovalRecords_ConfirmedBy];

                IF OBJECT_ID(N'[dbo].[FK_CeoApprovalRecords_CorrectionConfirmedBy]', N'F') IS NOT NULL
                    ALTER TABLE [dbo].[CeoApprovalRecords] DROP CONSTRAINT [FK_CeoApprovalRecords_CorrectionConfirmedBy];

                IF COL_LENGTH(N'dbo.CeoApprovalRecords', N'ConfirmedBy') IS NOT NULL
                    ALTER TABLE [dbo].[CeoApprovalRecords] DROP COLUMN [ConfirmedBy];

                IF COL_LENGTH(N'dbo.CeoApprovalRecords', N'ConfirmedAt') IS NOT NULL
                    ALTER TABLE [dbo].[CeoApprovalRecords] DROP COLUMN [ConfirmedAt];

                IF COL_LENGTH(N'dbo.CeoApprovalRecords', N'CorrectionConfirmedBy') IS NOT NULL
                    ALTER TABLE [dbo].[CeoApprovalRecords] DROP COLUMN [CorrectionConfirmedBy];

                IF COL_LENGTH(N'dbo.CeoApprovalRecords', N'CorrectionConfirmedAt') IS NOT NULL
                    ALTER TABLE [dbo].[CeoApprovalRecords] DROP COLUMN [CorrectionConfirmedAt];

                -- 2. Create dbo.CeoApprovalConfirmations
                IF OBJECT_ID(N'[dbo].[CeoApprovalConfirmations]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[CeoApprovalConfirmations] (
                        [ConfirmationId] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_CeoApprovalConfirmations] PRIMARY KEY,
                        [RecordId] INT NOT NULL,
                        [Kind] NVARCHAR(20) NOT NULL,
                        [ConfirmedBy] NVARCHAR(450) NOT NULL,
                        [ConfirmedAt] DATETIME2 NOT NULL CONSTRAINT [DF_CeoApprovalConfirmations_ConfirmedAt] DEFAULT SYSUTCDATETIME(),

                        CONSTRAINT [FK_CeoApprovalConfirmations_Record] FOREIGN KEY ([RecordId]) REFERENCES [dbo].[CeoApprovalRecords]([RecordId]),
                        CONSTRAINT [FK_CeoApprovalConfirmations_ConfirmedBy] FOREIGN KEY ([ConfirmedBy]) REFERENCES [dbo].[Employees]([Id]),
                        CONSTRAINT [CK_CeoApprovalConfirmations_Kind] CHECK ([Kind] IN (N'Recording', N'Correction'))
                    );

                    CREATE NONCLUSTERED INDEX [IX_CeoApprovalConfirmations_Record_Kind]
                    ON [dbo].[CeoApprovalConfirmations] ([RecordId], [Kind]);
                END;

                -- Immutability trigger on dbo.CeoApprovalConfirmations
                IF OBJECT_ID(N'[dbo].[TR_CeoApprovalConfirmations_Immutable]', N'TR') IS NOT NULL
                    DROP TRIGGER [dbo].[TR_CeoApprovalConfirmations_Immutable];

                EXEC sp_executesql N'
                CREATE TRIGGER [dbo].[TR_CeoApprovalConfirmations_Immutable]
                ON [dbo].[CeoApprovalConfirmations]
                FOR UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    RAISERROR(N''dbo.CeoApprovalConfirmations is immutable. Updates and deletes are prohibited.'', 16, 1);
                    ROLLBACK TRANSACTION;
                END;';

                -- 3. CeoApprovalThresholdChangeLogs: Make SettingId NOT NULL & add FK + immutability trigger
                IF COL_LENGTH(N'dbo.CeoApprovalThresholdChangeLogs', N'SettingId') IS NOT NULL
                BEGIN
                    ALTER TABLE [dbo].[CeoApprovalThresholdChangeLogs] ALTER COLUMN [SettingId] INT NOT NULL;
                END;

                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CeoApprovalThresholdChangeLogs_Setting')
                BEGIN
                    ALTER TABLE [dbo].[CeoApprovalThresholdChangeLogs]
                    ADD CONSTRAINT [FK_CeoApprovalThresholdChangeLogs_Setting]
                    FOREIGN KEY ([SettingId]) REFERENCES [dbo].[CeoApprovalThresholdSettings]([SettingId]);
                END;

                IF OBJECT_ID(N'[dbo].[TR_CeoApprovalThresholdChangeLogs_Immutable]', N'TR') IS NOT NULL
                    DROP TRIGGER [dbo].[TR_CeoApprovalThresholdChangeLogs_Immutable];

                EXEC sp_executesql N'
                CREATE TRIGGER [dbo].[TR_CeoApprovalThresholdChangeLogs_Immutable]
                ON [dbo].[CeoApprovalThresholdChangeLogs]
                FOR UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    RAISERROR(N''dbo.CeoApprovalThresholdChangeLogs is immutable. Updates and deletes are prohibited.'', 16, 1);
                    ROLLBACK TRANSACTION;
                END;';

                -- 4. CeoApprovalThresholdSettings: CHECK ThresholdAmount > 0 when not NULL + Filtered unique index for one active row per SettingKey
                IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_CeoApprovalThresholdSettings_AmountPositive')
                BEGIN
                    ALTER TABLE [dbo].[CeoApprovalThresholdSettings]
                    ADD CONSTRAINT [CK_CeoApprovalThresholdSettings_AmountPositive]
                    CHECK ([ThresholdAmount] IS NULL OR [ThresholdAmount] > 0);
                END;

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CeoApprovalThresholdSettings_OneActiveKey')
                BEGIN
                    CREATE UNIQUE NONCLUSTERED INDEX [IX_CeoApprovalThresholdSettings_OneActiveKey]
                    ON [dbo].[CeoApprovalThresholdSettings] ([SettingKey])
                    WHERE [IsActive] = 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                SET QUOTED_IDENTIFIER ON;
                SET ANSI_NULLS ON;

                -- 1. Revert CeoApprovalThresholdSettings changes
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CeoApprovalThresholdSettings_OneActiveKey')
                    DROP INDEX [IX_CeoApprovalThresholdSettings_OneActiveKey] ON [dbo].[CeoApprovalThresholdSettings];

                IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_CeoApprovalThresholdSettings_AmountPositive')
                    ALTER TABLE [dbo].[CeoApprovalThresholdSettings] DROP CONSTRAINT [CK_CeoApprovalThresholdSettings_AmountPositive];

                -- 2. Revert CeoApprovalThresholdChangeLogs trigger and FK
                IF OBJECT_ID(N'[dbo].[TR_CeoApprovalThresholdChangeLogs_Immutable]', N'TR') IS NOT NULL
                    DROP TRIGGER [dbo].[TR_CeoApprovalThresholdChangeLogs_Immutable];

                IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CeoApprovalThresholdChangeLogs_Setting')
                    ALTER TABLE [dbo].[CeoApprovalThresholdChangeLogs] DROP CONSTRAINT [FK_CeoApprovalThresholdChangeLogs_Setting];

                ALTER TABLE [dbo].[CeoApprovalThresholdChangeLogs] ALTER COLUMN [SettingId] INT NULL;

                -- 3. Drop CeoApprovalConfirmations table and trigger
                IF OBJECT_ID(N'[dbo].[TR_CeoApprovalConfirmations_Immutable]', N'TR') IS NOT NULL
                    DROP TRIGGER [dbo].[TR_CeoApprovalConfirmations_Immutable];

                IF OBJECT_ID(N'[dbo].[CeoApprovalConfirmations]', N'U') IS NOT NULL
                    DROP TABLE [dbo].[CeoApprovalConfirmations];

                -- 4. Re-add dropped confirmation columns to dbo.CeoApprovalRecords
                IF COL_LENGTH(N'dbo.CeoApprovalRecords', N'ConfirmedBy') IS NULL
                    ALTER TABLE [dbo].[CeoApprovalRecords] ADD [ConfirmedBy] NVARCHAR(450) NULL;

                IF COL_LENGTH(N'dbo.CeoApprovalRecords', N'ConfirmedAt') IS NULL
                    ALTER TABLE [dbo].[CeoApprovalRecords] ADD [ConfirmedAt] DATETIME2 NULL;

                IF COL_LENGTH(N'dbo.CeoApprovalRecords', N'CorrectionConfirmedBy') IS NULL
                    ALTER TABLE [dbo].[CeoApprovalRecords] ADD [CorrectionConfirmedBy] NVARCHAR(450) NULL;

                IF COL_LENGTH(N'dbo.CeoApprovalRecords', N'CorrectionConfirmedAt') IS NULL
                    ALTER TABLE [dbo].[CeoApprovalRecords] ADD [CorrectionConfirmedAt] DATETIME2 NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CeoApprovalRecords_ConfirmedBy')
                    ALTER TABLE [dbo].[CeoApprovalRecords] ADD CONSTRAINT [FK_CeoApprovalRecords_ConfirmedBy] FOREIGN KEY ([ConfirmedBy]) REFERENCES [dbo].[Employees]([Id]);

                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CeoApprovalRecords_CorrectionConfirmedBy')
                    ALTER TABLE [dbo].[CeoApprovalRecords] ADD CONSTRAINT [FK_CeoApprovalRecords_CorrectionConfirmedBy] FOREIGN KEY ([CorrectionConfirmedBy]) REFERENCES [dbo].[Employees]([Id]);
                """);
        }
    }
}
