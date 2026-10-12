using CreditFlow.Application.Authorization;

namespace CreditFlow.Web.Models;

/// <param name="Profile">The signed-in user's access profile.</param>
public sealed record DashboardViewModel(UserAccessProfile Profile);
