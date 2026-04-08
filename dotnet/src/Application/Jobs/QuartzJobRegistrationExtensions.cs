using Quartz;

namespace Application.Jobs;

public static class QuartzJobRegistrationExtensions
{
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
