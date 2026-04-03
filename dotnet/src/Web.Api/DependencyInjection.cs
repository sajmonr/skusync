namespace Web.Api;

public static class DependencyInjection
{
    extension<T>(T builder)
        where T : IHostApplicationBuilder
    {
        public T AddPresentation()
        {
            return builder;
        }
    }
}