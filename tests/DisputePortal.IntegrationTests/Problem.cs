using System.Text.Json;

namespace DisputePortal.IntegrationTests;

/// <summary>A Problem Details body, read after checking the RFC 9457 envelope every error carries.</summary>
public sealed record Problem(string Code, string Detail, JsonElement Body)
{
    public static async Task<Problem> ReadAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement body = document.RootElement.Clone();
        Assert.False(string.IsNullOrEmpty(body.GetProperty("traceId").GetString()));
        Assert.Equal((int)response.StatusCode, body.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("type").GetString()));
        Assert.False(string.IsNullOrEmpty(body.GetProperty("title").GetString()));
        string detail = body.GetProperty("detail").GetString() ?? "";
        Assert.NotEqual("", detail.Trim());
        Assert.Equal(response.RequestMessage?.RequestUri?.AbsolutePath, body.GetProperty("instance").GetString());
        return new Problem(body.GetProperty("code").GetString() ?? "", detail, body);
    }
}
