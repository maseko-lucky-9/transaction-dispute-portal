using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Features.Bff;

/// <summary>The origin the browser sees (nginx), e.g. http://localhost:8080. Redirect URIs are built from it, never from the Host header.</summary>
public sealed class PublicSettings
{
    public const string Section = "Public";

    [Required]
    [Url]
    public string BaseUrl { get; init; } = string.Empty;

    public bool IsHttps => BaseUrl.StartsWith(Uri.UriSchemeHttps + "://", StringComparison.OrdinalIgnoreCase);

    public string Url(string path) => BaseUrl.TrimEnd('/') + path;
}
