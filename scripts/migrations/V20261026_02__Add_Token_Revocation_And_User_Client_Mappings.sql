-- Migration: V20261026_02__Add_Token_Revocation_And_User_Client_Mappings.sql
-- Description: Creates RevokedTokens and UserClientMappings tables with indexes and initial mappings.

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'RevokedTokens')
BEGIN
    CREATE TABLE RevokedTokens (
        revocation_id INT IDENTITY(1,1) PRIMARY KEY,
        token_hash NVARCHAR(128) NOT NULL,
        revoked_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        expires_at DATETIME2 NOT NULL,
        revocation_reason NVARCHAR(255) NULL
    );
    CREATE INDEX IX_RevokedTokens_Hash ON RevokedTokens(token_hash);
END;

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'UserClientMappings')
BEGIN
    CREATE TABLE UserClientMappings (
        mapping_id INT IDENTITY(1,1) PRIMARY KEY,
        user_id NVARCHAR(100) NOT NULL,
        client_id NVARCHAR(100) NOT NULL,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_UserClient_UserId ON UserClientMappings(user_id);
    CREATE INDEX IX_UserClient_ClientId ON UserClientMappings(client_id);
END;

-- Seed canonical client mappings
IF NOT EXISTS (SELECT 1 FROM UserClientMappings WHERE user_id = 'USER-CLI-001')
BEGIN
    INSERT INTO UserClientMappings (user_id, client_id) VALUES ('USER-CLI-001', 'CLI-001');
END;

IF NOT EXISTS (SELECT 1 FROM UserClientMappings WHERE user_id = 'CLIENT-001')
BEGIN
    INSERT INTO UserClientMappings (user_id, client_id) VALUES ('CLIENT-001', 'CLI-001');
END;
GO
