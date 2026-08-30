using System.Reflection;
using GaifulinLab.Api.Controllers;
using GaifulinLab.Application;
using GaifulinLab.Contracts;
using GaifulinLab.Domain.Articles;
using GaifulinLab.Infrastructure;
using GaifulinLab.Web.Components;
using Microsoft.AspNetCore.Mvc;

namespace GaifulinLab.Architecture.Tests.Architecture;

public sealed class ProjectDependencyTests
{
    [Fact]
    public void Domain_DoesNotDependOnOtherGaifulinLabProjects() =>
        AssertDoesNotReference(DomainAssembly, AllOtherProjects("GaifulinLab.Domain"));

    [Fact]
    public void Contracts_DoesNotDependOnOtherGaifulinLabProjects() =>
        AssertDoesNotReference(ContractsProjectAssembly, AllOtherProjects("GaifulinLab.Contracts"));

    [Fact]
    public void Application_DoesNotDependOnInfrastructureApiOrWeb() =>
        AssertDoesNotReference(
            ApplicationProjectAssembly,
            ["GaifulinLab.Infrastructure", "GaifulinLab.Api", "GaifulinLab.Web"]);

    [Fact]
    public void Infrastructure_DoesNotDependOnApiOrWeb() =>
        AssertDoesNotReference(
            InfrastructureProjectAssembly,
            ["GaifulinLab.Api", "GaifulinLab.Web"]);

    [Fact]
    public void Api_DoesNotDependOnWeb() =>
        AssertDoesNotReference(ApiAssembly, ["GaifulinLab.Web"]);

    [Fact]
    public void Web_DoesNotDependOnServerProjects() =>
        AssertDoesNotReference(
            WebAssembly,
            [
                "GaifulinLab.Domain",
                "GaifulinLab.Application",
                "GaifulinLab.Infrastructure",
                "GaifulinLab.Api"
            ]);

    [Fact]
    public void ApiEndpointsAreMvcControllers()
    {
        var controllers = ApiAssembly
            .GetTypes()
            .Where(type => type.Namespace == "GaifulinLab.Api.Controllers")
            .Where(type => type.Name.EndsWith("Controller", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(controllers);
        Assert.All(
            controllers,
            controller => Assert.True(
                typeof(ControllerBase).IsAssignableFrom(controller),
                $"{controller.FullName} must inherit {nameof(ControllerBase)}."));
    }

    private static Assembly DomainAssembly => typeof(Article).Assembly;

    private static Assembly ContractsProjectAssembly => typeof(ContractsAssembly).Assembly;

    private static Assembly ApplicationProjectAssembly => typeof(ApplicationAssembly).Assembly;

    private static Assembly InfrastructureProjectAssembly => typeof(InfrastructureAssembly).Assembly;

    private static Assembly ApiAssembly => typeof(AuthController).Assembly;

    private static Assembly WebAssembly => typeof(App).Assembly;

    private static readonly string[] ProjectNames =
    [
        "GaifulinLab.Domain",
        "GaifulinLab.Contracts",
        "GaifulinLab.Application",
        "GaifulinLab.Infrastructure",
        "GaifulinLab.Api",
        "GaifulinLab.Web"
    ];

    private static string[] AllOtherProjects(string assemblyName) =>
        ProjectNames.Where(projectName => projectName != assemblyName).ToArray();

    private static void AssertDoesNotReference(Assembly assembly, IEnumerable<string> forbiddenProjects)
    {
        var references = assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToHashSet(StringComparer.Ordinal);

        var forbiddenReference = forbiddenProjects.FirstOrDefault(references.Contains);
        Assert.Null(forbiddenReference);
    }
}
