using System;
using FOMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FOMS.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261117220000_RemapEmp005ToAccountant")]
    public partial class RemapEmp005ToAccountant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Product Owner Decision A (Phase 21/22):
            // EMP-005 (user_id 5) remapped to Accountant (role_id 3).
            // Do not rename the user.
            migrationBuilder.Sql(
                """
                IF OBJECT_ID('dbo.Users', 'U') IS NOT NULL AND COL_LENGTH('dbo.Users', 'user_id') IS NOT NULL
                BEGIN
                    EXEC(N'UPDATE [dbo].[Users] SET [role_id] = 3 WHERE [user_id] = 5 OR [employee_number] = ''EMP-005'';');
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF OBJECT_ID('dbo.Users', 'U') IS NOT NULL AND COL_LENGTH('dbo.Users', 'user_id') IS NOT NULL
                BEGIN
                    EXEC(N'UPDATE [dbo].[Users] SET [role_id] = 1 WHERE [user_id] = 5 OR [employee_number] = ''EMP-005'';');
                END
                """);
        }
    }
}
