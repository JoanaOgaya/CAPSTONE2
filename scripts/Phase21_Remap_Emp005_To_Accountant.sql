-- ============================================================================
-- Phase 21: Remap EMP-005 (user_id 5) to Accountant (role_id 3)
-- Per Product Owner Decision A and Revision 5 Addendum IAM-01
-- ============================================================================

IF EXISTS (SELECT 1 FROM dbo.Users WHERE user_id = 5 AND role_id = 1)
BEGIN
    UPDATE dbo.Users
    SET role_id = 3,
        full_name = 'Billing Staff (Accountant)',
        updated_at = SYSUTCDATETIME()
    WHERE user_id = 5;
    PRINT 'EMP-005 successfully remapped to role_id 3 (Accountant).';
END
ELSE
BEGIN
    PRINT 'EMP-005 already remapped or not found.';
END;
GO
