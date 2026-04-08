using Quartz;

namespace Application.Jobs;

/// <summary>
/// Extension methods for <see cref="IServiceCollectionQuartzConfigurator"/> that simplify
/// registering application jobs from a <see cref="JobScheduleOptions"/> configuration object.
/// </summary>
public static class QuartzJobRegistrationExtensions
{
    /// <summary>
    /// Registers a Quartz job and its triggers based on the supplied <paramref name="options"/>.
    /// <list type="bullet">
    ///   <item><description>When <see cref="JobScheduleOptions.Enabled"/> is <c>false</c>, no job or trigger is registered.</description></item>
    ///   <item><description>A cron trigger is always added using <see cref="JobScheduleOptions.CronExpression"/>.</description></item>
    ///   <item><description>When <see cref="JobScheduleOptions.RunOnStart"/> is <c>true</c>, an additional one-shot trigger fires as soon as the scheduler starts.</description></item>
    /// </list>
    /// </summary>
    /// <typeparam name="TJob">The <see cref="IJob"/> implementation to register.</typeparam>
    /// <param name="quartz">The Quartz configurator to register the job and triggers on.</param>
    /// <param name="jobKey">The stable key used to identify this job within the scheduler.</param>
    /// <param name="options">The schedule options read from configuration.</param>
    /// <returns>The same <paramref name="quartz"/> configurator for fluent chaining.</returns>
    public static IServiceCollectionQuartzConfigurator AddScheduledJob<TJob>(
        this IServiceCollectionQuartzConfigurator quartz,
        JobKey jobKey,
        JobScheduleOptions options)
        where TJob : IJob
    {
        if (!options.Enabled)
        {
            return quartz;
        }

        quartz.AddJob<TJob>(opts => opts.WithIdentity(jobKey).StoreDurably());

        quartz.AddTrigger(opts => opts
            .ForJob(jobKey)
            .WithIdentity($"{jobKey.Name}-cron")
            .WithCronSchedule(options.CronExpression));

        if (options.RunOnStart)
        {
            quartz.AddTrigger(opts => opts
                .ForJob(jobKey)
                .WithIdentity($"{jobKey.Name}-startup")
                .StartNow()
                .WithSimpleSchedule(s => s.WithRepeatCount(0)));
        }

        return quartz;
    }
}
