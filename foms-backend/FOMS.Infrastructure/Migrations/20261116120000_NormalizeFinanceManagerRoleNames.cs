using FOMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FOMS.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261116120000_NormalizeFinanceManagerRoleNames")]
public partial class NormalizeFinanceManagerRoleNames : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'[dbo].[Roles]', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.Roles', N'Name') IS NOT NULL
                    UPDATE [dbo].[Roles]
                    SET [Name] = N'Finance Manager'
                    WHERE LOWER(LTRIM(RTRIM([Name]))) = N'financial manager';

                IF COL_LENGTH(N'dbo.Roles', N'role_name') IS NOT NULL
                    UPDATE [dbo].[Roles]
                    SET [role_name] = N'Finance Manager'
                    WHERE LOWER(LTRIM(RTRIM([role_name]))) = N'financial manager';

                IF COL_LENGTH(N'dbo.Roles', N'Name') IS NOT NULL
                    UPDATE [dbo].[Roles]
                    SET [Name] = N'Assistant of Finance Manager'
                    WHERE LOWER(LTRIM(RTRIM([Name]))) IN
                        (N'assistant financial manager', N'assistant finance manager', N'assistant of financial manager');

                IF COL_LENGTH(N'dbo.Roles', N'role_name') IS NOT NULL
                    UPDATE [dbo].[Roles]
                    SET [role_name] = N'Assistant of Finance Manager'
                    WHERE LOWER(LTRIM(RTRIM([role_name]))) IN
                        (N'assistant financial manager', N'assistant finance manager', N'assistant of financial manager');
            END;

            IF OBJECT_ID(N'[dbo].[Employees]', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.Employees', N'Role') IS NOT NULL
            BEGIN
                UPDATE [dbo].[Employees]
                SET [Role] = N'Finance Manager'
                WHERE LOWER(LTRIM(RTRIM([Role]))) = N'financial manager';

                UPDATE [dbo].[Employees]
                SET [Role] = N'Assistant of Finance Manager'
                WHERE LOWER(LTRIM(RTRIM([Role]))) IN
                    (N'assistant financial manager', N'assistant finance manager', N'assistant of financial manager');
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Do not restore legacy labels; role values are normalized in place.
    }
}
