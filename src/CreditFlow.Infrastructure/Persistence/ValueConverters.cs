using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Auditing;
using CreditFlow.Domain.Branches;
using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Products;
using CreditFlow.Domain.Roles;
using CreditFlow.Domain.Users;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CreditFlow.Infrastructure.Persistence;

internal sealed class UserIdConverter() : ValueConverter<UserId, Guid>(id => id.Value, value => new UserId(value));

internal sealed class RoleIdConverter() : ValueConverter<RoleId, Guid>(id => id.Value, value => new RoleId(value));

internal sealed class BranchIdConverter() : ValueConverter<BranchId, Guid>(id => id.Value, value => new BranchId(value));

internal sealed class AuditLogIdConverter()
    : ValueConverter<AuditLogId, long>(id => id.Value, value => new AuditLogId(value));

internal sealed class ClientIdConverter() : ValueConverter<ClientId, Guid>(id => id.Value, value => new ClientId(value));

internal sealed class ClientLineIdConverter()
    : ValueConverter<ClientLineId, Guid>(id => id.Value, value => new ClientLineId(value));

internal sealed class LoanProductIdConverter()
    : ValueConverter<LoanProductId, Guid>(id => id.Value, value => new LoanProductId(value));

internal sealed class LoanApplicationIdConverter()
    : ValueConverter<LoanApplicationId, Guid>(id => id.Value, value => new LoanApplicationId(value));

internal sealed class ApplicationLineIdConverter()
    : ValueConverter<ApplicationLineId, Guid>(id => id.Value, value => new ApplicationLineId(value));
