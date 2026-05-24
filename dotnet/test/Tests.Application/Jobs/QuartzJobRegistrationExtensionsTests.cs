using Application.Jobs;
using Application.Jobs.Maintenance;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Shouldly;

namespace Tests.Application.Jobs;

public class QuartzJobRegistrationExtensionsTests
{
    [Fact]
    public async Task AddScheduledJob_ShouldNotRegisterJob_WhenEnabledIsFalse()
    {
        var options = new JobScheduleOptions { Enabled = false };
        var scheduler = await BuildScheduler(options);

        var jobDetail = await scheduler.GetJobDetail(ProductMaintenanceJob.Key);

        jobDetail.ShouldBeNull();
    }

    [Fact]
    public async Task AddScheduledJob_ShouldRegisterJob_WhenEnabledIsTrue()
    {
        var options = new JobScheduleOptions { Enabled = true, CronExpression = "0 0 * * * ?" };
        var scheduler = await BuildScheduler(options);

        var jobDetail = await scheduler.GetJobDetail(ProductMaintenanceJob.Key);

        jobDetail.ShouldNotBeNull();
        jobDetail.JobType.ShouldBe(typeof(ProductMaintenanceJob));
    }

    [Fact]
    public async Task AddScheduledJob_ShouldRegisterOneCronTrigger_WhenRunOnStartIsFalse()
    {
        var options = new JobScheduleOptions { Enabled = true, CronExpression = "0 0 * * * ?", RunOnStart = false };
        var scheduler = await BuildScheduler(options);

        var triggers = await scheduler.GetTriggersOfJob(ProductMaintenanceJob.Key);

        triggers.Count.ShouldBe(1);
        triggers.Single().ShouldBeAssignableTo<ICronTrigger>();
    }

    [Fact]
    public async Task AddScheduledJob_ShouldRegisterTwoTriggers_WhenRunOnStartIsTrue()
    {
        var options = new JobScheduleOptions { Enabled = true, CronExpression = "0 0 * * * ?", RunOnStart = true };
        var scheduler = await BuildScheduler(options);

        var triggers = await scheduler.GetTriggersOfJob(ProductMaintenanceJob.Key);

        triggers.Count.ShouldBe(2);
    }

    [Fact]
    public async Task AddScheduledJob_ShouldIncludeStartupSimpleTrigger_WhenRunOnStartIsTrue()
    {
        var options = new JobScheduleOptions { Enabled = true, CronExpression = "0 0 * * * ?", RunOnStart = true };
        var scheduler = await BuildScheduler(options);

        var triggers = await scheduler.GetTriggersOfJob(ProductMaintenanceJob.Key);

        triggers.ShouldContain(t => t is ISimpleTrigger);
    }

    [Fact]
    public async Task AddScheduledJob_ShouldUseCronExpression_FromOptions()
    {
        var cronExpression = "0 0/30 * * * ?";
        var options = new JobScheduleOptions { Enabled = true, CronExpression = cronExpression };
        var scheduler = await BuildScheduler(options);

        var triggers = await scheduler.GetTriggersOfJob(ProductMaintenanceJob.Key);

        var cronTrigger = triggers.OfType<ICronTrigger>().Single();
        cronTrigger.CronExpressionString.ShouldBe(cronExpression);
    }

    [Fact]
    public async Task AddScheduledJob_ShouldStartStartupTriggerImmediately_WhenJitterIsZero()
    {
        var before = DateTimeOffset.UtcNow;
        var options = new JobScheduleOptions
        {
            Enabled = true,
            CronExpression = "0 0 * * * ?",
            RunOnStart = true,
            StartupJitterMs = 0
        };
        var scheduler = await BuildScheduler(options);
        var after = DateTimeOffset.UtcNow;

        var triggers = await scheduler.GetTriggersOfJob(ProductMaintenanceJob.Key);
        var startup = triggers.OfType<ISimpleTrigger>().Single();

        // With zero jitter the trigger should fire at "now" (within the bracket of test setup time).
        startup.StartTimeUtc.ShouldBeGreaterThanOrEqualTo(before);
        startup.StartTimeUtc.ShouldBeLessThanOrEqualTo(after);
    }

    [Fact]
    public async Task AddScheduledJob_ShouldDelayStartupTrigger_WhenJitterIsConfigured()
    {
        const int jitterMs = 5_000;
        var before = DateTimeOffset.UtcNow;
        var options = new JobScheduleOptions
        {
            Enabled = true,
            CronExpression = "0 0 * * * ?",
            RunOnStart = true,
            StartupJitterMs = jitterMs
        };
        var scheduler = await BuildScheduler(options);
        var after = DateTimeOffset.UtcNow;

        var triggers = await scheduler.GetTriggersOfJob(ProductMaintenanceJob.Key);
        var startup = triggers.OfType<ISimpleTrigger>().Single();

        // Start time must fall inside [now, now + jitter]. We don't pin a tighter bound because
        // the sample is uniformly random — asserting any specific value would be flaky.
        startup.StartTimeUtc.ShouldBeGreaterThanOrEqualTo(before);
        startup.StartTimeUtc.ShouldBeLessThanOrEqualTo(after + TimeSpan.FromMilliseconds(jitterMs));
    }

    [Fact]
    public async Task AddScheduledJob_ShouldSpaceStartupTriggers_AcrossManyRegistrations()
    {
        // The whole point of jitter is that two jobs sharing RunOnStart end up with *different*
        // start times. With 1s of jitter and 20 samples the probability of all 20 landing in
        // any 10ms window is vanishingly small, so we assert the spread is more than that.
        const int jitterMs = 1_000;
        var startTimes = new List<DateTimeOffset>();

        for (var i = 0; i < 20; i++)
        {
            var options = new JobScheduleOptions
            {
                Enabled = true,
                CronExpression = "0 0 * * * ?",
                RunOnStart = true,
                StartupJitterMs = jitterMs
            };
            var scheduler = await BuildScheduler(options);
            var triggers = await scheduler.GetTriggersOfJob(ProductMaintenanceJob.Key);
            startTimes.Add(triggers.OfType<ISimpleTrigger>().Single().StartTimeUtc);
        }

        var spread = startTimes.Max() - startTimes.Min();
        spread.ShouldBeGreaterThan(TimeSpan.FromMilliseconds(10));
    }

    private static async Task<IScheduler> BuildScheduler(JobScheduleOptions options)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz(quartz =>
            quartz.AddScheduledJob<ProductMaintenanceJob>(ProductMaintenanceJob.Key, options));

        var provider = services.BuildServiceProvider();
        var schedulerFactory = provider.GetRequiredService<ISchedulerFactory>();
        return await schedulerFactory.GetScheduler();
    }
}
