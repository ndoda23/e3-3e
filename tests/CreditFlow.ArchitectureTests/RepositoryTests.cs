using System.Reflection;
using CreditFlow.Application;
using CreditFlow.Domain;
using CreditFlow.Infrastructure;
using NetArchTest.Rules;

namespace CreditFlow.ArchitectureTests;

/// <summary>
/// CQRS-lite: the write side has one repository per aggregate root, declared in the Domain next to the aggregate and
/// implemented in Infrastructure; the read side (lists, inboxes, reports) is a set of query services declared in
/// Application. EF Core stays inside Infrastructure.
/// </summary>
public sealed class RepositoryTests
{
    private static readonly Assembly Domain = typeof(DomainAssembly).Assembly;
    private static readonly Assembly Application = typeof(ApplicationAssembly).Assembly;
    private static readonly Assembly Infrastructure = typeof(InfrastructureAssembly).Assembly;
    private static readonly Assembly Web = typeof(Program).Assembly;

    private static readonly Assembly[] AllLayers = [Domain, Application, Infrastructure, Web];

    [Fact]
    public void Repository_interfaces_are_declared_only_in_the_domain()
    {
        var repositories = AllLayers.SelectMany(a => a.GetTypes()).Where(IsRepositoryInterface).ToList();

        // Never pass vacuously: the known repositories must be found.
        Assert.True(repositories.Count >= 3, "Expected at least the staff, role and branch repositories.");
        Assert.All(repositories, r => Assert.Equal(Domain, r.Assembly));
    }

    [Fact]
    public void Every_domain_repository_is_implemented_in_infrastructure()
    {
        var implementations = Infrastructure.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false }).ToList();

        foreach (var repository in Domain.GetTypes().Where(IsRepositoryInterface))
        {
            Assert.Contains(implementations, t => repository.IsAssignableFrom(t));
        }
    }

    [Fact]
    public void Query_services_are_declared_in_the_application_layer()
    {
        var queries = AllLayers.SelectMany(a => a.GetTypes())
            .Where(t => t.IsInterface && t.Name.EndsWith("Queries", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(queries);
        Assert.All(queries, q => Assert.Equal(Application, q.Assembly));
    }

    [Fact]
    public void Ef_core_is_used_only_by_infrastructure()
    {
        foreach (var assembly in new[] { Domain, Application, Web })
        {
            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOn("Microsoft.EntityFrameworkCore")
                .GetResult();

            Assert.True(
                result.IsSuccessful,
                $"{assembly.GetName().Name}: " + string.Join(", ", result.FailingTypeNames ?? []));
        }
    }

    private static bool IsRepositoryInterface(Type type) =>
        type.IsInterface && type.Name.StartsWith('I') && type.Name.EndsWith("Repository", StringComparison.Ordinal);
}
