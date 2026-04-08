using Application.Jobs;
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

        var jobDetail = await scheduler.GetJobDetail(ShopifySyncJob.Key);

        jobDetail.ShouldBeNull();
    }

    [Fact]
    public async Task AddScheduledJob_ShouldRegisterJob_WhenEnabledIsTrue()
    {
        var options = new JobScheduleOptions { Enabled = true, CronExpression = "0 0 * * * ?" };
        var scheduler = await BuildScheduler(options);

        var jobDetail = await scheduler.GetJobDetail(ShopifySyncJob.Key);

        jobDetail.ShouldNotBeNull();
        jobDetail.JobType.ShouldBe(typeof(ShopifySyncJob));
    }

    [Fact]
    public async Task AddScheduledJob_ShouldRegisterOneCronTrigger_WhenRunOnStartIsFalse()
    {
        var options = new JobScheduleOptions { Enabled = true, CronExpression = "0 0 * * * ?", RunOnStart = false };
        var scheduler = await BuildScheduler(options);

        var triggers = await scheduler.GetTriggersOfJob(ShopifySyncJob.Key);

        triggers.Count.ShouldBe(1);
        triggers.Single().ShouldBeAssignableTo<ICronTrigger>();
    }

    [Fact]
    public async Task AddScheduledJob_ShouldRegisterTwoTriggers_WhenRunOnStartIsTrue()
    {
        var options = new JobScheduleOptions { Enabled = true, CronExpression = "0 0 * * * ?", RunOnStart = true };
        var scheduler = await BuildScheduler(options);

        var triggers = await scheduler.GetTriggersOfJob(ShopifySyncJob.Key);

        triggers.Count.ShouldBe(2);
    }

    [Fact]
    public async Task AddScheduledJob_ShouldIncludeStartupSimpleTrigger_WhenRunOnStartIsTrue()
    {
        var options = new JobScheduleOptions { Enabled = true, CronExpression = "0 0 * * * ?", RunOnStart = true };
        var scheduler = await BuildScheduler(options);

        var triggers = await scheduler.GetTriggersOfJob(ShopifySyncJob.Key);

        triggers.ShouldContain(t => t is ISimpleTrigger);
    }

    [Fact]
    public async Task AddScheduledJob_ShouldUseCronExpression_FromOptions()
    {
        var cronExpression = "0 0/30 * * * ?";
        var options = new JobScheduleOptions { Enabled = true, CronExpression = cronExpression };
        var scheduler = await BuildScheduler(options);

        var triggers = await scheduler.GetTriggersOfJob(ShopifySyncJob.Key);

        var cronTrigger = triggers.OfType<ICronTrigger>().Single();
        cronTrigger.CronExpressionString.ShouldBe(cronExpression);
    }

    private static async Task<IScheduler> BuildScheduler(JobScheduleOptions options)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz(quartz =>
            quartz.AddScheduledJob<ShopifySyncJob>(ShopifySyncJob.Key, options));

        var provider = services.BuildServiceProvider();
        var schedulerFactory = provider.GetRequiredService<ISchedulerFactory>();
        return await schedulerFactory.GetScheduler();
    }
}
