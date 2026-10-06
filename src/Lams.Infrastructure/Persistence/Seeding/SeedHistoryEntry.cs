namespace Lams.Infrastructure.Persistence.Seeding;

/// <summary>A reference-data seed unit that has been applied (table <c>SeedHistory</c>).</summary>
public sealed class SeedHistoryEntry
{
    public const int SeedIdMaxLength = 100;

    public string SeedId { get; set; } = string.Empty;

    public DateTimeOffset AppliedAt { get; set; }
}
