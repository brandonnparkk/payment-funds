using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PaymentFunds.Migrations
{
    /// <inheritdoc />
    public partial class AddPayees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PayeeId",
                table: "PaymentRequests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "PaymentRequests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Payees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ProviderAccountReference = table.Column<string>(type: "text", nullable: true),
                    PayoutDestinationMask = table.Column<string>(type: "text", nullable: true),
                    TaxFormOnFile = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VerifiedBy = table.Column<string>(type: "text", nullable: true),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payees", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRequests_PayeeId",
                table: "PaymentRequests",
                column: "PayeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Payees_Email",
                table: "Payees",
                column: "Email",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentRequests_Payees_PayeeId",
                table: "PaymentRequests",
                column: "PayeeId",
                principalTable: "Payees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaymentRequests_Payees_PayeeId",
                table: "PaymentRequests");

            migrationBuilder.DropTable(
                name: "Payees");

            migrationBuilder.DropIndex(
                name: "IX_PaymentRequests_PayeeId",
                table: "PaymentRequests");

            migrationBuilder.DropColumn(
                name: "PayeeId",
                table: "PaymentRequests");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "PaymentRequests");
        }
    }
}
