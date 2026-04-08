using SharedKernel.Options;
using Shouldly;

namespace Tests.Application.SharedKernel;

public class OptionsConfigurationSectionNotFoundExceptionTests
{
    [Fact]
    public void Constructor_ShouldIncludeConfigurationKey_InMessage()
    {
        var ex = new OptionsConfigurationSectionNotFoundException("ScheduledJobs:ShopifyProductSync");

        ex.Message.ShouldContain("ScheduledJobs:ShopifyProductSync");
    }

    [Fact]
    public void Constructor_ShouldProduceReadableMessage_DescribingMissingSection()
    {
        var ex = new OptionsConfigurationSectionNotFoundException("MySection");

        ex.Message.ShouldBe("The configuration section 'MySection' was not found.");
    }

    [Fact]
    public void Exception_ShouldBeAssignableTo_Exception()
    {
        var ex = new OptionsConfigurationSectionNotFoundException("MySection");

        ex.ShouldBeAssignableTo<Exception>();
    }

    [Fact]
    public void Exception_ShouldBeCatchable_AsBaseException()
    {
        Action action = () => throw new OptionsConfigurationSectionNotFoundException("MySection");

        Should.Throw<Exception>(action);
    }
}
