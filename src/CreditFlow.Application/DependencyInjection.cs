using FluentValidation;
using CreditFlow.Application.Administration;
using CreditFlow.Application.Applications;
using CreditFlow.Application.Authorization;
using CreditFlow.Application.Branches;
using CreditFlow.Application.Clients;
using CreditFlow.Application.Reports;
using CreditFlow.Application.Disbursements;
using CreditFlow.Application.Common;
using CreditFlow.Application.Roles;
using CreditFlow.Application.Users;
using CreditFlow.Domain.Scoring;
using Microsoft.Extensions.DependencyInjection;

namespace CreditFlow.Application;

public static class DependencyInjection
{
    /// <summary>Registers application services, evaluators, validators and outbox handlers.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, ServiceLifetime.Singleton);

        services.AddSingleton<IDataScopeEvaluator, DataScopeEvaluator>();
        services.AddSingleton(ScoringEngine.Standard());

        services.AddScoped<StaffAccessChangeStep>();
        services.AddScoped<UserAdministrationService>();
        services.AddScoped<RoleAdministrationService>();
        services.AddScoped<BranchAdministrationService>();
        services.AddScoped<CurrentAccessProfile>();
        services.AddScoped<ClientService>();
        services.AddScoped<ApplicationService>();
        services.AddScoped<Dashboard.DashboardService>();
        services.AddScoped<DecisionService>();
        services.AddScoped<ContractService>();
        services.AddScoped<DisbursementService>();
        services.AddScoped<DisbursementProcessor>();
        services.AddScoped<LoanService>();
        services.AddScoped<ContractDocumentService>();
        services.AddScoped<AdminGuard>();
        services.AddScoped<ProductAdministrationService>();
        services.AddScoped<ScoringRuleAdministrationService>();
        services.AddScoped<ApprovalThresholdsAdministrationService>();
        services.AddScoped<FxRateAdministrationService>();
        services.AddScoped<HolidayAdministrationService>();
        services.AddScoped<ReportService>();
        services.AddScoped<ApplicationLifecycleJobs>();

        AddOutboxHandlers(services);
        return services;
    }

    /// <summary>Every <see cref="IOutboxHandler{TEvent}"/> in this assembly, so none can be forgotten.</summary>
    private static void AddOutboxHandlers(IServiceCollection services)
    {
        var handlers = typeof(DependencyInjection).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IOutboxHandler<>))
                .Select(i => (Service: i, Implementation: t)));

        foreach (var (service, implementation) in handlers)
        {
            services.AddScoped(service, implementation);
        }
    }
}
