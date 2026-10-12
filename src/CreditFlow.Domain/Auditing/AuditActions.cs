namespace CreditFlow.Domain.Auditing;

/// <summary>Names of audited actions. Stable values: they are stored and filtered on.</summary>
public static class AuditActions
{
    // Data changes (written by the SaveChanges interceptor)
    public const string Created = "Created";
    public const string Updated = "Updated";
    public const string Deleted = "Deleted";

    // Security events
    public const string LoginSucceeded = "LoginSucceeded";
    public const string LoginFailed = "LoginFailed";
    public const string LoginFailedTemporaryPasswordExpired = "LoginFailed:TemporaryPasswordExpired";
    public const string LockedOut = "LockedOut";
    public const string Logout = "Logout";
    public const string SessionExpired = "SessionExpired";
    public const string PasswordChanged = "PasswordChanged";
    public const string PasswordReset = "PasswordReset";
    public const string MfaEnabled = "MfaEnabled";
    public const string MfaDisabled = "MfaDisabled";
    public const string MfaReset = "MfaReset";
    public const string MfaRequirementChanged = "MfaRequirementChanged";
    public const string RecoveryCodesGenerated = "RecoveryCodesGenerated";
    public const string UserUnlocked = "UserUnlocked";
    public const string UserActivated = "UserActivated";
    public const string UserDeactivated = "UserDeactivated";
    public const string ForcedSignOut = "ForcedSignOut";
    public const string RolePermissionsChanged = "RolePermissionsChanged";
}
