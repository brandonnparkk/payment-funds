using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentFunds.Migrations
{
    /// <inheritdoc />
    public partial class AddRejectionAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RejectedAt",
                table: "PaymentRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectedBy",
                table: "PaymentRequests",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RejectedAt",
                table: "PaymentRequests");

            migrationBuilder.DropColumn(
                name: "RejectedBy",
                table: "PaymentRequests");
        }
    }
}
