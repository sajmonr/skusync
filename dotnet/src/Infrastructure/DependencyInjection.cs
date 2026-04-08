using Infrastructure.Database;
using Microsoft.Extensions.Hosting;

namespace Infrastructure;

public static class DependencyInjection
{
    extension<T>(T builder)
        where T : IHostApplicationBuilder
    {
        /// <summary>
        /// Registers all Infrastructure-layer services (database context, health checks)
        /// with the dependency injection container.
        /// </summary>
        /// <returns>The builder instance for further chaining.</returns>
        public T AddInfrastructure()
        {
            builder.AddDatabase();
            
            return builder;
        }
    }
    
}