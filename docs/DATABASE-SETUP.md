# FOMS Database Architecture & Setup Guide

This document specifies the database requirements, configuration options, migration execution pipeline, and operational runtime considerations for the Fastcat Operations Management System (FOMS).

---

## 1. SQL Server Requirements

- **Supported Engine:** Microsoft SQL Server 2019+ or Microsoft SQL Server 2022 (including Developer, Standard, Enterprise, or SQL Server Express editions).
- **Default Local Instance:** `localhost\SQLEXPRESS`
- **Authentication:** Windows Authentication (Integrated Security) or SQL Server Mixed Mode Authentication.
- **Collation:** `SQL_Latin1_General_CP1_CI_AS` (Default).
- **Required Features:** 
  - Standard T-SQL, Schemas, Triggers, Computed Columns.
  - ANSI NULLS and QUOTED_IDENTIFIER ON for filtered indices (`SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;`).

---

## 2. Test Connection String Configuration

The test suite connects to SQL Server dynamically using the `FOMS_TEST_CONNECTION_STRING` environment variable.

### Configuration Rules
1. **Environment Variable Override:** If `FOMS_TEST_CONNECTION_STRING` is set in the runtime environment, the integration test suite (`Phase2SecurityAndDatabaseTests`) uses it exclusively.
2. **Default Fallback:** If not specified, tests fall back to:
   ```
   Server=localhost\SQLEXPRESS;Database=FOMS_DB;Trusted_Connection=True;TrustServerCertificate=True;
   ```
3. **PowerShell Usage (e.g. for CI or Clean Test Runs):**
   ```powershell
   $env:FOMS_TEST_CONNECTION_STRING = "Server=localhost\SQLEXPRESS;Database=FOMS_CLEAN_BUILD_PROOF;Trusted_Connection=True;TrustServerCertificate=True;"
   dotnet test foms-backend/FOMS.Tests/FOMS.Tests.csproj
   ```

---

## 3. Database Creation & Sequential Migration Pipeline

To set up an operational or clean test instance from scratch, execute the following steps in order:

### Step 1: EF Core Baseline Schema
Applies base EF Core identity, audit, invoice, and payment infrastructure.
```powershell
dotnet ef database update `
  --project foms-backend/FOMS.Infrastructure `
  --startup-project foms-backend/FOMS.Api `
  --context ApplicationDbContext `
  --connection "Server=localhost\SQLEXPRESS;Database=FOMS_DB;Trusted_Connection=True;TrustServerCertificate=True;"
```

### Step 2: FOMS Domain Tables & Alignment
Creates domain tables (`CashAdvances`, `Liquidations`, `Waybills`, `BillingDocuments`, `ClientRates`, `Reports`, etc.) and aligns column definitions with legacy FOMS domain entities.
```powershell
sqlcmd -S localhost\SQLEXPRESS -d FOMS_DB -E -C -i "scripts/create_foms_missing_tables.sql"
```

### Step 3: Sequential Flyway-Style Migrations (V1 through V5)
Execute migrations in strict numerical and chronological sequence:

1. **V1 - Auth Lockout & Failure Reason Tracking (IAM-02)**
   ```powershell
   sqlcmd -S localhost\SQLEXPRESS -d FOMS_DB -E -C -i "scripts/migrations/V20261026_01__Add_Auth_Lockout_And_Failure_Reason.sql"
   ```
   - Adds `LockoutEnd`, `AccessFailedCount`, `IsActive` to `Users`.
   - Adds `failure_reason` to `Waybills`.

2. **V2 - Token Revocation & Multi-Tenant Client Mappings (IAM-05, IAM-06)**
   ```powershell
   sqlcmd -S localhost\SQLEXPRESS -d FOMS_DB -E -C -i "scripts/migrations/V20261026_02__Add_Token_Revocation_And_User_Client_Mappings.sql"
   ```
   - Creates `RevokedTokens` table with expiration index.
   - Creates `UserClientMappings` table for strict tenant isolation.

3. **V3 - Executive Approvals & Mutually Exclusive Audit Triggers (IAM-04, AUD-01, AUD-02)**
   ```powershell
   sqlcmd -S localhost\SQLEXPRESS -d FOMS_DB -E -C -i "scripts/migrations/V20261026_03__Add_Executive_Approvals_And_Audit_Triggers.sql"
   ```
   - Creates `ExecutiveApprovalRecords` table with constraint `CK_ExecApproval_Title` restricting approver title to `('CEO', 'President')` only.
   - Creates DDL audit triggers and immutable row-level change logging for financial tables.

4. **V4 - Financial Retention & Finance Manager Delegation (NFR-04, NFR-05)**
   ```powershell
   sqlcmd -S localhost\SQLEXPRESS -d FOMS_DB -E -C -i "scripts/migrations/V20261026_04__Add_Financial_Retention_And_FM_Delegates.sql"
   ```
   - Creates `FinanceManagerDelegations` table for dynamic FM delegate configuration.
   - Enforces separation of duties (delegates cannot self-approve requested/validated items).
   - Backfills financial retention dates (`client_retention_until` = created + 5 yrs; `internal_retention_until` = created + 10 yrs) computed from each record's own historical creation timestamp.

5. **V5 - PBKDF2 Salted Hashes & Production Seed Separation (IAM-02)**
   ```powershell
   sqlcmd -S localhost\SQLEXPRESS -d FOMS_DB -E -C -i "scripts/migrations/V20261026_05__Convert_Passwords_To_PBKDF2_Salted_Hashes.sql"
   ```
   - Enforces cryptographic check constraint `CK_Users_PasswordHash_Format` requiring `PBKDF2$...` format.
   - Removes all test/dev accounts and passwords from migration pipeline.
   - Rollback script available at `scripts/migrations/V20261026_05_Rollback.sql`.

### Step 4: Development & Test Seeds (Non-Production Only)
Populates development and test fixtures (`admin`, `staff-ha`, `staff-fm`, `staff-afm`, `staff-ceo`, `staff-pres`, `testclient`) with valid PBKDF2 hashes.
```powershell
sqlcmd -S localhost\SQLEXPRESS -d FOMS_DB -E -C -i "scripts/seeds/DevSeed_Users.sql"
```
*Note: In production environments, this step must NEVER be executed. Production user provisioning occurs exclusively through central administrative IAM onboarding.*

---

## 4. Runtime Architecture & Operational Considerations

### Session Inactivity Tracker (`SessionActivityTracker`)
- **Architecture:** The sliding inactivity tracking required by IAM-02 (15 minutes maximum inactive duration) is implemented in-memory using a thread-safe `ConcurrentDictionary<string, DateTime>` within `FOMS.Api.Services.SessionActivityTracker`.
- **Process Lifecycle Behavior:** Because the tracker is maintained in-process memory for maximum I/O performance on high-frequency API requests, active sliding inactivity state resets upon application server restart or container recycling. Clients with valid, non-expired JWT tokens will refresh their sliding window on their first post-restart request.
- **Token Invalidation:** Upon lockout, logout, or explicit session invalidation, the JTI is permanently recorded into the persistent SQL `RevokedTokens` database table, surviving process restarts.

### Rollback Procedures
Each migration has a matching rollback script in `scripts/migrations/`. For instance, to rollback V5:
```powershell
sqlcmd -S localhost\SQLEXPRESS -d FOMS_DB -E -C -i "scripts/migrations/V20261026_05_Rollback.sql"
```
Re-applying V5 afterward restores the PBKDF2 constraint seamlessly.
