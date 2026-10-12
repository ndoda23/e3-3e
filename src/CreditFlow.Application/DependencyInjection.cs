using FluentValidation;
using CreditFlow.Application.Authorization;
using CreditFlow.Application.AuthorityLimits;
using CreditFlow.Application.Branches;
using CreditFlow.Application.Roles;
using CreditFlow.Application.Users;
using Microsoft.Extensions.DependencyInjection;

namespace CreditFlow.Application;

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
