namespace Lams.Domain.Roles;

/// <summary>Landing page a role is sent to after sign-in (FRD M1).</summary>
public enum DashboardKind
{
    Officer = 1,
    Underwriter = 2,
    Branch = 3,
    BusinessDevelopment = 4,
    BackOffice = 5,
    AmlScreening = 6,
    Fab = 7,
    Exceptions = 8,
    Administration = 9,
}
