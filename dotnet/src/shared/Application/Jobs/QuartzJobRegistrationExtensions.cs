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
    ///   <item><description>When <see cref="JobScheduleOptions.RunOnStart"/> is <c>true</c>, an additional one-shot trigger fires when the scheduler starts. If <see cref="JobScheduleOptions.StartupJitterMs"/> is non-zero, a uniformly random offset in <c>[0, StartupJitterMs]</c> milliseconds is added to that trigger's start time so multiple jobs with <c>RunOnStart</c> don't hammer downstreams simultaneously at boot.</description></item>
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
            var startAt = DateTimeOffset.UtcNow + SampleJitter(options.StartupJitterMs);
            quartz.AddTrigger(opts => opts
                .ForJob(jobKey)
                .WithIdentity($"{jobKey.Name}-startup")
                .StartAt(startAt)
                .WithSimpleSchedule(s => s.WithRepeatCount(0)));
        }

        return quartz;
    }

    /// <summary>
    /// Returns a uniformly random <see cref="TimeSpan"/> in <c>[0, maxMs]</c> milliseconds.
    /// Negative or zero inputs return <see cref="TimeSpan.Zero"/> so callers can pass the
    /// configured value through unconditionally.
    /// </summary>
    private static TimeSpan SampleJitter(int maxMs)
    {
        if (maxMs <= 0)
        {
            return TimeSpan.Zero;
        }
        return TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * maxMs);
    }
}
