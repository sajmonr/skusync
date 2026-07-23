using FastEndpoints;
using FluentValidation;

namespace Web.Api.Features.Authentication.Login;

public class LoginRequestValidator : Validator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(request => request.Password)
            .NotEmpty()
            .WithMessage("Password is required.");
    }
}
