using System.Text.Json.Serialization;
using DisputePortal.Api.Features.Agent;
using DisputePortal.Api.Features.Bff;
using DisputePortal.Api.Features.Disputes;
using DisputePortal.Api.Features.Me;
using DisputePortal.Api.Features.Transactions;
using DisputePortal.Api.Shared.Eligibility;
using DisputePortal.Api.Shared.Errors;
using DisputePortal.Api.Shared.Health;
using DisputePortal.Api.Shared.Hosting;
using DisputePortal.Api.Shared.Observability;
using DisputePortal.Api.Shared.OpenApi;
using DisputePortal.Api.Shared.Persistence;
using DisputePortal.Api.Shared.Secrets;
using DisputePortal.Api.Shared.Security;
using FluentValidation;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;

// The chiseled image has no curl: the container's healthcheck runs this binary with --healthcheck.
if (args.Contains(RunModes.HealthCheck))
{
    return await ManagementProbe.RunAsync();
}

bool migrate = args.Contains(RunModes.Migrate);
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Catch captive dependencies and missing registrations at startup, in every environment.
builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
});

// JSON logs; scopes carry the trace and span ids. Bodies, tokens and SQL parameters are never logged.
builder.Logging.ClearProviders().AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
});

try
{
    builder.Configuration.AddVaultSecrets(migrate);
}
catch (VaultUnavailableException exception)
{
    using ILoggerFactory startupLogging = LoggerFactory.Create(logging => logging.AddJsonConsole());
    startupLogging.CreateLogger("Startup").LogCritical("{Reason}; " + DependencyGate.StartStackHint, exception.Message);
    return 1;
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddPersistence(migrate ? DatabaseUse.Migrations : DatabaseUse.Application);
builder.Services.AddDataProtectionKeyRing();
// Before the migrate branch too: the seeder dates its demo data against the same configured window.
builder.Services.AddDisputeEligibility();

if (migrate)
{
    // EF probes the history table before it exists and logs that expected miss as an error; real failures still
    // surface through MigrationRunner's single line.
    builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.None);
    await using WebApplication migrator = builder.Build();
    return await MigrationRunner.RunAsync(migrator.Services, CancellationToken.None);
}

builder.ConfigurePorts();
builder.AddTelemetry();
builder.Services.AddTrustedProxy(builder.Configuration);
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ErrorCodes.Complete);
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddSessionStore();
builder.Services.AddReadinessChecks();
builder.Services.AddBffAuthentication();
builder.Services.AddPortalAuthorization();
builder.Services.AddPerSubjectRateLimiter(builder.Configuration);
builder.Services.AddRequestTimeouts(options => options.DefaultPolicy = new RequestTimeoutPolicy { Timeout = TimeSpan.FromSeconds(30) });
builder.Services.AddApiDocumentation();
builder.Services.AddSingleton<IValidator<LoginRequest>, LoginRequestValidator>();
builder.Services.AddTransactions();
builder.Services.AddDisputes();
builder.Services.AddAgent();
// Enums cross the wire by name ("Posted"), and the OpenAPI document lists their values for the generated TS types.
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

WebApplication app = builder.Build();

// First, so everything after it (the rate limiter above all) sees the client's address, not nginx's. Public port only.
app.UseForwardedHeadersOnThePublicPort();
app.UseExceptionHandler();
// Bare 401/403/404/429 statuses get a body from the same writer as every other error.
app.UseStatusCodePages(context => ProblemResponses.WriteAsync(context.HttpContext, new ProblemDetails()));
// After the status-code pages, so their 404s and the CSRF 403 carry a Problem Details body.
app.UseMiddleware<ManagementPortMiddleware>();
app.UseMiddleware<NoStoreMiddleware>();
app.UseMiddleware<CsrfMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseRequestTimeouts();
app.MapHealthEndpoints();
app.MapBffEndpoints();
app.MapMeEndpoints();
app.MapTransactionsEndpoints();
app.MapDisputesEndpoints();
app.MapAgentEndpoints();
app.MapApiDocumentation();

if (!await DependencyGate.WaitAsync(app.Services, app.Logger, CancellationToken.None))
{
    return 1;
}

await app.RunAsync();
return 0;
