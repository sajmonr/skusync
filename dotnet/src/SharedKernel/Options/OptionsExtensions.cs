using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SharedKernel.Options;

public static class OptionsExtensions
{

    extension(IHostApplicationBuilder builder)
    {
        /// <summary>
        /// Adds options of type <typeparamref name="TOptions"/> to the service collection by binding them to a configuration section.
        /// Also validates the options using data annotations and optionally validates them on application start.
        /// </summary>
        /// <typeparam name="TOptions">The type of options to bind and configure.</typeparam>
        /// <param name="sectionKey">The configuration section key to bind the options to.</param>
        /// <param name="validateOnStart">
        /// A boolean value indicating whether to validate the options on application startup.
        /// Defaults to <c>true</c>.
        /// </param>
        /// <returns>The host application builder instance, allowing further configuration chaining.</returns>
        public IHostApplicationBuilder AddOptionsFromConfiguration<TOptions>(string sectionKey, bool validateOnStart = true)
        where TOptions : class
        {
            var optionsBuilder = builder.Services
                .AddOptions<TOptions>()
                .BindConfiguration(sectionKey)
                .ValidateDataAnnotations();

            if (validateOnStart)
            {
                optionsBuilder.ValidateOnStart();
            }
            
            return builder;
        }

        /// <summary>
        /// Retrieves a configuration section by the specified key or throws an exception if the section does not exist.
        /// </summary>
        /// <param name="sectionKey">The key of the configuration section to retrieve.</param>
        /// <returns>The configuration section associated with the specified key.</returns>
        /// <exception cref="OptionsConfigurationSectionNotFoundException">
        /// Thrown when the configuration section with the specified key is not found.
        /// </exception>
        public IConfigurationSection GetConfigurationSectionOrThrow(string sectionKey)
        {
            var section = builder.Configuration.GetSection(sectionKey);

            if (section.Exists())
            {
                return section;
            }
            
            throw new OptionsConfigurationSectionNotFoundException(sectionKey);
        }

        /// <summary>
        /// Retrieves the connection string associated with the specified key from the configuration.
        /// Throws an exception if the connection string is not found or is empty.
        /// </summary>
        /// <param name="connectionStringKey">The key of the connection string to retrieve.</param>
        /// <returns>The connection string associated with the specified key.</returns>
        /// <exception cref="OptionsConfigurationSectionNotFoundException">
        /// Thrown when the connection string for the specified key is not found or is empty.
        /// </exception>
        public string GetConnectionStringOrThrow(string connectionStringKey)
        {
            var connectionString = builder.Configuration.GetConnectionString(connectionStringKey);

            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                return connectionString;
            }
            
            throw new OptionsConfigurationSectionNotFoundException($"ConnectionStrings:{connectionStringKey}");
        }
        
    }
    
}