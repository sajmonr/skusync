namespace Web.Api;

public static class DependencyInjection
{
    extension<T>(T builder)
        where T : IHostApplicationBuilder
    {
        /// <summary>
        /// Registers all Presentation-layer services (middleware, endpoint filters, etc.)
        /// with the dependency injection container.
        /// </summary>
        /// <returns>The builder instance for further chaining.</returns>
        public T AddPresentation()
        {
            return builder;
        }
    }
}