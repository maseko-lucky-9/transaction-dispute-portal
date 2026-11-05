namespace DisputePortal.Api.Shared.Hosting;

/// <summary>One image, three entry points: serve (default), <c>--migrate</c> (one-shot) and <c>--healthcheck</c> (container probe).</summary>
public static class RunModes
{
    public const string Migrate = "--migrate";
    public const string HealthCheck = "--healthcheck";
}
