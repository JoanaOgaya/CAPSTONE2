using FOMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FOMS.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261117150000_PaymentApprovalConcurrencyAndUniqueReceipt")]
    public partial class PaymentApprovalConcurrencyAndUniqueReceipt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Flow",
                table: "Payments",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "inflow");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Payments",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentId",
                table: "OfficialReceipts",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OfficialReceipts_PaymentId_Unique",
                table: "OfficialReceipts",
                column: "PaymentId",
                unique: true,
                filter: "[PaymentId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OfficialReceipts_PaymentId_Unique",
                table: "OfficialReceipts");

            migrationBuilder.DropColumn(
                name: "PaymentId",
                table: "OfficialReceipts");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "Flow",
                table: "Payments");
        }
    }
}
