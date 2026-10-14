using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CreditFlow.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// CreditFlow roles: replaces the role's administrative flag with its kind, drops the LAMS-only segment,
    /// authority level and authority-limit data, and recreates the user directory view without them. The LAMS roles
    /// themselves are replaced by seed unit <c>0004_creditflow_roles</c>.
    /// </summary>
    public partial class CreditFlowRoles : Migration
    {
        private const string CreateUserDirectoryView = """
            CREATE VIEW [dbo].[vw_UserDirectory] AS
            SELECT
                s.[Id],
                s.[FullName],
                u.[UserName],
                u.[Email],
                s.[RoleId],
                r.[Code]              AS [RoleCode],
                r.[DisplayName]       AS [RoleName],
                CAST(CASE WHEN r.[Kind] = N'Administrative' THEN 1 ELSE 0 END AS bit) AS [RoleIsAdministrative],
                r.[RequireMfa]        AS [RoleRequiresMfa],
                s.[BranchId],
                b.[Name]              AS [BranchName],
                s.[IsActive],
                s.[IsAvailable],
                s.[Capacity],
                u.[LockoutEnd],
                u.[TwoFactorEnabled],
                u.[RequireMfa],
                u.[MustChangePassword],
                u.[TemporaryPasswordExpiresAt],
                u.[LastLoginAt],
                s.[CreatedAt],
                s.[UpdatedAt]
            FROM [dbo].[StaffMembers] s
            INNER JOIN [dbo].[AspNetUsers] u ON u.[Id] = s.[Id]
            INNER JOIN [dbo].[AspNetRoles] r ON r.[Id] = s.[RoleId]
            LEFT JOIN [dbo].[Branches] b ON b.[Id] = s.[BranchId];
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(InitialCreateObjects.DropUserDirectoryView);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "AspNetRoles",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Business");

            // Existing rows: the kind follows the old flag, and LAMS-only scope / dashboard values are mapped to valid
            // CreditFlow values until seed unit 0004 replaces the LAMS roles (an unknown enum value cannot be read).
            migrationBuilder.Sql("""
                UPDATE [dbo].[AspNetRoles] SET [Kind] = N'Administrative' WHERE [IsAdministrative] = 1;
                UPDATE [dbo].[AspNetRoles] SET [DataScope] = N'Organization'
                    WHERE [DataScope] IN (N'Own', N'Assigned', N'Segment');
                UPDATE [dbo].[AspNetRoles] SET [Dashboard] = CASE [Dashboard]
                        WHEN N'Officer' THEN N'Operator'
                        WHEN N'Underwriter' THEN N'Underwriting'
                        WHEN N'Administration' THEN N'Administration'
                        ELSE N'Audit' END;
                """);

            migrationBuilder.DropTable(name: "AuthorityLimits");
            migrationBuilder.DropColumn(name: "AuthorityLevel", table: "StaffMembers");
            migrationBuilder.DropColumn(name: "Segment", table: "StaffMembers");
            migrationBuilder.DropColumn(name: "AuthorityLevelRule", table: "AspNetRoles");
            migrationBuilder.DropColumn(name: "SegmentRule", table: "AspNetRoles");
            migrationBuilder.DropColumn(name: "IsAdministrative", table: "AspNetRoles");

            migrationBuilder.Sql(CreateUserDirectoryView);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(InitialCreateObjects.DropUserDirectoryView);

            migrationBuilder.AddColumn<bool>(
                name: "IsAdministrative",
                table: "AspNetRoles",
                type: "bit",
                nullable: false,
                defaultValue: false);
            migrationBuilder.Sql("UPDATE [dbo].[AspNetRoles] SET [IsAdministrative] = 1 WHERE [Kind] = N'Administrative';");
            migrationBuilder.DropColumn(name: "Kind", table: "AspNetRoles");

            migrationBuilder.AddColumn<string>(
                name: "SegmentRule",
                table: "AspNetRoles",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NotAllowed");
            migrationBuilder.AddColumn<string>(
                name: "AuthorityLevelRule",
                table: "AspNetRoles",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NotAllowed");
            migrationBuilder.AddColumn<string>(
                name: "Segment",
                table: "StaffMembers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "AuthorityLevel",
                table: "StaffMembers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AuthorityLimits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorityLevel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    LimitType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    MaxAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    MaxRateGapPp = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    MinCustomerRoaePct = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthorityLimits", x => x.Id);
                    table.CheckConstraint(
                        "CK_AuthorityLimits_CreditDecision",
                        "[LimitType] <> 'CreditDecision' OR ([Currency] IS NOT NULL AND [MaxAmount] > 0)");
                    table.ForeignKey(
                        name: "FK_AuthorityLimits_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateIndex(
                name: "IX_AuthorityLimits_RoleId_LimitType_AuthorityLevel_Currency",
                table: "AuthorityLimits",
                columns: new[] { "RoleId", "LimitType", "AuthorityLevel", "Currency" },
                unique: true);

            migrationBuilder.Sql(InitialCreateObjects.CreateUserDirectoryView);
        }
    }
}
