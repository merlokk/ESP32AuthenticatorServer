using Assembly = System.Reflection.Assembly;
using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using AuthServer.Identity;
using AuthServer.Identity.Contracts;
using AuthServer.Shared;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace AuthServer.ArchitectureTests;

// architecture.md §3: the module rules, enforced here rather than in review.
public sealed class ModuleBoundaryTests
{
    private static readonly Assembly Shared = typeof(Result).Assembly;
    private static readonly Assembly Identity = typeof(IdentityModule).Assembly;
    private static readonly Assembly IdentityContracts = typeof(IdentityPolicies).Assembly;

    private static readonly Architecture Architecture = new ArchLoader()
        .LoadAssemblies(Shared, Identity, IdentityContracts)
        .Build();

    [Fact]
    public void Shared_knows_no_module()
    {
        References(Shared).ShouldNotContain(name => name.StartsWith("AuthServer.", StringComparison.Ordinal));
    }

    [Fact]
    public void Contracts_reference_only_Shared_and_no_framework_of_the_module()
    {
        References(IdentityContracts)
            .Where(name => !name.StartsWith("System", StringComparison.Ordinal) && name != "netstandard")
            .ShouldAllBe(name => name == "AuthServer.Shared");
    }

    [Fact]
    public void Identity_publishes_only_its_registration()
    {
        // EF generates migrations public; they are no API.
        Identity.GetExportedTypes()
            .Where(type => type.Namespace != "AuthServer.Identity.Infrastructure.Persistence.Migrations")
            .Select(type => type.FullName)
            .ShouldBe([typeof(IdentityModule).FullName]);
    }

    [Fact]
    public void A_module_references_only_Shared_and_Contracts_of_AuthServer()
    {
        References(Identity)
            .Where(name => name.StartsWith("AuthServer.", StringComparison.Ordinal))
            .ShouldAllBe(name => name == "AuthServer.Shared" || name == "AuthServer.Identity.Contracts");
    }

    [Fact]
    public void The_domain_depends_on_no_persistence_and_no_transport()
    {
        Types().That().ResideInNamespace("AuthServer.Identity.Domain")
            .Should().NotDependOnAny(Types().That()
                .ResideInNamespaceMatching(@"Microsoft\.EntityFrameworkCore.*")
                .Or().ResideInNamespaceMatching(@"Microsoft\.AspNetCore\.Http.*")
                .Or().ResideInNamespaceMatching(@"FastEndpoints.*")
                .Or().ResideInNamespaceMatching(@"OpenIddict.*")
                .Or().ResideInNamespaceMatching(@"AuthServer\.Identity\.(Features|Infrastructure).*"))
            .Check(Architecture);
    }

    [Fact]
    public void Slices_do_not_call_slices()
    {
        var slices = Identity.GetTypes()
            .Select(type => type.Namespace)
            .OfType<string>()
            .Where(name => name.StartsWith("AuthServer.Identity.Features.", StringComparison.Ordinal))
            .Distinct()
            .ToList();

        slices.ShouldNotBeEmpty();

        foreach (var slice in slices)
        {
            Types().That().ResideInNamespace(slice)
                .Should().NotDependOnAny(Types().That()
                    .ResideInNamespaceMatching(@"AuthServer\.Identity\.Features\..*")
                    .And().DoNotResideInNamespace(slice))
                .Check(Architecture);
        }
    }

    private static IEnumerable<string> References(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(reference => reference.Name!);
}
