using Lams.Domain.Auditing;
using Lams.Domain.AuthorityLimits;
using Lams.Domain.Branches;
using Lams.Domain.Roles;
using Lams.Domain.Users;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Lams.Infrastructure.Persistence;

internal sealed class UserIdConverter() : ValueConverter<UserId, Guid>(id => id.Value, value => new UserId(value));

internal sealed class RoleIdConverter() : ValueConverter<RoleId, Guid>(id => id.Value, value => new RoleId(value));

internal sealed class BranchIdConverter() : ValueConverter<BranchId, Guid>(id => id.Value, value => new BranchId(value));

internal sealed class AuthorityLimitIdConverter()
    : ValueConverter<AuthorityLimitId, Guid>(id => id.Value, value => new AuthorityLimitId(value));

internal sealed class AuditLogIdConverter()
    : ValueConverter<AuditLogId, long>(id => id.Value, value => new AuditLogId(value));
