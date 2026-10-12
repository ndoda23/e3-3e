using CreditFlow.Application.Auditing;
using CreditFlow.Application.Authorization;
using CreditFlow.Application.Branches;
using CreditFlow.Application.Common;
using CreditFlow.Application.Roles;
using CreditFlow.Application.Users;
using CreditFlow.Domain.Branches;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Roles;
using CreditFlow.Domain.Users;

namespace CreditFlow.Application.UnitTests;

internal sealed class FakeCurrentUser : ICurrentUser
{
    public UserId? UserId { get; set; }

    public string? UserName { get; set; }

    public string? IpAddress => "127.0.0.1";

    public string? UserAgent => "tests";

    public string? CorrelationId => "test";
}

/// <summary>Records saves and transaction outcomes. Writes are not actually rolled back.</summary>
internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int Saves { get; private set; }

    public int Commits { get; private set; }

    public int Rollbacks { get; private set; }

    public bool InTransaction { get; private set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        return Task.CompletedTask;
    }

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> work, CancellationToken cancellationToken)
        where TResult : Result
    {
        InTransaction = true;
        try
        {
            var result = await work(cancellationToken);
            if (result.IsSuccess)
            {
                Commits++;
            }
            else
            {
                Rollbacks++;
            }

            return result;
        }
        finally
        {
            InTransaction = false;
        }
    }
}

internal sealed class FakeAccessCacheInvalidator(FakeUnitOfWork unitOfWork) : IAccessCacheInvalidator
{
    public List<UserId> InvalidatedUsers { get; } = [];

    public List<RoleId> InvalidatedRoles { get; } = [];

    /// <summary>Set when an invalidation happened while a transaction was still open (must not happen).</summary>
    public bool InvalidatedInsideTransaction { get; private set; }

    public Task InvalidateUserAsync(UserId userId, CancellationToken cancellationToken)
    {
        InvalidatedInsideTransaction |= unitOfWork.InTransaction;
        InvalidatedUsers.Add(userId);
        return Task.CompletedTask;
    }

    public Task InvalidateRoleAsync(RoleId roleId, CancellationToken cancellationToken)
    {
        InvalidatedInsideTransaction |= unitOfWork.InTransaction;
        InvalidatedRoles.Add(roleId);
        return Task.CompletedTask;
    }
}

internal sealed class FakeAuditWriter : IAuditWriter
{
    public List<AuditEvent> Events { get; } = [];

    public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        Events.Add(auditEvent);
        return Task.CompletedTask;
    }
}

internal sealed class FakeIdentityService(FakeUnitOfWork unitOfWork) : IIdentityService
{
    public Dictionary<UserId, AccountInfo> Accounts { get; } = [];

    public List<UserId> SessionInvalidations { get; } = [];

    public List<(UserId Id, string Password, DateTimeOffset ExpiresAt)> TemporaryPasswords { get; } = [];

    public List<NewAccount> CreatedAccounts { get; } = [];

    public List<UserId> Unlocked { get; } = [];

    /// <summary>When set, account updates fail with this error.</summary>
    public Error? FailUpdatesWith { get; set; }

    /// <summary>Set when a write happened outside a transaction.</summary>
    public bool WroteOutsideTransaction { get; private set; }

    public void AddAccount(UserId id, string userName, string email) =>
        Accounts[id] = new AccountInfo(id, userName, email, false, null, false, false, false, null, null);

    public Task<Result> CreateAccountAsync(NewAccount account, CancellationToken cancellationToken)
    {
        WroteOutsideTransaction |= !unitOfWork.InTransaction;
        CreatedAccounts.Add(account);
        AddAccount(account.Id, account.UserName, account.Email);
        return Task.FromResult(Result.Success());
    }

    public Task<AccountInfo?> FindAccountAsync(UserId id, CancellationToken cancellationToken) =>
        Task.FromResult(Accounts.GetValueOrDefault(id));

    public Task<bool> IsUserNameTakenAsync(string userName, UserId? exceptUserId, CancellationToken cancellationToken) =>
        Task.FromResult(Accounts.Values.Any(a =>
            a.Id != exceptUserId && string.Equals(a.UserName, userName, StringComparison.OrdinalIgnoreCase)));

    public Task<bool> IsEmailTakenAsync(string email, UserId? exceptUserId, CancellationToken cancellationToken) =>
        Task.FromResult(Accounts.Values.Any(a =>
            a.Id != exceptUserId && string.Equals(a.Email, email, StringComparison.OrdinalIgnoreCase)));

    public Task<Result> UpdateAccountAsync(UserId id, string userName, string email, CancellationToken cancellationToken)
    {
        WroteOutsideTransaction |= !unitOfWork.InTransaction;
        if (FailUpdatesWith is { } error)
        {
            return Task.FromResult(Result.Failure(error));
        }

        Accounts[id] = Accounts[id] with { UserName = userName, Email = email };
        return Task.FromResult(Result.Success());
    }

