using Shouldly;
using Web.Api.Features.Authentication.Login;

namespace Tests.Web.Api.Features.Authentication.Login;

public class LoginRequestValidatorTests
{
    [Fact]
    public void Validate_ShouldFail_WhenPasswordIsEmpty()
    {
        var result = new LoginRequestValidator().Validate(new LoginRequest());

        result.IsValid.ShouldBeFalse();
        result.Errors.Single().ErrorMessage.ShouldBe("Password is required.");
    }

    [Fact]
    public void Validate_ShouldSucceed_WhenPasswordIsProvided()
    {
        var result = new LoginRequestValidator().Validate(new LoginRequest { Password = "password" });

        result.IsValid.ShouldBeTrue();
    }
}
