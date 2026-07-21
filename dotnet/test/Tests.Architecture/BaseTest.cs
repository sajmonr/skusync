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

    // Resolved via each host's DependencyInjection type rather than its global Program class:
    // both Web.Api and AppServer declare a top-level Program, so typeof(Program) is ambiguous.
    protected static readonly Assembly PresentationAssembly =
        typeof(Web.Api.DependencyInjection).Assembly;
    protected static readonly Assembly AppServerAssembly =
        typeof(AppServer.DependencyInjection).Assembly;
}
