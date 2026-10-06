namespace Lams.Domain.Common;

/// <summary>Creates new identifiers. Version 7 GUIDs are time-ordered, which keeps indexes compact.</summary>
internal static class IdGenerator
{
    public static Guid NewGuid() => Guid.CreateVersion7();
}
