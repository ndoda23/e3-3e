using CreditFlow.Application.ReferenceData;

namespace CreditFlow.Infrastructure.Persistence.Seeding;

internal sealed record DevPersona(string UserName, string Email, string FullName, string RoleCode, string? BranchCode = null);

/// <summary>
/// Demo users (Development only). Enough people for four-eyes: two Underwriters and two Senior Underwriters (one
/// person decides once per application), the three Credit Committee members, Compliance, Auditor, and Operators in
/// two branches (two in Vake to show branch-wide visibility, one in Batumi to show branch isolation).
/// </summary>
internal static class DevPersonas
{
    public static IReadOnlyList<DevPersona> All { get; } =
    [
        new("anna.gelashvili", "anna.g@pcb.ge", "Anna Gelashvili", RoleCodes.Operator, BranchSeedCatalog.Vake),
        new("salome.janelidze", "salome.j@pcb.ge", "Salome Janelidze", RoleCodes.Operator, BranchSeedCatalog.Vake),
        new("davit.maisuradze", "davit.m@pcb.ge", "Davit Maisuradze", RoleCodes.Operator, BranchSeedCatalog.Batumi),
        new("nika.beridze", "nika.b@pcb.ge", "Nika Beridze", RoleCodes.Underwriter),
        new("giorgi.kapanadze", "giorgi.k@pcb.ge", "Giorgi Kapanadze", RoleCodes.Underwriter),
        new("tamta.tatishvili", "tamta.t@pcb.ge", "Tamta Tatishvili", RoleCodes.SeniorUnderwriter),
        new("mariam.lomidze", "mariam.l@pcb.ge", "Mariam Lomidze", RoleCodes.SeniorUnderwriter),
        new("irakli.tsereteli", "irakli.t@pcb.ge", "Irakli Tsereteli", RoleCodes.CreditCommittee),
        new("sophio.gogichaishvili", "sophio.g@pcb.ge", "Sophio Gogichaishvili", RoleCodes.CreditCommittee),
        new("zurab.kapanadze", "zurab.k@pcb.ge", "Zurab Kapanadze", RoleCodes.CreditCommittee),
        new("nino.kvaratskhelia", "nino.k@pcb.ge", "Nino Kvaratskhelia", RoleCodes.Compliance),
        new("levan.abashidze", "levan.a@pcb.ge", "Levan Abashidze", RoleCodes.Auditor),
    ];
}
