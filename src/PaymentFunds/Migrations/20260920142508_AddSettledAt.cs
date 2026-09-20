using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentFunds.Migrations
{
    /// <inheritdoc />
    public partial class AddSettledAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SettledAt",
                table: "PaymentRequests",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SettledAt",
                table: "PaymentRequests");
        }
    }
}
