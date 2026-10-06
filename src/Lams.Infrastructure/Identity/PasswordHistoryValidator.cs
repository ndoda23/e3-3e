using Lams.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Lams.Infrastructure.Identity;

/// <summary>Rejects a new password equal to any of the user's last <see cref="Depth"/> passwords (including the current one).</summary>
public sealed class PasswordHistoryValidator(LamsDbContext db) : IPasswordValidator<ApplicationUser>
{
    public const int Depth = 5;

    /// <summary>Previous hashes kept in history; together with the current hash they cover <see cref="Depth"/> passwords.</summary>
    public const int StoredHistoryDepth = Depth - 1;

    public const string ErrorCode = "PasswordReused";

    public async Task<IdentityResult> ValidateAsync(
        UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(user);

        if (string.IsNullOrEmpty(password))
        {
            return IdentityResult.Success;
        }

        var hashes = new List<string>();
        if (user.PasswordHash is not null)
        {
            hashes.Add(user.PasswordHash);
        }

        hashes.AddRange(await db.PasswordHistory
            .Where(h => h.UserId == user.Id)
            .OrderByDescending(h => h.CreatedAt)
            .ThenByDescending(h => h.Id)
            .Take(StoredHistoryDepth)
            .Select(h => h.PasswordHash)
            .ToListAsync());

        var reused = hashes.Any(hash =>
            manager.PasswordHasher.VerifyHashedPassword(user, hash, password) != PasswordVerificationResult.Failed);

        return reused
            ? IdentityResult.Failed(new IdentityError
            {
                Code = ErrorCode,
                Description = $"You cannot reuse any of your last {Depth} passwords.",
            })
            : IdentityResult.Success;
    }
}
