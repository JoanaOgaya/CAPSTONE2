-- Migration: V20261026_05__Convert_Passwords_To_PBKDF2_Salted_Hashes.sql
-- Description: Ensures password_hash column in dbo.Users supports ASP.NET Core Identity PBKDF2 hashes.
--              Test users and development password hashes are separated into scripts/seeds/DevSeed_Users.sql (Item #6).
--              Production migrations create ZERO development or test accounts.

IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Users')
BEGIN
    -- Ensure password_hash length is NVARCHAR(500) for cryptographic salts
    IF EXISTS (
        SELECT * FROM INFORMATION_SCHEMA.COLUMNS 
        WHERE TABLE_NAME = 'Users' AND COLUMN_NAME = 'password_hash' AND CHARACTER_MAXIMUM_LENGTH < 500
    )
    BEGIN
        ALTER TABLE dbo.Users ALTER COLUMN password_hash NVARCHAR(500) NOT NULL;
    END;

    -- Security constraint: Enforce PBKDF2 standard prefix 'AQAAAA%' on active accounts in dbo.Users
    IF NOT EXISTS (SELECT * FROM sys.check_constraints WHERE name = 'CK_Users_PasswordHash_Format')
    BEGIN
        -- Add constraint only for rows with non-empty passwords
        ALTER TABLE dbo.Users ADD CONSTRAINT CK_Users_PasswordHash_Format 
        CHECK (password_hash IS NULL OR LEN(password_hash) = 0 OR password_hash LIKE 'AQAAAA%');
    END;
END;
GO

