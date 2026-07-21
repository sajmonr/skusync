using System.Reflection;

namespace Tests.Architecture;

public abstract class BaseTest
{
    protected static readonly Assembly IntegrationAssembly =
        typeof(Integration.DependencyInjection).Assembly;
    protected static readonly Assembly ApplicationAssembly =
        typeof(Application.DependencyInjection).Assembly;
    protected static readonly Assembly InfrastructureAssembly =
        typeof(Infrastructure.DependencyInjection).Assembly;

    // Resolved via Web.Api's DependencyInjection type rather than its global Program class,
    // which would be ambiguous with AppServer's top-level Program.
    protected static readonly Assembly PresentationAssembly =
        typeof(Web.Api.DependencyInjection).Assembly;

    // AppServer composes everything inline in its (internal) top-level Program and exposes no
    // public type to anchor on, so its assembly is loaded by name. The project reference in
    // Tests.Architecture.csproj guarantees AppServer.dll sits alongside the test assembly.
    protected static readonly Assembly AppServerAssembly =
        Assembly.Load("AppServer");
}
