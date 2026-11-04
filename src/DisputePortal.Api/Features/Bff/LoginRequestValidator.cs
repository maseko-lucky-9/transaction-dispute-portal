using FluentValidation;

namespace DisputePortal.Api.Features.Bff;

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public const int MaxReturnUrlLength = 2048;

    public LoginRequestValidator() =>
        RuleFor(request => request.ReturnUrl)
            .MaximumLength(MaxReturnUrlLength)
            .Must(IsLocalPath).WithMessage("returnUrl must be a local path that starts with a single '/'.")
            .When(request => request.ReturnUrl is not null);

    // "//host" and "/\host" are protocol-relative in browsers; control characters can split the Location header.
    public static bool IsLocalPath(string? url) =>
        url is ['/', ..]
        && !url.StartsWith("//", StringComparison.Ordinal)
        && !url.StartsWith("/\\", StringComparison.Ordinal)
        && !url.Any(char.IsControl);
}
