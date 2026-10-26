# Script: verify_clean_build.ps1
# Automates Item #3 & Clean Build Proof:
# Fresh database -> Base EF -> Schema alignment -> V01-V06 -> DevSeed -> Tests
# -> Full Rollback of V06..V01 with verification -> Re-apply V01..V06 + DevSeed with verification

$ErrorActionPreference = "Stop"
$Server = "localhost\SQLEXPRESS"
$Database = "FOMS_CLEAN_BUILD_PROOF"

Write-Host "=========================================================="
Write-Host "STEP 1: Re-creating empty database $Database"
Write-Host "=========================================================="
sqlcmd -S $Server -E -C -Q "IF DB_ID('$Database') IS NOT NULL BEGIN ALTER DATABASE $Database SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE $Database; END; CREATE DATABASE $Database;"

Write-Host "`n=========================================================="
Write-Host "STEP 2: Executing Base EF Core Migrations (ApplicationDbContext)"
Write-Host "=========================================================="
dotnet ef database update --project foms-backend/FOMS.Infrastructure --startup-project foms-backend/FOMS.Api --context ApplicationDbContext --connection "Server=$Server;Database=$Database;Trusted_Connection=True;TrustServerCertificate=True;"

Write-Host "`n=========================================================="
Write-Host "STEP 3: Executing Base Schema Alignment: create_foms_missing_tables.sql"
Write-Host "=========================================================="
sqlcmd -S $Server -d $Database -E -C -i "scripts/create_foms_missing_tables.sql"

Write-Host "`n=========================================================="
Write-Host "STEP 4: Applying Migrations V01 to V06 in sequential order"
Write-Host "=========================================================="
Write-Host "--> Running V01: V20261026_01__Add_Auth_Lockout_And_Failure_Reason.sql"
sqlcmd -S $Server -d $Database -E -C -i "scripts/migrations/V20261026_01__Add_Auth_Lockout_And_Failure_Reason.sql"

Write-Host "--> Running V02: V20261026_02__Add_Token_Revocation_And_User_Client_Mappings.sql"
sqlcmd -S $Server -d $Database -E -C -i "scripts/migrations/V20261026_02__Add_Token_Revocation_And_User_Client_Mappings.sql"

Write-Host "--> Running V03: V20261026_03__Add_Executive_Approvals_And_Audit_Triggers.sql"
sqlcmd -S $Server -d $Database -E -C -i "scripts/migrations/V20261026_03__Add_Executive_Approvals_And_Audit_Triggers.sql"

Write-Host "--> Running V04: V20261026_04__Add_Financial_Retention_And_FM_Delegates.sql"
sqlcmd -S $Server -d $Database -E -C -i "scripts/migrations/V20261026_04__Add_Financial_Retention_And_FM_Delegates.sql"

Write-Host "--> Running V05: V20261026_05__Convert_Passwords_To_PBKDF2_Salted_Hashes.sql"
sqlcmd -S $Server -d $Database -E -C -i "scripts/migrations/V20261026_05__Convert_Passwords_To_PBKDF2_Salted_Hashes.sql"

Write-Host "--> Running V06: V20261026_06__Correct_Retention_And_Executive_Approvals.sql"
sqlcmd -S $Server -d $Database -E -C -i "scripts/migrations/V20261026_06__Correct_Retention_And_Executive_Approvals.sql"

Write-Host "--> Running Dev Seed: DevSeed_Users.sql (Gated for development/test)"
sqlcmd -S $Server -d $Database -E -C -i "scripts/seeds/DevSeed_Users.sql"

Write-Host "`n=========================================================="
Write-Host "STEP 5: Running Test Suite against clean database"
Write-Host "=========================================================="
$env:FOMS_TEST_CONNECTION_STRING = "Server=$Server;Database=$Database;Trusted_Connection=True;TrustServerCertificate=True;"
dotnet test foms-backend/FOMS.Tests/FOMS.Tests.csproj --no-build

Write-Host "`n=========================================================="
Write-Host "STEP 6: Executing and Verifying Rollbacks (V06 down to V01)"
Write-Host "=========================================================="

Write-Host "--> Rolling back V06: V20261026_06_Rollback.sql"
sqlcmd -S $Server -d $Database -E -C -i "scripts/migrations/V20261026_06_Rollback.sql"
Write-Host "    [VERIFY V06 ROLLBACK]: Checking AccountsReceivable retention columns removed..."
sqlcmd -S $Server -d $Database -E -C -Q "IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.AccountsReceivable') AND name = 'client_retention_until') THROW 50001, 'V06 rollback failed: client_retention_until still exists on AccountsReceivable', 1; ELSE PRINT '    PASS: AccountsReceivable retention columns removed cleanly.';"

Write-Host "--> Rolling back V05: V20261026_05_Rollback.sql"
sqlcmd -S $Server -d $Database -E -C -i "scripts/migrations/V20261026_05_Rollback.sql"
Write-Host "    [VERIFY V05 ROLLBACK]: Checking CK_Users_PasswordHash_Format constraint removed..."
sqlcmd -S $Server -d $Database -E -C -Q "IF EXISTS (SELECT * FROM sys.check_constraints WHERE name = 'CK_Users_PasswordHash_Format') THROW 50002, 'V05 rollback failed: constraint CK_Users_PasswordHash_Format still exists', 1; ELSE PRINT '    PASS: CK_Users_PasswordHash_Format constraint removed cleanly.';"

