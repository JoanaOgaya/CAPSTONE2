-- Development & Test Environment Seed: Test Users & Password Hashes
-- Gated by Configuration: EnableDevLogins = true / Environment = Development
-- CAUTION: DO NOT EXECUTE IN PRODUCTION (IAM-01, NFR-05, Item #6, Item #8)

-- 1. Environment Guard: Refuse execution if target database is not Dev/Test/Proof
IF DB_NAME() NOT LIKE '%DEV%' AND DB_NAME() NOT LIKE '%TEST%' AND DB_NAME() NOT LIKE '%PROOF%'
BEGIN
    RAISERROR('SECURITY VIOLATION: DevSeed_Users.sql cannot be executed against production databases. The target database name must contain DEV, TEST, or PROOF.', 16, 1);
    RETURN;
END;
GO

IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Users')
BEGIN
    -- 2. Populate synthetic non-production accounts with standardized test PBKDF2 hashes
    -- Note: No real individual names or production passwords exist in this seed script.

    -- Financial Manager Test User (EMP-001 / crystalyn)
    IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE username = 'crystalyn')
    BEGIN
        INSERT INTO dbo.Users (employee_number, full_name, username, password_hash, role_id, is_active)
        VALUES ('EMP-001', 'Financial Manager Test User', 'crystalyn', 'AQAAAAIAAYagAAAAELEV0A43ycXuJwAPbKNRSpiHStPCXeUqxOzoKxHLiRqK0u/PcOEeuUc9R+rYUwWc4g==', 1, 1);
    END
    ELSE
    BEGIN
        UPDATE dbo.Users 
        SET full_name = 'Financial Manager Test User',
            password_hash = 'AQAAAAIAAYagAAAAELEV0A43ycXuJwAPbKNRSpiHStPCXeUqxOzoKxHLiRqK0u/PcOEeuUc9R+rYUwWc4g=='
        WHERE username = 'crystalyn';
    END;

    -- Head Accountant Test User (EMP-002 / misty)
    IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE username = 'misty')
    BEGIN
        INSERT INTO dbo.Users (employee_number, full_name, username, password_hash, role_id, is_active)
        VALUES ('EMP-002', 'Head Accountant Test User', 'misty', 'AQAAAAIAAYagAAAAEKJK3fyQBjEO7NBTXn460gKLgPwC9uNPW+JNkMpWR4j7pO2fgXyI/80uhsqYR3TQXg==', 2, 1);
    END
    ELSE
    BEGIN
        UPDATE dbo.Users 
        SET full_name = 'Head Accountant Test User',
            password_hash = 'AQAAAAIAAYagAAAAEKJK3fyQBjEO7NBTXn460gKLgPwC9uNPW+JNkMpWR4j7pO2fgXyI/80uhsqYR3TQXg=='
        WHERE username = 'misty';
    END;

    -- Accountant Test User (EMP-003 / mariel)
    IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE username = 'mariel')
    BEGIN
        INSERT INTO dbo.Users (employee_number, full_name, username, password_hash, role_id, is_active)
        VALUES ('EMP-003', 'Accountant Test User', 'mariel', 'AQAAAAIAAYagAAAAEJhSI/pyMULSimK1Btlr0uZG2UbbDkDq2jYKFwtczgt+XFeeZGcZySlUdmCggh5JgQ==', 3, 1);
    END
    ELSE
    BEGIN
        UPDATE dbo.Users 
        SET full_name = 'Accountant Test User',
            password_hash = 'AQAAAAIAAYagAAAAEJhSI/pyMULSimK1Btlr0uZG2UbbDkDq2jYKFwtczgt+XFeeZGcZySlUdmCggh5JgQ=='
        WHERE username = 'mariel';
    END;

    -- Assistant FM Test User (EMP-004 / hannah)
    IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE username = 'hannah')
    BEGIN
        INSERT INTO dbo.Users (employee_number, full_name, username, password_hash, role_id, is_active)
        VALUES ('EMP-004', 'Assistant FM Test User', 'hannah', 'AQAAAAIAAYagAAAAEJCD5cJ2k2MT/Cf64u3+L0FFcIwQ4hagjouBWnX7stk8wTiXf2iJ65fCEUsJl1c3lg==', 4, 1);
    END
    ELSE
    BEGIN
        UPDATE dbo.Users 
        SET full_name = 'Assistant FM Test User',
            password_hash = 'AQAAAAIAAYagAAAAEJCD5cJ2k2MT/Cf64u3+L0FFcIwQ4hagjouBWnX7stk8wTiXf2iJ65fCEUsJl1c3lg=='
        WHERE username = 'hannah';
    END;

    -- Billing Staff Test User (EMP-005 / joana)
    IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE username = 'joana')
    BEGIN
        INSERT INTO dbo.Users (employee_number, full_name, username, password_hash, role_id, is_active)
        VALUES ('EMP-005', 'Billing Staff Test User', 'joana', 'AQAAAAIAAYagAAAAEHCMT7efz6D1gdds560GRzXqv0TBZHt9IRQSLZ5ww0JjEX+eBKbjSIb+cu45ixbUBQ==', 5, 1);
    END
    ELSE
    BEGIN
        UPDATE dbo.Users 
        SET full_name = 'Billing Staff Test User',
            password_hash = 'AQAAAAIAAYagAAAAEHCMT7efz6D1gdds560GRzXqv0TBZHt9IRQSLZ5ww0JjEX+eBKbjSIb+cu45ixbUBQ=='
        WHERE username = 'joana';
    END;

    -- Test Client User (CLIENT-001 / client.shopee)
    IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE username = 'client.shopee')
    BEGIN
        INSERT INTO dbo.Users (employee_number, full_name, username, password_hash, role_id, is_active)
        VALUES ('CLIENT-001', 'Test Client Company User', 'client.shopee', 'AQAAAAIAAYagAAAAEAvJMcbx5UXwEShvRcrwRDiogZN1BWVs8DJYbVrS9Z6IR1p+MlI/0fKytBLUJ1nz7Q==', 6, 1);
    END
    ELSE
    BEGIN
        UPDATE dbo.Users 
        SET full_name = 'Test Client Company User',
            password_hash = 'AQAAAAIAAYagAAAAEAvJMcbx5UXwEShvRcrwRDiogZN1BWVs8DJYbVrS9Z6IR1p+MlI/0fKytBLUJ1nz7Q=='
        WHERE username = 'client.shopee';
    END;
END;
GO
