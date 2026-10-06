namespace Lams.Infrastructure.Identity;

/// <summary>A previous password hash of a user, kept so recent passwords cannot be reused.</summary>
public sealed class UserPasswordHistory
{
    public long Id { get; set; }

    public Guid UserId { get; set; }

    public string PasswordHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
