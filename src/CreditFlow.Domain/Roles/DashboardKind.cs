namespace CreditFlow.Domain.Roles;

/// <summary>The landing page a role is sent to after sign-in.</summary>
public enum DashboardKind
{
    Operator = 1,
    Underwriting = 2,
    Committee = 3,
    Compliance = 4,
    Audit = 5,
    Administration = 6,
}
