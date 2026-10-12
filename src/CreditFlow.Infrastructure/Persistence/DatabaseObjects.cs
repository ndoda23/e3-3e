using Microsoft.EntityFrameworkCore.Migrations;

namespace CreditFlow.Infrastructure.Persistence;

/// <summary>
/// Database objects EF Core does not create from the model: the user directory view and the foreign keys between
/// strongly typed id columns and Identity's Guid keys. Used by migrations.
/// </summary>
internal static class DatabaseObjects
{
    public const string CreateUserDirectoryView = $"""
        CREATE VIEW [dbo].[{UserDirectoryRow.ViewName}] AS
        SELECT
            s.[Id],
            s.[FullName],
            u.[UserName],
            u.[Email],
            s.[RoleId],
            r.[Code]              AS [RoleCode],
            r.[DisplayName]       AS [RoleName],
            r.[IsAdministrative]  AS [RoleIsAdministrative],
            r.[RequireMfa]        AS [RoleRequiresMfa],
            s.[BranchId],
            b.[Name]              AS [BranchName],
            s.[Segment],
            s.[AuthorityLevel],
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

    public const string DropUserDirectoryView = $"DROP VIEW IF EXISTS [dbo].[{UserDirectoryRow.ViewName}];";

    /// <summary>Adds the foreign keys EF Core cannot model (different CLR key types).</summary>
    public static void AddManualForeignKeys(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddForeignKey(
            name: "FK_StaffMembers_AspNetUsers_Id",
            table: "StaffMembers",
            column: "Id",
            principalTable: "AspNetUsers",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_StaffMembers_AspNetRoles_RoleId",
            table: "StaffMembers",
            column: "RoleId",
            principalTable: "AspNetRoles",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_AuthorityLimits_AspNetRoles_RoleId",
            table: "AuthorityLimits",
            column: "RoleId",
            principalTable: "AspNetRoles",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    public static void DropManualForeignKeys(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("FK_StaffMembers_AspNetUsers_Id", "StaffMembers");
        migrationBuilder.DropForeignKey("FK_StaffMembers_AspNetRoles_RoleId", "StaffMembers");
        migrationBuilder.DropForeignKey("FK_AuthorityLimits_AspNetRoles_RoleId", "AuthorityLimits");
    }
}
