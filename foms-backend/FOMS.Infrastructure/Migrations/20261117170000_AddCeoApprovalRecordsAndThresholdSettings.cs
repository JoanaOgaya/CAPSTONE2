using System;
using FOMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FOMS.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261117170000_AddCeoApprovalRecordsAndThresholdSettings")]
    public partial class AddCeoApprovalRecordsAndThresholdSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                SET QUOTED_IDENTIFIER ON;
                SET ANSI_NULLS ON;

                -- 1. Create dbo.CeoApprovalThresholdSettings
                IF OBJECT_ID(N'[dbo].[CeoApprovalThresholdSettings]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[CeoApprovalThresholdSettings] (
                        [SettingId] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_CeoApprovalThresholdSettings] PRIMARY KEY,
                        [SettingKey] NVARCHAR(100) NOT NULL,
                        [ThresholdAmount] DECIMAL(18,2) NULL, -- NULL = unset (no default seed)
                        [Currency] NVARCHAR(10) NOT NULL CONSTRAINT [DF_CeoApprovalThresholdSettings_Currency] DEFAULT N'PHP',
                        [EffectiveFrom] DATETIME2 NOT NULL CONSTRAINT [DF_CeoApprovalThresholdSettings_EffectiveFrom] DEFAULT SYSUTCDATETIME(),
                        [EffectiveTo] DATETIME2 NULL,
                        [IsActive] BIT NOT NULL CONSTRAINT [DF_CeoApprovalThresholdSettings_IsActive] DEFAULT 1,
                        [UpdatedBy] NVARCHAR(450) NOT NULL,
                        [UpdatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_CeoApprovalThresholdSettings_UpdatedAt] DEFAULT SYSUTCDATETIME(),
                        CONSTRAINT [FK_CeoApprovalThresholdSettings_Employees] FOREIGN KEY ([UpdatedBy]) REFERENCES [dbo].[Employees]([Id])
                    );

                    CREATE NONCLUSTERED INDEX [IX_CeoApprovalThresholdSettings_Key_Dates]
                    ON [dbo].[CeoApprovalThresholdSettings] ([SettingKey], [EffectiveFrom], [EffectiveTo], [IsActive]);
                END;

                -- 2. Create dbo.CeoApprovalThresholdChangeLogs
                IF OBJECT_ID(N'[dbo].[CeoApprovalThresholdChangeLogs]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[CeoApprovalThresholdChangeLogs] (
                        [LogId] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_CeoApprovalThresholdChangeLogs] PRIMARY KEY,
                        [SettingId] INT NULL,
                        [OldAmount] DECIMAL(18,2) NULL,
                        [NewAmount] DECIMAL(18,2) NULL,
                        [ChangedBy] NVARCHAR(450) NOT NULL,
                        [ChangedAt] DATETIME2 NOT NULL CONSTRAINT [DF_CeoApprovalThresholdChangeLogs_ChangedAt] DEFAULT SYSUTCDATETIME(),
                        [Reason] NVARCHAR(500) NOT NULL,
                        CONSTRAINT [FK_CeoApprovalThresholdChangeLogs_Employees] FOREIGN KEY ([ChangedBy]) REFERENCES [dbo].[Employees]([Id])
                    );
                END;

                -- 3. Create dbo.CeoApprovalRecords
                IF OBJECT_ID(N'[dbo].[CeoApprovalRecords]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[CeoApprovalRecords] (
                        [RecordId] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_CeoApprovalRecords] PRIMARY KEY,
                        [ItemType] NVARCHAR(50) NOT NULL,
                        [ItemId] NVARCHAR(100) NOT NULL,
                        [Status] NVARCHAR(20) NOT NULL,
                        [ApproverLabel] NVARCHAR(50) NOT NULL CONSTRAINT [DF_CeoApprovalRecords_ApproverLabel] DEFAULT N'CEO/President (in person)',
                        [EnteredBy] NVARCHAR(450) NOT NULL,
                        [EnteredAt] DATETIME2 NOT NULL CONSTRAINT [DF_CeoApprovalRecords_EnteredAt] DEFAULT SYSUTCDATETIME(),
                        [ApprovalDate] DATE NOT NULL,
                        [Notes] NVARCHAR(1000) NULL,
                        [RejectionReason] NVARCHAR(1000) NULL,
                        [AttachmentPath] NVARCHAR(500) NULL,
                        [AttachmentFileName] NVARCHAR(255) NULL,
                        [HasAttachment] BIT NOT NULL CONSTRAINT [DF_CeoApprovalRecords_HasAttachment] DEFAULT 0,
                        [ConfirmedBy] NVARCHAR(450) NULL,
                        [ConfirmedAt] DATETIME2 NULL,
                        [IsCorrection] BIT NOT NULL CONSTRAINT [DF_CeoApprovalRecords_IsCorrection] DEFAULT 0,
                        [OriginalRecordId] INT NULL,
                        [CorrectionReason] NVARCHAR(1000) NULL,
                        [CorrectionConfirmedBy] NVARCHAR(450) NULL,
                        [CorrectionConfirmedAt] DATETIME2 NULL,
                        [IsActive] BIT NOT NULL CONSTRAINT [DF_CeoApprovalRecords_IsActive] DEFAULT 1,

                        CONSTRAINT [FK_CeoApprovalRecords_EnteredBy] FOREIGN KEY ([EnteredBy]) REFERENCES [dbo].[Employees]([Id]),
                        CONSTRAINT [FK_CeoApprovalRecords_ConfirmedBy] FOREIGN KEY ([ConfirmedBy]) REFERENCES [dbo].[Employees]([Id]),
                        CONSTRAINT [FK_CeoApprovalRecords_CorrectionConfirmedBy] FOREIGN KEY ([CorrectionConfirmedBy]) REFERENCES [dbo].[Employees]([Id]),
                        CONSTRAINT [FK_CeoApprovalRecords_OriginalRecord] FOREIGN KEY ([OriginalRecordId]) REFERENCES [dbo].[CeoApprovalRecords]([RecordId]),

                        -- Check constraints
                        CONSTRAINT [CK_CeoApprovalRecords_ItemType] CHECK ([ItemType] IN (
                            N'AnnualBudget', N'BudgetTransfer', N'BudgetCarryOver', N'BudgetOverride',
                            N'Reimbursement', N'CashOutflow', N'InvoiceDiscount', N'AnnualFinancialStatement'
                        )),
                        CONSTRAINT [CK_CeoApprovalRecords_Status] CHECK ([Status] IN (N'Approved', N'Rejected')),
                        CONSTRAINT [CK_CeoApprovalRecords_RejectionReason] CHECK ([Status] != N'Rejected' OR [RejectionReason] IS NOT NULL),
                        CONSTRAINT [CK_CeoApprovalRecords_HasAttachment] CHECK ([HasAttachment] = 0 OR [ItemType] IN (N'AnnualBudget', N'CashOutflow', N'AnnualFinancialStatement')),
                        CONSTRAINT [CK_CeoApprovalRecords_ApprovalDate] CHECK ([ApprovalDate] <= CAST(DATEADD(hour, 8, SYSUTCDATETIME()) AS DATE))
                    );

                    -- Unique index on initial records (non-corrections)
                    CREATE UNIQUE NONCLUSTERED INDEX [IX_CeoApprovalRecords_UniqueItem_NonCorrection]
                    ON [dbo].[CeoApprovalRecords] ([ItemType], [ItemId])
                    WHERE [IsCorrection] = 0;

                    -- Query performance index for gate evaluation (ItemType + ItemId + EnteredAt desc)
                    CREATE NONCLUSTERED INDEX [IX_CeoApprovalRecords_Item_Lookup]
                    ON [dbo].[CeoApprovalRecords] ([ItemType], [ItemId], [EnteredAt] DESC);
                END;

                -- 4. Database Trigger for Immutability (no UPDATE or DELETE)
                IF OBJECT_ID(N'[dbo].[TR_CeoApprovalRecords_Immutable]', N'TR') IS NOT NULL
                BEGIN
                    DROP TRIGGER [dbo].[TR_CeoApprovalRecords_Immutable];
                END;
                EXEC sp_executesql N'
                CREATE TRIGGER [dbo].[TR_CeoApprovalRecords_Immutable]
                ON [dbo].[CeoApprovalRecords]
                FOR UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    RAISERROR(N''dbo.CeoApprovalRecords is immutable. Updates and deletes are prohibited. Corrections must be inserted as new records.'', 16, 1);
                    ROLLBACK TRANSACTION;
                END;';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                SET QUOTED_IDENTIFIER ON;
                SET ANSI_NULLS ON;

                -- Drop trigger
                IF OBJECT_ID(N'[dbo].[TR_CeoApprovalRecords_Immutable]', N'TR') IS NOT NULL
                    DROP TRIGGER [dbo].[TR_CeoApprovalRecords_Immutable];

                -- Drop tables
                IF OBJECT_ID(N'[dbo].[CeoApprovalRecords]', N'U') IS NOT NULL
                    DROP TABLE [dbo].[CeoApprovalRecords];

                IF OBJECT_ID(N'[dbo].[CeoApprovalThresholdChangeLogs]', N'U') IS NOT NULL
                    DROP TABLE [dbo].[CeoApprovalThresholdChangeLogs];

                IF OBJECT_ID(N'[dbo].[CeoApprovalThresholdSettings]', N'U') IS NOT NULL
                    DROP TABLE [dbo].[CeoApprovalThresholdSettings];
                """);
        }
    }
}
