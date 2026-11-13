using FOMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FOMS.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261117130000_AddJournalEntryInvoiceIdUniquePostingIndex")]
public partial class AddJournalEntryInvoiceIdUniquePostingIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
            IF OBJECT_ID(N'[dbo].[JournalEntries]', N'U') IS NOT NULL
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.JournalEntries') AND name = N'InvoiceId')
                BEGIN
                    ALTER TABLE dbo.JournalEntries ADD InvoiceId NVARCHAR(450) NULL;
                END;
            END;
            """);

        migrationBuilder.Sql(
            """
            SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
            IF OBJECT_ID(N'[dbo].[JournalEntries]', N'U') IS NOT NULL
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_JournalEntries_InvoiceId_UniquePosting' AND object_id = OBJECT_ID(N'dbo.JournalEntries'))
                BEGIN
                    CREATE UNIQUE NONCLUSTERED INDEX IX_JournalEntries_InvoiceId_UniquePosting
                    ON dbo.JournalEntries(InvoiceId)
                    WHERE InvoiceId IS NOT NULL AND Posted = 1;
                END;
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
            IF OBJECT_ID(N'[dbo].[JournalEntries]', N'U') IS NOT NULL
            BEGIN
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_JournalEntries_InvoiceId_UniquePosting' AND object_id = OBJECT_ID(N'dbo.JournalEntries'))
                BEGIN
                    DROP INDEX IX_JournalEntries_InvoiceId_UniquePosting ON dbo.JournalEntries;
                END;
            END;
            """);

        migrationBuilder.Sql(
            """
            SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
            IF OBJECT_ID(N'[dbo].[JournalEntries]', N'U') IS NOT NULL
            BEGIN
                IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.JournalEntries') AND name = N'InvoiceId')
                BEGIN
                    ALTER TABLE dbo.JournalEntries DROP COLUMN InvoiceId;
                END;
            END;
            """);
    }
}
