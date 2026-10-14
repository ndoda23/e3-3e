using System.Reflection;
using CreditFlow.Application;
using CreditFlow.Domain;
using CreditFlow.Infrastructure;
using NetArchTest.Rules;

namespace CreditFlow.ArchitectureTests;

public sealed class LayerDependencyTests
{
    private static readonly Assembly Domain = typeof(DomainAssembly).Assembly;
    private static readonly Assembly Application = typeof(ApplicationAssembly).Assembly;
    private static readonly Assembly Infrastructure = typeof(InfrastructureAssembly).Assembly;
    private static readonly Assembly Web = typeof(Program).Assembly;

    [Fact]
    public void Domain_references_only_the_base_class_library()
    {
        var references = Domain.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => !name.StartsWith("System", StringComparison.Ordinal)
                           && name != "netstandard"
                           && name != "mscorlib")
            .ToList();

        Assert.Empty(references);
    }

    [Fact]
    public void Domain_does_not_depend_on_other_layers_or_frameworks()
    {
        var result = Types.InAssembly(Domain)
            .ShouldNot()
            .HaveDependencyOnAny(
                "CreditFlow.Application",
                "CreditFlow.Infrastructure",
                "CreditFlow.Web",
                "Microsoft.AspNetCore",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.Extensions",
                "FluentValidation")
            .GetResult();

        AssertSuccess(result);
    }

    [Fact]
    public void Application_does_not_depend_on_Infrastructure_or_Web()
    {
        var result = Types.InAssembly(Application)
            .ShouldNot()
            .HaveDependencyOnAny(
                "CreditFlow.Infrastructure",
                "CreditFlow.Web",
                "Microsoft.AspNetCore",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.Extensions.Identity")
            .GetResult();

        AssertSuccess(result);
    }

    [Fact]
    public void Application_assembly_does_not_reference_Infrastructure_or_Web()
    {
        var references = Application.GetReferencedAssemblies().Select(a => a.Name).ToList();

        Assert.DoesNotContain("CreditFlow.Infrastructure", references);
        Assert.DoesNotContain("CreditFlow.Web", references);
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_Web()
    {
        var result = Types.InAssembly(Infrastructure)
            .ShouldNot()
            .HaveDependencyOn("CreditFlow.Web")
            .GetResult();

        AssertSuccess(result);
    }

    [Fact]
    public void Controllers_do_not_use_DbContext_directly()
    {
        var controllers = Types.InAssembly(Web)
            .That()
            .Inherit(typeof(Microsoft.AspNetCore.Mvc.ControllerBase))
            .Or()
            .HaveNameEndingWith("Controller");

        // Never pass vacuously: the rule must actually inspect controllers.
        var found = controllers.GetTypes().Where(t => !t.IsAbstract).ToList();
        Assert.True(found.Count >= 1, "No controller types were found in CreditFlow.Web.");

        var result = controllers
            .ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "CreditFlow.Infrastructure.Persistence")
            .GetResult();

        AssertSuccess(result);
    }

    private static void AssertSuccess(NetArchTest.Rules.TestResult result)
    {
        var offenders = result.FailingTypeNames ?? [];
        Assert.True(result.IsSuccessful, "Violating types: " + string.Join(", ", offenders));
    }
}
