using Lams.Application.Common;
using Lams.Application.ReferenceData;
using Lams.Application.Users;
using Lams.Domain.Branches;
using Lams.Domain.Users;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Lams.Application.UnitTests.Users;

/// <summary>A <see cref="UserAdministrationService"/> wired to in-memory fakes, acting as a System Administrator.</summary>
internal sealed class UserAdministrationFixture
{
    public UserAdministrationFixture()
    {
        UnitOfWork = new FakeUnitOfWork();
        Reference = new FakeReferenceRepositories();
        Staff = new FakeStaffRepository(Reference);
        Identity = new FakeIdentityService(UnitOfWork);
        Cache = new FakeAccessCacheInvalidator(UnitOfWork);
        Audit = new FakeAuditWriter();
        Clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero));

        Vake = Reference.AddBranch("VAK", "Vake").Id;
        Saburtalo = Reference.AddBranch("SAB", "Saburtalo").Id;
        ClosedBranch = Reference.AddBranch("OLD", "Closed branch", isActive: false).Id;

        Admin = AddMember(TestReferenceData.Staff(RoleCodes.SystemAdministrator, fullName: "Admin One"), "admin1");
        CurrentUser = new FakeCurrentUser { UserId = Admin.Id, UserName = "admin1" };

        Service = new UserAdministrationService(
            Staff,
            Reference,
            Reference,
            Identity,
            directory: null!,
            new FakePasswordGenerator(),
            new StaffAccessChangeStep(UnitOfWork, Identity, Cache),
            Cache,
            Audit,
            UnitOfWork,
            CurrentUser,
            Clock,
            Options.Create(new SecurityOptions()),
            new CreateUserCommandValidator(),
            new UpdateUserCommandValidator());
    }

    public FakeUnitOfWork UnitOfWork { get; }

    public FakeReferenceRepositories Reference { get; }

    public FakeStaffRepository Staff { get; }

    public FakeIdentityService Identity { get; }

    public FakeAccessCacheInvalidator Cache { get; }

    public FakeAuditWriter Audit { get; }

    public FakeTimeProvider Clock { get; }

    public FakeCurrentUser CurrentUser { get; }

    public BranchId Vake { get; }

    public BranchId Saburtalo { get; }

    public BranchId ClosedBranch { get; }

    public StaffMember Admin { get; }

    public UserAdministrationService Service { get; }

    public StaffMember AddMember(StaffMember member, string userName)
    {
        Staff.Add(member);
        Identity.AddAccount(member.Id, userName, $"{userName}@pcb.ge");
        return member;
    }

    public StaffMember AddSeniorUnderwriter() =>
        AddMember(
            TestReferenceData.Staff(RoleCodes.Underwriter, Vake, authorityLevel: AuthorityLevel.Senior, fullName: "Tamta Tatishvili"),
            "tamta.tatishvili");

    /// <summary>An update command that keeps every field of the member as it is.</summary>
    public UpdateUserCommand UnchangedUpdate(StaffMember member)
    {
        var account = Identity.Accounts[member.Id];
        return new UpdateUserCommand
        {
            Id = member.Id,
            UserName = account.UserName,
            Email = account.Email,
            FullName = member.FullName,
            RoleId = member.RoleId,
            BranchId = member.BranchId,
            Segment = member.Segment,
            AuthorityLevel = member.AuthorityLevel,
            Capacity = member.Capacity,
        };
    }
}
