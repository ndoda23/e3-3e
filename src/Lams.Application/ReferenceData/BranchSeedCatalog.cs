namespace Lams.Application.ReferenceData;

public sealed record BranchSeed(string Code, string Name, string City);

/// <summary>Branches seeded in every environment (idempotent by code).</summary>
public static class BranchSeedCatalog
{
    public const string TbilisiHq = "TBS-HQ";
    public const string Vake = "VAK";
    public const string Saburtalo = "SAB";
    public const string Kutaisi = "KUT";
    public const string Batumi = "BAT";
    public const string Rustavi = "RUS";

    public static IReadOnlyList<BranchSeed> All { get; } =
    [
        new(TbilisiHq, "Tbilisi HQ", "Tbilisi"),
        new(Vake, "Vake", "Tbilisi"),
        new(Saburtalo, "Saburtalo", "Tbilisi"),
        new(Kutaisi, "Kutaisi", "Kutaisi"),
        new(Batumi, "Batumi", "Batumi"),
        new(Rustavi, "Rustavi", "Rustavi"),
    ];
}
