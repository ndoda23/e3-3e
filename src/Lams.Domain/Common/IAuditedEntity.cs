using Lams.Domain.Users;

namespace Lams.Domain.Common;

/// <summary>
/// An entity that records who created and last changed it. The persistence layer stamps these values
/// from the current user and the clock when changes are saved, so domain methods stay free of them.
/// </summary>
public interface IAuditedEntity
{
    void MarkCreated(UserId? by, DateTimeOffset at);

    void MarkUpdated(UserId? by, DateTimeOffset at);
}
