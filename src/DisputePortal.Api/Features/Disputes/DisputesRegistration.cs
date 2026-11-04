using DisputePortal.Api.Shared.Observability;
using FluentValidation;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace DisputePortal.Api.Features.Disputes;

public static class DisputesRegistration
{
    public static IServiceCollection AddDisputes(this IServiceCollection services)
    {
        services.AddSingleton<DisputeCursorProtector>();
        services.AddSingleton<DisputeMetrics>();
        services.AddSingleton<IValidator<DisputeListRequest>, DisputeListRequestValidator>();
        // Scoped: it asks the database whether the reason exists.
        services.AddScoped<IValidator<CreateDisputeRequest>, CreateDisputeRequestValidator>();
        services.AddScoped<IDisputeRepository, DisputeRepository>();
        services.AddScoped<IDisputeReasonRepository, DisputeReasonRepository>();
        services.AddScoped<DisputeCommands>();
        services.AddScoped<DisputeQueries>();
        services.Configure<OpenApiOptions>("v1", options => options.AddSchemaTransformer(DescribeCreateRequest));
        return services;
    }

    // The body binds as strings (for field-level errors); the document states the real formats for the generated TS types.
    private static Task DescribeCreateRequest(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.JsonTypeInfo.Type != typeof(CreateDisputeRequest) || schema.Properties is null)
        {
            return Task.CompletedTask;
        }

        schema.Properties["id"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" };
        schema.Properties["transactionId"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" };
        schema.Properties["reasonCode"] = new OpenApiSchema { Type = JsonSchemaType.String, MinLength = 1, MaxLength = DisputeReasonEntry.CodeMaxLength };
        schema.Properties["description"] = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            MinLength = CreateDisputeRequestValidator.DescriptionMinLength,
            MaxLength = Domain.Disputes.Dispute.DescriptionMaxLength,
        };
        schema.Properties["amount"] = new OpenApiSchema { Type = JsonSchemaType.String, Pattern = @"^(?:0|[1-9][0-9]{0,12})(?:\.[0-9]{1,2})?$" };
        schema.Required = new HashSet<string>(StringComparer.Ordinal) { "id", "transactionId", "reasonCode", "description" };
        return Task.CompletedTask;
    }
}
