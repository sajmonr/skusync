using Infrastructure.Database;
using Microsoft.Extensions.Hosting;

namespace Infrastructure;

public static class DependencyInjection
{
    extension<T>(T builder)
        where T : IHostApplicationBuilder
    {
        public T AddInfrastructure()
        {
            builder.AddDatabase();
            
            return builder;
        }
    }
    
}