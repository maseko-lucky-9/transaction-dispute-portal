using DisputePortal.Domain.Eligibility;
using Microsoft.Extensions.Options;

namespace DisputePortal.Api.Shared.Eligibility;

public static class EligibilityRegistration
{
    /// <summary>The Domain's eligibility chain. A new rule is a new class plus one line here; rules run in this order.</summary>
    public static IServiceCollection AddDisputeEligibility(this IServiceCollection services)
    {
        services.AddOptions<DisputePolicyOptions>().BindConfiguration(DisputePolicyOptions.Section)
            .Validate(options => options.WindowDays > 0, "DisputePolicy:WindowDays must be positive.")
            .Validate(options => options.MaxActiveDisputesPerCustomer > 0, "DisputePolicy:MaxActiveDisputesPerCustomer must be positive.")
            .Validate(options => options.MaxDisputesPerTransaction > 0, "DisputePolicy:MaxDisputesPerTransaction must be positive.")
            .ValidateOnStart();
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<DisputePolicyOptions>>().Value);
        services.AddSingleton<IDisputeEligibilityRule, PostedRule>();
        services.AddSingleton<IDisputeEligibilityRule, DebitRule>();
        services.AddSingleton<IDisputeEligibilityRule, DisputeWindowRule>();
        services.AddSingleton<IDisputeEligibilityRule, AmountRule>();
        services.AddSingleton<IDisputeEligibilityRule, ActiveDisputeCapRule>();
        services.AddSingleton<IDisputeEligibilityRule, TransactionDisputeCapRule>();
        services.AddSingleton<DisputeEligibilityPolicy>();
        return services;
    }
}
