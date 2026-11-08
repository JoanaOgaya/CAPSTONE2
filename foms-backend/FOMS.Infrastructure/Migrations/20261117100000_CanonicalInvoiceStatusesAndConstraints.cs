using FOMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FOMS.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261117100000_CanonicalInvoiceStatusesAndConstraints")]
public partial class CanonicalInvoiceStatusesAndConstraints : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Batch 1: Add new lifecycle, concurrency, and audit columns to Invoices
        migrationBuilder.Sql(
            """
            SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
            IF OBJECT_ID(N'[dbo].[Invoices]', N'U') IS NOT NULL
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Invoices') AND name = N'DocumentStatus')
                    ALTER TABLE dbo.Invoices ADD DocumentStatus NVARCHAR(50) NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Invoices') AND name = N'PaymentStatus')
                    ALTER TABLE dbo.Invoices ADD PaymentStatus NVARCHAR(50) NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Invoices') AND name = N'SentDate')
                    ALTER TABLE dbo.Invoices ADD SentDate DATETIME2 NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Invoices') AND name = N'ReceivedDate')
                    ALTER TABLE dbo.Invoices ADD ReceivedDate DATETIME2 NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Invoices') AND name = N'ReviewRejectionReason')
                    ALTER TABLE dbo.Invoices ADD ReviewRejectionReason NVARCHAR(MAX) NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Invoices') AND name = N'IsSampledForReview')
                    ALTER TABLE dbo.Invoices ADD IsSampledForReview BIT NOT NULL CONSTRAINT DF_Invoices_IsSampledForReview DEFAULT (0);

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Invoices') AND name = N'SampledBy')
                    ALTER TABLE dbo.Invoices ADD SampledBy NVARCHAR(100) NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Invoices') AND name = N'SampledAt')
                    ALTER TABLE dbo.Invoices ADD SampledAt DATETIME2 NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Invoices') AND name = N'DisputeReasonCode')
                    ALTER TABLE dbo.Invoices ADD DisputeReasonCode NVARCHAR(100) NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Invoices') AND name = N'DisputeRemarks')
                    ALTER TABLE dbo.Invoices ADD DisputeRemarks NVARCHAR(1000) NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Invoices') AND name = N'IsDisputed')
                    ALTER TABLE dbo.Invoices ADD IsDisputed BIT NOT NULL CONSTRAINT DF_Invoices_IsDisputed DEFAULT (0);

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Invoices') AND name = N'AccrualRevenueRecognized')
                    ALTER TABLE dbo.Invoices ADD AccrualRevenueRecognized BIT NOT NULL CONSTRAINT DF_Invoices_AccrualRevenueRecognized DEFAULT (0);

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Invoices') AND name = N'AccrualJournalEntryId')
                    ALTER TABLE dbo.Invoices ADD AccrualJournalEntryId NVARCHAR(100) NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Invoices') AND name = N'ApprovedBy')
                    ALTER TABLE dbo.Invoices ADD ApprovedBy NVARCHAR(100) NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Invoices') AND name = N'ApprovedAt')
                    ALTER TABLE dbo.Invoices ADD ApprovedAt DATETIME2 NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Invoices') AND name = N'VerifiedBy')
                    ALTER TABLE dbo.Invoices ADD VerifiedBy NVARCHAR(100) NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Invoices') AND name = N'VerifiedAt')
                    ALTER TABLE dbo.Invoices ADD VerifiedAt DATETIME2 NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Invoices') AND name = N'RowVersion')
                    ALTER TABLE dbo.Invoices ADD RowVersion ROWVERSION NOT NULL;
            END;
            """);

        // Batch 2: Timestamped backup table, data transformation, and verification gate
        migrationBuilder.Sql(
            """
            SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
            IF OBJECT_ID(N'[dbo].[Invoices]', N'U') IS NOT NULL
            BEGIN
                DECLARE @BackupName NVARCHAR(128) = N'Invoices_Backup_' + REPLACE(REPLACE(REPLACE(CONVERT(VARCHAR(19), GETUTCDATE(), 120), '-', ''), ':', ''), ' ', '_');
                DECLARE @CheckBackupSql NVARCHAR(MAX) = N'IF OBJECT_ID(N''dbo.'' + QUOTENAME(@BName), N''U'') IS NOT NULL RAISERROR(''Backup table already exists. Aborting to prevent overwrite.'', 16, 1);';
                EXEC sp_executesql @CheckBackupSql, N'@BName NVARCHAR(128)', @BName = @BackupName;

                DECLARE @CreateBackupSql NVARCHAR(MAX) = N'SELECT * INTO dbo.' + QUOTENAME(@BackupName) + N' FROM dbo.Invoices;';
                EXEC sp_executesql @CreateBackupSql;

                UPDATE dbo.Invoices
                SET DocumentStatus = N'Pending HA Verification'
                WHERE DocumentStatus IN (N'ForReview', N'For Review')
                   OR (DocumentStatus IS NULL AND PaymentValidationStatus = N'Pending Approval');

                UPDATE dbo.Invoices
                SET DocumentStatus = N'Returned to Creator'
                WHERE DocumentStatus = N'Draft'
                  AND PaymentValidationStatus IN (N'Needs Revision', N'Returned for Correction');

                UPDATE dbo.Invoices
                SET DocumentStatus = N'Draft'
                WHERE DocumentStatus IS NULL;

                UPDATE dbo.Invoices
                SET PaymentStatus = CASE
                    WHEN Balance <= 0 THEN N'Paid'
                    WHEN AmountPaid > 0 THEN N'Partially Paid'
                    ELSE N'Unpaid'
                END
                WHERE PaymentStatus IS NULL OR PaymentStatus NOT IN (N'Unpaid', N'Partially Paid', N'Paid');

                DECLARE @InvalidCount INT;
                SELECT @InvalidCount = COUNT(*) FROM dbo.Invoices
                WHERE DocumentStatus NOT IN (
                    N'Draft', N'Pending HA Verification', N'Pending FM Approval',
                    N'Returned to HA', N'Returned to Creator', N'Approved',
                    N'Sent', N'Received', N'Disputed', N'Void'
                ) OR PaymentStatus NOT IN (N'Unpaid', N'Partially Paid', N'Paid');

                IF @InvalidCount > 0
                BEGIN
                    RAISERROR('Verification failed: %d rows contain invalid status values.', 16, 1, @InvalidCount);
                END;
            END;
            """);

        // Batch 3: Constraints, indexes, and table alterations
        migrationBuilder.Sql(
            """
            SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
            IF OBJECT_ID(N'[dbo].[Invoices]', N'U') IS NOT NULL
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Invoices_DocumentStatus')
                BEGIN
                    ALTER TABLE dbo.Invoices ADD CONSTRAINT CK_Invoices_DocumentStatus
                    CHECK (DocumentStatus IN (
                        N'Draft', N'Pending HA Verification', N'Pending FM Approval',
                        N'Returned to HA', N'Returned to Creator', N'Approved',
                        N'Sent', N'Received', N'Disputed', N'Void'
                    ));
                END;

                IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Invoices_PaymentStatus')
                BEGIN
                    ALTER TABLE dbo.Invoices ADD CONSTRAINT CK_Invoices_PaymentStatus
                    CHECK (PaymentStatus IN (N'Unpaid', N'Partially Paid', N'Paid'));
                END;

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Invoices_AccrualJournalEntryId' AND object_id = OBJECT_ID(N'dbo.Invoices'))
                BEGIN
                    CREATE UNIQUE NONCLUSTERED INDEX IX_Invoices_AccrualJournalEntryId
                    ON dbo.Invoices(AccrualJournalEntryId)
                    WHERE AccrualJournalEntryId IS NOT NULL;
                END;

                IF OBJECT_ID(N'[dbo].[JournalEntries]', N'U') IS NOT NULL
                BEGIN
                    ALTER TABLE dbo.JournalEntries ALTER COLUMN EntryNumber NVARCHAR(100) NOT NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_JournalEntries_EntryNumber' AND object_id = OBJECT_ID(N'dbo.JournalEntries'))
                    BEGIN
                        CREATE UNIQUE NONCLUSTERED INDEX IX_JournalEntries_EntryNumber
                        ON dbo.JournalEntries(EntryNumber);
                    END;
                END;

                IF OBJECT_ID(N'[dbo].[ReceivableBalances]', N'U') IS NOT NULL
                BEGIN
                    ALTER TABLE dbo.ReceivableBalances ALTER COLUMN DueDate DATETIME2 NULL;
                END;
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
                -- Drop check constraints
                ALTER TABLE dbo.Invoices DROP CONSTRAINT IF EXISTS CK_Invoices_DocumentStatus;
                ALTER TABLE dbo.Invoices DROP CONSTRAINT IF EXISTS CK_Invoices_PaymentStatus;

                -- Allow NULLs so pre-migration NULL values restore cleanly
                ALTER TABLE dbo.Invoices DROP CONSTRAINT IF EXISTS DF_Invoices_DocumentStatus;
                ALTER TABLE dbo.Invoices DROP CONSTRAINT IF EXISTS DF_Invoices_PaymentStatus;
                ALTER TABLE dbo.Invoices ALTER COLUMN DocumentStatus NVARCHAR(50) NULL;
                ALTER TABLE dbo.Invoices ALTER COLUMN PaymentStatus NVARCHAR(50) NULL;

                -- Drop unique indexes
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Invoices_AccrualJournalEntryId' AND object_id = OBJECT_ID(N'dbo.Invoices'))
                BEGIN
                    DROP INDEX IX_Invoices_AccrualJournalEntryId ON dbo.Invoices;
                END;

                IF OBJECT_ID(N'[dbo].[JournalEntries]', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_JournalEntries_EntryNumber' AND object_id = OBJECT_ID(N'dbo.JournalEntries'))
                BEGIN
                    DROP INDEX IX_JournalEntries_EntryNumber ON dbo.JournalEntries;
                END;

                -- Drop RowVersion
                IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Invoices') AND name = N'RowVersion')
                BEGIN
                    ALTER TABLE dbo.Invoices DROP COLUMN RowVersion;
                END;

                -- Revert to most recent backup table if available
                DECLARE @LatestBackup NVARCHAR(128);
                SELECT TOP 1 @LatestBackup = name
                FROM sys.tables
                WHERE name LIKE 'Invoices_Backup_%'
                ORDER BY create_date DESC;

                IF @LatestBackup IS NOT NULL
                BEGIN
                    DECLARE @RestoreSql NVARCHAR(MAX) = N'
                        UPDATE i
                        SET i.DocumentStatus = b.DocumentStatus,
                            i.PaymentStatus = b.PaymentStatus,
                            i.PaymentValidationStatus = b.PaymentValidationStatus
                        FROM dbo.Invoices i
                        JOIN dbo.' + QUOTENAME(@LatestBackup) + N' b ON i.Id = b.Id;';
                    EXEC sp_executesql @RestoreSql;
                END;
            END;
            """);
    }
}
