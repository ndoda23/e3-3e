using CreditFlow.Application.Authorization;
using CreditFlow.Application.ReferenceData;
using CreditFlow.Application.Roles;
using CreditFlow.Domain.Branches;
using CreditFlow.Domain.Roles;
using CreditFlow.Domain.Users;

namespace CreditFlow.Application.UnitTests;

/// <summary>Role profiles and details built from the seed catalog, with stable ids per role code.</summary>
internal static class TestReferenceData
{
    private static readonly Dictionary<string, Domain.Roles.RoleId> RoleIds =
        RoleSeedCatalog.All.ToDictionary(r => r.Code, _ => Domain.Roles.RoleId.New());

    public static Domain.Roles.RoleId RoleId(string code) => RoleIds[code];

    public static RoleProfile Profile(string code)
    {
        var seed = RoleSeedCatalog.Get(code);
        return new RoleProfile
        {
            Id = RoleIds[code],
            Code = RoleCode.Create(seed.Code).Value,
            Kind = seed.Kind,
            DataScope = seed.DataScope,
            Dashboard = seed.Dashboard,
            BranchRule = seed.BranchRule,
        };
    }

    public static RoleDetails Details(string code, IReadOnlyCollection<string>? permissions = null)
    {
        var seed = RoleSeedCatalog.Get(code);
        return new RoleDetails
        {
            Profile = Profile(code),
            DisplayName = seed.DisplayName,
            Description = seed.Description,
            IsSystem = true,
            RequireMfa = false,
            UserCount = 0,
            Permissions = permissions ?? seed.Permissions,
        };
    }

    public static StaffMember Staff(
        string roleCode,
        BranchId? branchId = null,
        string fullName = "Test User") =>
        StaffMember.Create(UserId.New(), fullName, Profile(roleCode), branchId).Value;

    public static UserAccessProfile AccessProfile(StaffMember staff)
    {
        var roleCode = RoleIds.Single(r => r.Value == staff.RoleId).Key;
        var seed = RoleSeedCatalog.Get(roleCode);
        return UserAccessProfile.Create(staff, Profile(roleCode), seed.DisplayName, seed.Permissions);
    }
}
