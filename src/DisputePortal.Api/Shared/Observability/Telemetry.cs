using System.Diagnostics;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace DisputePortal.Api.Shared.Observability;

public static class Telemetry
{
    /// <summary>Spans the app creates itself (the Redis session store has no stable OpenTelemetry instrumentation).</summary>
    public static readonly ActivitySource Source = new("DisputePortal.Api");

    private const string EndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>
    /// Traces and metrics for ASP.NET Core, HttpClient, Npgsql (EF Core's queries) and the session store, exported over
    /// OTLP only when an endpoint is configured. Query strings never reach a span: they can carry state or codes.
    /// </summary>
    public static WebApplicationBuilder AddTelemetry(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<SecurityMetrics>();
        if (string.IsNullOrWhiteSpace(builder.Configuration[EndpointVariable]))
        {
            return builder;
        }

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("dispute-portal-api"))
            .WithTracing(tracing => tracing
                .AddSource(Source.Name)
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.EnrichWithHttpRequest = (activity, _) => activity.SetTag("url.query", null);
                    options.Filter = context => !context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase);
                })
                .AddHttpClientInstrumentation(options =>
                    options.EnrichWithHttpRequestMessage = (activity, request) =>
                        activity.SetTag("url.full", request.RequestUri?.GetLeftPart(UriPartial.Path)))
                .AddNpgsql())
            .WithMetrics(metrics => metrics
                .AddMeter(SecurityMetrics.MeterName)
                // The framework's own meters (.NET 8+): request duration, Kestrel connections, HttpClient, Npgsql pool.
                .AddMeter("Microsoft.AspNetCore.Hosting", "Microsoft.AspNetCore.Server.Kestrel", "System.Net.Http", "Npgsql"))
            // Reads OTEL_EXPORTER_OTLP_ENDPOINT (and the other OTEL_* variables) itself.
            .UseOtlpExporter();
        return builder;
    }
}
