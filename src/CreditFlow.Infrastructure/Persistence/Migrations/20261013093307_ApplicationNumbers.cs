using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CreditFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ApplicationNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "ApplicationNumbers");

            // Existing applications are numbered in the order they were created, then the sequence continues after them.
            migrationBuilder.AddColumn<long>(
                name: "Number",
                table: "LoanApplications",
                type: "bigint",
                nullable: true);

            migrationBuilder.Sql("""
                WITH numbered AS (
                    SELECT [Number], ROW_NUMBER() OVER (ORDER BY [CreatedAt], [Id]) AS [Ordinal] FROM [LoanApplications]
                )
                UPDATE numbered SET [Number] = [Ordinal];

                DECLARE @next bigint = (SELECT ISNULL(MAX([Number]), 0) + 1 FROM [LoanApplications]);
                DECLARE @restart nvarchar(200) = N'ALTER SEQUENCE [ApplicationNumbers] RESTART WITH ' + CAST(@next AS nvarchar(20));
                EXEC sp_executesql @restart;
                """);

            migrationBuilder.AlterColumn<long>(
                name: "Number",
                table: "LoanApplications",
                type: "bigint",
                nullable: false,
                defaultValueSql: "NEXT VALUE FOR [ApplicationNumbers]",
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoanApplications_Number",
                table: "LoanApplications",
                column: "Number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LoanApplications_Number",
                table: "LoanApplications");

            migrationBuilder.DropColumn(
                name: "Number",
                table: "LoanApplications");

            migrationBuilder.DropSequence(
                name: "ApplicationNumbers");
        }
    }
}
