using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lams.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSeedHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SeedHistory",
                columns: table => new
                {
                    SeedId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AppliedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeedHistory", x => x.SeedId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SeedHistory");
        }
    }
}
