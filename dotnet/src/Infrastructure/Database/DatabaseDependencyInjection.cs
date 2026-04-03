using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Options;

namespace Infrastructure.Database;

public static class DatabaseDependencyInjection
{
    private const string ConnectionStringConfigurationKey = "SkuSync";

    extension(IHost app)
    {
        public void ApplyDatabaseMigrations()
        {
            var scopeFactory = app.Services.GetRequiredService<IServiceScopeFactory>();
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            dbContext.Database.Migrate();
        }
    }

    extension(IHostApplicationBuilder builder)
    {
        public IHostApplicationBuilder AddDatabase()
        {
            var skuSyncConnectionString = builder.GetConnectionStringOrThrow(ConnectionStringConfigurationKey);

            builder.Services.AddHealthChecks()
                .AddNpgSql(skuSyncConnectionString);

            builder.Services.AddDbContext<ApplicationDbContext>(options =>
                options.UseNpgsql(skuSyncConnectionString));

            return builder;
        }
    }
}