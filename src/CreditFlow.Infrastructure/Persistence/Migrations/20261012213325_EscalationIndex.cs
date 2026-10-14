using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CreditFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EscalationIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_LoanApplications_Status_EscalatedAt_StatusChangedAt",
                table: "LoanApplications",
                columns: new[] { "Status", "EscalatedAt", "StatusChangedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LoanApplications_Status_EscalatedAt_StatusChangedAt",
                table: "LoanApplications");
        }
    }
}