    public Task<Result> SetTemporaryPasswordAsync(
        UserId id, string temporaryPassword, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        WroteOutsideTransaction |= !unitOfWork.InTransaction;
        TemporaryPasswords.Add((id, temporaryPassword, expiresAt));
        SessionInvalidations.Add(id);
        return Task.FromResult(Result.Success());
    }

    public Task<Result> UnlockAsync(UserId id, CancellationToken cancellationToken)
    {
        WroteOutsideTransaction |= !unitOfWork.InTransaction;
        Unlocked.Add(id);
        return Task.FromResult(Result.Success());
    }

    public Task<Result> SetRequireMfaAsync(UserId id, bool requireMfa, CancellationToken cancellationToken)
    {
        WroteOutsideTransaction |= !unitOfWork.InTransaction;
        Accounts[id] = Accounts[id] with { RequireMfa = requireMfa };
        return Task.FromResult(Result.Success());
    }

    public Task<Result> InvalidateSessionsAsync(UserId id, CancellationToken cancellationToken)
    {
        WroteOutsideTransaction |= !unitOfWork.InTransaction;
        SessionInvalidations.Add(id);
        return Task.FromResult(Result.Success());
    }
}

internal sealed class FakePasswordGenerator : ITemporaryPasswordGenerator
{
    private int _counter;

    public string Generate() => $"Temp-Password-{++_counter:D4}!";
}

internal sealed class FakeStaffRepository(FakeReferenceRepositories reference) : IStaffRepository
{
    public Dictionary<UserId, StaffMember> Members { get; } = [];

    public Task<StaffMember?> GetAsync(UserId id, CancellationToken cancellationToken) =>
        Task.FromResult(Members.GetValueOrDefault(id));

    public Task<int> CountActiveAdministratorsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Members.Values.Count(m => m.IsActive && reference.IsAdministrative(m.RoleId)));

    public void Add(StaffMember staffMember) => Members[staffMember.Id] = staffMember;
}

/// <summary>Roles (from the seed catalog) and branches held in memory.</summary>
internal sealed class FakeReferenceRepositories : IRoleRepository, IBranchRepository
{
    private readonly Dictionary<RoleId, RoleDetails> _roles =
        CreditFlow.Application.ReferenceData.RoleSeedCatalog.All
            .Select(r => TestReferenceData.Details(r.Code))
            .ToDictionary(r => r.Id);

    public Dictionary<BranchId, Branch> Branches { get; } = [];

    public bool IsAdministrative(RoleId roleId) => _roles[roleId].Profile.IsAdministrative;

    public Branch AddBranch(string code, string name, bool isActive = true)
    {
        var branch = Branch.Create(code, name, "Tbilisi").Value;
        if (!isActive)
        {
            branch.Deactivate();
        }

        Branches[branch.Id] = branch;
        return branch;
    }

    // ----- Roles -----
    public Task<RoleProfile?> GetProfileAsync(RoleId roleId, CancellationToken cancellationToken) =>
        Task.FromResult(_roles.GetValueOrDefault(roleId)?.Profile);

    public Task<RoleDetails?> GetDetailsAsync(RoleId roleId, CancellationToken cancellationToken) =>
        Task.FromResult(_roles.GetValueOrDefault(roleId));

    Task<IReadOnlyList<RoleDetails>> IRoleRepository.ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RoleDetails>>(_roles.Values.ToList());

    public Task SetPermissionsAsync(RoleId roleId, PermissionSet permissions, CancellationToken cancellationToken)
    {
        _roles[roleId] = _roles[roleId] with { Permissions = permissions.Permissions.Select(p => p.Value).ToList() };
        return Task.CompletedTask;
    }

    public Task SetRequireMfaAsync(RoleId roleId, bool requireMfa, CancellationToken cancellationToken)
    {
        _roles[roleId] = _roles[roleId] with { RequireMfa = requireMfa };
        return Task.CompletedTask;
    }

    // ----- Branches -----
    public Task<Branch?> GetAsync(BranchId id, CancellationToken cancellationToken) =>
        Task.FromResult(Branches.GetValueOrDefault(id));

    public Task<IReadOnlyList<BranchSummary>> ListAsync(bool includeInactive, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BranchSummary>>(Branches.Values
            .Where(b => includeInactive || b.IsActive)
            .Select(b => new BranchSummary(b.Id, b.Code, b.Name, b.City, b.IsActive, 0))
            .ToList());

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken) =>
        Task.FromResult(Branches.Values.Any(b => b.Code == code));

    public int AssignedStaffCount { get; set; }

    public Task<int> CountAssignedStaffAsync(BranchId id, CancellationToken cancellationToken) =>
        Task.FromResult(AssignedStaffCount);

    public void Add(Branch branch) => Branches[branch.Id] = branch;

    public void Remove(Branch branch) => Branches.Remove(branch.Id);
}
