using Microsoft.AspNetCore.Authorization;

namespace CreditFlow.Web.Authorization;

public static class Policies
{
    /// <summary>Policy-name prefix for "holds this permission".</summary>
    public const string PermissionPrefix = "perm:";

    /// <summary>Policy-name prefix for "holds at least one of these comma-separated permissions".</summary>
    public const string AnyPermissionPrefix = "perm-any:";

    /// <summary>Holds at least one <c>admin.*</c> permission (the whole Admin area).</summary>
    public const string Administration = "creditflow:administration";
}

/// <summary>Restricts a controller or action to users holding the permission. Code checks permissions, never roles.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class HasPermissionAttribute(string permission) : AuthorizeAttribute(Policies.PermissionPrefix + permission)
{
    public string Permission { get; } = permission;
}

/// <summary>Restricts a controller or action to users holding at least one of the permissions.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class HasAnyPermissionAttribute(params string[] permissions)
    : AuthorizeAttribute(Policies.AnyPermissionPrefix + string.Join(',', permissions))
{
    public IReadOnlyList<string> Permissions { get; } = permissions;
}
