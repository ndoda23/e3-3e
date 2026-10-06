using FluentValidation;
using Lams.Application.Authorization;
using Lams.Application.AuthorityLimits;
using Lams.Application.Branches;
using Lams.Application.Roles;
using Lams.Application.Users;
using Microsoft.Extensions.DependencyInjection;

namespace Lams.Application;

public static class DependencyInjection
{
    /// <summary>Registers application services, evaluators and validators.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, ServiceLifetime.Singleton);

        services.AddSingleton<IDataScopeEvaluator, DataScopeEvaluator>();
        services.AddScoped<IAuthorityLimitEvaluator, AuthorityLimitEvaluator>();

        services.AddScoped<StaffAccessChangeStep>();
        services.AddScoped<UserAdministrationService>();
        services.AddScoped<RoleAdministrationService>();
        services.AddScoped<BranchAdministrationService>();
        services.AddScoped<AuthorityLimitAdministrationService>();

        return services;
    }
}
