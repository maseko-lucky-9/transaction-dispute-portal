using System.Text.Json.Nodes;
using DisputePortal.Domain.Disputes;
using FluentValidation;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace DisputePortal.Api.Features.Agent;

public static class AgentRegistration
{
    public static IServiceCollection AddAgent(this IServiceCollection services)
    {
        services.AddSingleton<AgentCursorProtector>();
        services.AddSingleton<IValidator<AgentQueueRequest>, AgentQueueRequestValidator>();
        services.AddSingleton<IValidator<TransitionRequest>, TransitionRequestValidator>();
        services.AddScoped<IAgentDisputeRepository, AgentDisputeRepository>();
        services.AddScoped<AgentCommands>();
        services.AddScoped<AgentQueries>();
        services.Configure<OpenApiOptions>("v1", options => options.AddSchemaTransformer(DescribeTransitionRequest));
        return services;
    }

    // The body binds as strings (for field errors); the document states the agent's moves for the generated TS types.
    private static Task DescribeTransitionRequest(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.JsonTypeInfo.Type != typeof(TransitionRequest) || schema.Properties is null)
        {
            return Task.CompletedTask;
        }

        DisputeStatus[] agentMoves = [DisputeStatus.UnderReview, DisputeStatus.Accepted, DisputeStatus.Rejected];
        schema.Properties["toStatus"] = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Enum = [.. agentMoves.Select(status => (JsonNode)status.ToString())],
        };
        schema.Properties["note"] = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            MaxLength = Dispute.NoteMaxLength,
            Description = "Required to accept or reject; the customer sees it.",
        };
        schema.Required = new HashSet<string>(StringComparer.Ordinal) { "toStatus" };
        return Task.CompletedTask;
    }
}
