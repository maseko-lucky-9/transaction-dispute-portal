using FluentValidation;

namespace DisputePortal.Api.Features.Transactions;

public static class TransactionsRegistration
{
    public static IServiceCollection AddTransactions(this IServiceCollection services)
    {
        services.AddSingleton<TransactionCursorProtector>();
        services.AddSingleton<IValidator<TransactionListRequest>, TransactionListRequestValidator>();
        services.AddScoped<TransactionQueries>();
        return services;
    }
}
