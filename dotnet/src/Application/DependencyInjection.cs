using Microsoft.Extensions.Hosting;

namespace Application;

public static class DependencyInjection
{
    extension<T>(T builder)
        where T : IHostApplicationBuilder
    {
        public T AddApplication()
        {
            return builder;
        }
    }
}