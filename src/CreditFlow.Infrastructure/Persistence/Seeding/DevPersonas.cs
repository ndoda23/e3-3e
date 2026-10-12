using CreditFlow.Application.ReferenceData;
using CreditFlow.Domain.Users;

namespace CreditFlow.Infrastructure.Persistence.Seeding;

internal sealed record DevPersona(
    string UserName,
    string Email,
    string FullName,
    string RoleCode,
    string? BranchCode = null,
    Segment? Segment = null,
    AuthorityLevel? AuthorityLevel = null);

/// <summary>Demo users from the prototype personas (Development only): one per role, per level for LO/UW, per segment for HD.</summary>
internal static class DevPersonas
{
    public static IReadOnlyList<DevPersona> All { get; } =
    [
        new("anna.gelashvili", "anna.g@pcb.ge", "Anna Gelashvili", RoleCodes.LoanOfficer, BranchSeedCatalog.Batumi, AuthorityLevel: AuthorityLevel.Junior),
        new("salome.janelidze", "salome.j@pcb.ge", "Salome Janelidze", RoleCodes.LoanOfficer, BranchSeedCatalog.Saburtalo, AuthorityLevel: AuthorityLevel.Standard),
        new("davit.maisuradze", "davit.m@pcb.ge", "Davit Maisuradze", RoleCodes.LoanOfficer, BranchSeedCatalog.TbilisiHq, AuthorityLevel: AuthorityLevel.Senior),
        new("nika.beridze", "nika.b@pcb.ge", "Nika Beridze", RoleCodes.Underwriter, BranchSeedCatalog.Saburtalo, AuthorityLevel: AuthorityLevel.Junior),
        new("giorgi.kapanadze", "giorgi.k@pcb.ge", "Giorgi Kapanadze", RoleCodes.Underwriter, BranchSeedCatalog.TbilisiHq, AuthorityLevel: AuthorityLevel.Standard),
        new("tamta.tatishvili", "tamta.t@pcb.ge", "Tamta Tatishvili", RoleCodes.Underwriter, BranchSeedCatalog.TbilisiHq, AuthorityLevel: AuthorityLevel.Senior),
        new("mariam.lomidze", "mariam.l@pcb.ge", "Mariam Lomidze", RoleCodes.BranchManager, BranchSeedCatalog.TbilisiHq),
        new("lasha.chkheidze", "lasha.c@pcb.ge", "Lasha Chkheidze", RoleCodes.BusinessDevelopmentManager),
        new("keti.nadiradze", "keti.n@pcb.ge", "Keti Nadiradze", RoleCodes.BackOffice),
        new("nino.kvaratskhelia", "nino.k@pcb.ge", "Nino Kvaratskhelia", RoleCodes.AmlAnalyst),
        new("levan.abashidze", "levan.a@pcb.ge", "Levan Abashidze", RoleCodes.FinancialAnalysis),
        new("irakli.tsereteli", "irakli.t@pcb.ge", "Irakli Tsereteli", RoleCodes.Head, Segment: Segment.Business),
        new("sophio.gogichaishvili", "sophio.g@pcb.ge", "Sophio Gogichaishvili", RoleCodes.Head, Segment: Segment.Retail),
        new("zurab.kapanadze", "zurab.k@pcb.ge", "Zurab Kapanadze", RoleCodes.Director),
    ];
}
