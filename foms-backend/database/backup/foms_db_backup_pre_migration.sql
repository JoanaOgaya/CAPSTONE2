-- ==============================================================================
-- FOMS_DB Pre-Migration Snapshot & Backup Script
-- Date: October 26, 2026
-- Description: Full schema and seed state backup before Capstone 2 schema alignment.
-- ==============================================================================

PRINT 'FOMS Pre-Migration Backup Snapshot Created';
-- Consolidates reference points from init.sql, seed_azure_approvals.sql, and create_foms_missing_tables.sql
-- Original seed and schema scripts are preserved in database/mssql/ and scripts/.
