using Assembly = System.Reflection.Assembly;
using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using AuthServer.Firmware;
using AuthServer.Firmware.Contracts;
using AuthServer.Identity;
using AuthServer.Identity.Contracts;
using AuthServer.Shared;
using AuthServer.Sync;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace AuthServer.ArchitectureTests;

// architecture.md §3: the module rules, enforced here rather than in review.
public sealed class ModuleBoundaryTests
{
    private static readonly Assembly Shared = typeof(Result).Assembly;
    private static readonly Assembly IdentityContracts = typeof(IdentityPolicies).Assembly;
    private static readonly Assembly FirmwareContracts = typeof(IFirmwareOffers).Assembly;

    // Each module: its assembly, its registration type and the Contracts it may reference besides Shared.
    private static readonly Dictionary<string, (Assembly Assembly, System.Type Registration, string[] Contracts)> Modules = new()
    {
        ["Identity"] = (typeof(IdentityModule).Assembly, typeof(IdentityModule), ["AuthServer.Identity.Contracts", "AuthServer.Firmware.Contracts"]),
        ["Sync"] = (typeof(SyncModule).Assembly, typeof(SyncModule), ["AuthServer.Identity.Contracts"]),
        ["Firmware"] = (typeof(FirmwareModule).Assembly, typeof(FirmwareModule), ["AuthServer.Identity.Contracts", "AuthServer.Firmware.Contracts"]),
    };

    public static TheoryData<string> ModuleNames => [.. Modules.Keys];

    private static readonly Architecture Architecture = new ArchLoader()
        .LoadAssemblies([Shared, IdentityContracts, FirmwareContracts, .. Modules.Values.Select(module => module.Assembly)])
        .Build();

    [Fact]
    public void Shared_knows_no_module()
    {
        References(Shared).ShouldNotContain(name => name.StartsWith("AuthServer.", StringComparison.Ordinal));
    }

    [Fact]
    public void Contracts_reference_only_Shared_and_no_framework_of_the_module()
    {
        References(FirmwareContracts)
            .Where(name => !name.StartsWith("System", StringComparison.Ordinal) && name != "netstandard")
            .ShouldAllBe(name => name == "AuthServer.Shared");
        References(IdentityContracts)
            .Where(name => !name.StartsWith("System", StringComparison.Ordinal) && name != "netstandard")
            .ShouldAllBe(name => name == "AuthServer.Shared");
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void A_module_publishes_only_its_registration(string name)
    {
        var (assembly, registration, _) = Modules[name];

        // EF generates migrations public; they are no API.
        assembly.GetExportedTypes()
            .Where(type => type.Namespace != $"AuthServer.{name}.Infrastructure.Persistence.Migrations")
            .Select(type => type.FullName)
            .ShouldBe([registration.FullName]);
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void A_module_references_only_Shared_and_Contracts_of_AuthServer(string name)
    {
        var (assembly, _, contracts) = Modules[name];

        References(assembly)
            .Where(reference => reference.StartsWith("AuthServer.", StringComparison.Ordinal))
            .ShouldAllBe(reference => reference == "AuthServer.Shared" || contracts.Contains(reference));
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void The_domain_depends_on_no_persistence_and_no_transport(string name)
    {
        Types().That().ResideInNamespace($"AuthServer.{name}.Domain")
            .Should().NotDependOnAny(Types().That()
                .ResideInNamespaceMatching(@"Microsoft\.EntityFrameworkCore.*")
                .Or().ResideInNamespaceMatching(@"Microsoft\.AspNetCore\.Http.*")
                .Or().ResideInNamespaceMatching(@"FastEndpoints.*")
                .Or().ResideInNamespaceMatching(@"OpenIddict.*")
                .Or().ResideInNamespaceMatching($@"AuthServer\.{name}\.(Features|Infrastructure).*"))
            .Check(Architecture);
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Slices_do_not_call_slices(string name)
    {
        var features = $"AuthServer.{name}.Features.";
        var slices = Modules[name].Assembly.GetTypes()
            .Select(type => type.Namespace)
            .OfType<string>()
            .Where(space => space.StartsWith(features, StringComparison.Ordinal))
            .Distinct()
            .ToList();

        slices.ShouldNotBeEmpty();

        foreach (var slice in slices)
        {
            Types().That().ResideInNamespace(slice)
                .Should().NotDependOnAny(Types().That()
                    .ResideInNamespaceMatching($@"AuthServer\.{name}\.Features\..*")
                    .And().DoNotResideInNamespace(slice))
                .Check(Architecture);
        }
    }

    private static IEnumerable<string> References(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(reference => reference.Name!);
}
