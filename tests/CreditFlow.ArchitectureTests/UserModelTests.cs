using System.Reflection;
using CreditFlow.Domain;
using CreditFlow.Domain.Users;
using CreditFlow.Infrastructure;
using Microsoft.AspNetCore.Identity;

namespace CreditFlow.ArchitectureTests;

/// <summary>
/// The user model is split in two: <see cref="StaffMember"/> (Domain) holds the business data, and the
/// Identity account (Infrastructure) holds only authentication data. Both share the same id.
/// </summary>
public sealed class UserModelTests
{
    /// <summary>Business attributes that belong on <see cref="StaffMember"/>, never on the Identity account.</summary>
    private static readonly string[] BusinessAttributes =
    [
        nameof(StaffMember.FullName),
        nameof(StaffMember.RoleId),
        nameof(StaffMember.BranchId),
        nameof(StaffMember.IsActive),
        nameof(StaffMember.IsAvailable),
        nameof(StaffMember.Capacity),
    ];

    [Fact]
    public void Staff_member_lives_in_the_domain_and_carries_the_business_attributes()
    {
        Assert.Equal(typeof(DomainAssembly).Assembly, typeof(StaffMember).Assembly);

        var properties = typeof(StaffMember).GetProperties().Select(p => p.Name).ToHashSet();
        Assert.All(BusinessAttributes, name => Assert.Contains(name, properties));
    }

    [Fact]
    public void No_layer_other_than_Infrastructure_defines_an_Identity_user_type()
    {
        var assemblies = new[]
        {
            typeof(DomainAssembly).Assembly,
            typeof(CreditFlow.Application.ApplicationAssembly).Assembly,
            typeof(Program).Assembly,
        };

        var offenders = assemblies.SelectMany(a => a.GetTypes()).Where(IsIdentityUser).Select(t => t.FullName);

        Assert.Empty(offenders);
    }

    [Fact]
    public void Identity_account_holds_only_authentication_data()
    {
        var accountTypes = typeof(InfrastructureAssembly).Assembly.GetTypes().Where(IsIdentityUser).ToList();

        // Never pass vacuously: the account type must exist.
        Assert.NotEmpty(accountTypes);

        foreach (var type in accountTypes)
        {
            var declared = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(p => p.Name);
            Assert.Empty(declared.Intersect(BusinessAttributes));
        }
    }

    private static bool IsIdentityUser(Type type) =>
        type is { IsClass: true, IsAbstract: false } && typeof(IdentityUser<Guid>).IsAssignableFrom(type);
}
