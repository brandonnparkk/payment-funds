using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentFunds.Migrations
{
    /// <inheritdoc />
    public partial class RenameProviderReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "StripePaymentIntentId",
                table: "PaymentRequests",
                newName: "ProviderReference");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ProviderReference",
                table: "PaymentRequests",
                newName: "StripePaymentIntentId");
        }
    }
}
