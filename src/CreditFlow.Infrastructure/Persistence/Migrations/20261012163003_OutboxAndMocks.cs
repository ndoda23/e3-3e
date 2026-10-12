using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CreditFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OutboxAndMocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BureauReports",
                columns: table => new
                {
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RetrievedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReportJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BureauReports", x => new { x.ApplicationId, x.ClientId });
                    table.ForeignKey(
                        name: "FK_BureauReports_LoanApplications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "LoanApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Channel = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Recipient = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AvailableAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ClaimedUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedMessages",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Handler = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedMessages", x => new { x.MessageId, x.Handler });
                });

            migrationBuilder.CreateTable(
                name: "SanctionsListEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PersonalNumber = table.Column<string>(type: "char(11)", unicode: false, fixedLength: true, maxLength: 11, nullable: true),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ListName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SanctionsListEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_ApplicationId_CreatedAt",
                table: "Notifications",
                columns: new[] { "ApplicationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Due",
                table: "OutboxMessages",
                columns: new[] { "AvailableAt", "OccurredAt" },
                filter: "[ProcessedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SanctionsListEntries_FullName",
                table: "SanctionsListEntries",
                column: "FullName");

            migrationBuilder.CreateIndex(
                name: "IX_SanctionsListEntries_PersonalNumber",
                table: "SanctionsListEntries",
                column: "PersonalNumber");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BureauReports");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropTable(
                name: "ProcessedMessages");

            migrationBuilder.DropTable(
                name: "SanctionsListEntries");
        }
    }
}
