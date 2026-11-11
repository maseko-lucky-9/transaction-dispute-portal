using System.Net;
using System.Net.Http.Json;

namespace DisputePortal.UnitTests;

/// <summary>Vault's answers to the AppRole or Kubernetes login and the KV v2 reads, shared by the in-memory and the TLS fakes.</summary>
internal static class FakeVaultResponses
{
    public static HttpResponseMessage For(string path) => path switch
    {
        "/v1/auth/approle/login" or "/v1/auth/kubernetes/login" or "/v1/auth/k8s-homelab/login" => Json(new { auth = new { client_token = "token", lease_duration = 60, renewable = false, policies = Array.Empty<string>() } }),
        "/v1/kv/data/dispute-portal/database" => Secret(new() { ["Host"] = "postgres", ["Port"] = "5432", ["Name"] = "dispute_portal", ["Username"] = "dispute_app", ["Password"] = "app-pass" }),
        "/v1/kv/data/dispute-portal/database-owner" => Secret(new() { ["Host"] = "postgres", ["Username"] = "dispute_owner", ["Password"] = "owner-pass" }),
        "/v1/kv/data/dispute-portal/redis" => Secret(new() { ["Host"] = "redis", ["Username"] = "dispute_app", ["Password"] = "redis-pass" }),
        "/v1/kv/data/dispute-portal/keycloak" => Secret(new() { ["ClientSecret"] = "kc-secret" }),
        _ => new HttpResponseMessage(HttpStatusCode.Forbidden),
    };

    private static HttpResponseMessage Secret(Dictionary<string, string> data) =>
        Json(new { data = new { data, metadata = new { version = 1, created_time = "2026-11-04T00:00:00Z", destroyed = false } } });

    private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
}