Write-Host "--> Rolling back V04: V20261026_04_Rollback.sql"
sqlcmd -S $Server -d $Database -E -C -i "scripts/migrations/V20261026_04_Rollback.sql"
Write-Host "    [VERIFY V04 ROLLBACK]: Checking FinanceManagerDelegates table and Invoices retention removed..."
sqlcmd -S $Server -d $Database -E -C -Q "IF EXISTS (SELECT * FROM sys.tables WHERE name = 'FinanceManagerDelegates') OR EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Invoices') AND name = 'client_retention_until') THROW 50003, 'V04 rollback failed: FinanceManagerDelegates or retention columns still exist', 1; ELSE PRINT '    PASS: FinanceManagerDelegates and retention columns removed cleanly.';"

Write-Host "--> Rolling back V03: V20261026_03_Rollback.sql"
sqlcmd -S $Server -d $Database -E -C -i "scripts/migrations/V20261026_03_Rollback.sql"
Write-Host "    [VERIFY V03 ROLLBACK]: Checking ExecutiveApprovalRecords and audit triggers removed..."
sqlcmd -S $Server -d $Database -E -C -Q "IF EXISTS (SELECT * FROM sys.tables WHERE name = 'ExecutiveApprovalRecords') OR EXISTS (SELECT * FROM sys.triggers WHERE name = 'TR_AuditLogs_PreventUpdate') THROW 50004, 'V03 rollback failed: ExecutiveApprovalRecords or triggers still exist', 1; ELSE PRINT '    PASS: ExecutiveApprovalRecords and audit triggers removed cleanly.';"

Write-Host "--> Rolling back V02: V20261026_02_Rollback.sql"
sqlcmd -S $Server -d $Database -E -C -i "scripts/migrations/V20261026_02_Rollback.sql"
Write-Host "    [VERIFY V02 ROLLBACK]: Checking UserClientMappings and RevokedTokens removed..."
sqlcmd -S $Server -d $Database -E -C -Q "IF EXISTS (SELECT * FROM sys.tables WHERE name = 'UserClientMappings') OR EXISTS (SELECT * FROM sys.tables WHERE name = 'RevokedTokens') THROW 50005, 'V02 rollback failed: UserClientMappings or RevokedTokens still exist', 1; ELSE PRINT '    PASS: UserClientMappings and RevokedTokens removed cleanly.';"

Write-Host "--> Rolling back V01: V20261026_01_Rollback.sql"
sqlcmd -S $Server -d $Database -E -C -i "scripts/migrations/V20261026_01_Rollback.sql"
Write-Host "    [VERIFY V01 ROLLBACK]: Checking Users failed_login_count and Waybills failure_reason removed..."
sqlcmd -S $Server -d $Database -E -C -Q "IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Users') AND name = 'failed_login_count') OR EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Waybills') AND name = 'failure_reason') THROW 50006, 'V01 rollback failed: failed_login_count or failure_reason still exist', 1; ELSE PRINT '    PASS: V01 columns removed cleanly.';"

Write-Host "`n=========================================================="
Write-Host "STEP 7: Re-applying All Migrations (V01 to V06) and DevSeed"
Write-Host "=========================================================="
sqlcmd -S $Server -d $Database -E -C -i "scripts/migrations/V20261026_01__Add_Auth_Lockout_And_Failure_Reason.sql"
sqlcmd -S $Server -d $Database -E -C -i "scripts/migrations/V20261026_02__Add_Token_Revocation_And_User_Client_Mappings.sql"
sqlcmd -S $Server -d $Database -E -C -i "scripts/migrations/V20261026_03__Add_Executive_Approvals_And_Audit_Triggers.sql"
sqlcmd -S $Server -d $Database -E -C -i "scripts/migrations/V20261026_04__Add_Financial_Retention_And_FM_Delegates.sql"
sqlcmd -S $Server -d $Database -E -C -i "scripts/migrations/V20261026_05__Convert_Passwords_To_PBKDF2_Salted_Hashes.sql"
sqlcmd -S $Server -d $Database -E -C -i "scripts/migrations/V20261026_06__Correct_Retention_And_Executive_Approvals.sql"
sqlcmd -S $Server -d $Database -E -C -i "scripts/seeds/DevSeed_Users.sql"

Write-Host "--> [VERIFY RE-APPLICATION]: Confirming full schema restoration..."
sqlcmd -S $Server -d $Database -E -C -Q "
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Users') AND name = 'failed_login_count') THROW 50011, 'V01 failed re-apply', 1;
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'RevokedTokens') THROW 50012, 'V02 failed re-apply', 1;
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ExecutiveApprovalRecords') THROW 50013, 'V03 failed re-apply', 1;
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'FinanceManagerDelegates') THROW 50014, 'V04 failed re-apply', 1;
IF NOT EXISTS (SELECT * FROM sys.check_constraints WHERE name = 'CK_Users_PasswordHash_Format') THROW 50015, 'V05 failed re-apply', 1;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.AccountsReceivable') AND name = 'client_retention_until') THROW 50016, 'V06 failed re-apply', 1;
PRINT '    PASS: All 6 migrations re-applied and verified deterministic.';
"

Write-Host "`n=========================================================="
Write-Host "Clean build proof and full rollback verification completed successfully!"
Write-Host "=========================================================="
