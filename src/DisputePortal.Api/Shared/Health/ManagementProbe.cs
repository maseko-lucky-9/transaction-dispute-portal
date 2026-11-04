namespace DisputePortal.Api.Shared.Health;

/// <summary>The container's <c>--healthcheck</c>: the chiseled image has no curl, so the app probes itself.</summary>
public static class ManagementProbe
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    public static async Task<int> RunAsync()
    {
        int port = int.TryParse(Environment.GetEnvironmentVariable("Ports__Management"), out int configured) ? configured : new ManagementSettings().Management;
        using HttpClient client = new() { Timeout = Timeout };
        try
        {
            using HttpResponseMessage response = await client.GetAsync(new Uri($"http://localhost:{port}{ManagementSettings.LivePath}"));
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return 1;
        }
    }
}
