using System.Net.Http.Json;
using System.Text;
using DisputePortal.Api.Shared.Persistence;
using DisputePortal.Api.Shared.Security;
using Microsoft.Net.Http.Headers;

namespace DisputePortal.IntegrationTests;

/// <summary>Requests to the agent endpoints, as the back-office UI sends them.</summary>
public static class AgentRequests
{
    public const string Queue = "/api/v1/agent/disputes";

    public static IdpUser Agent(string subject = Seeder.AgentSubject) => new(subject, "Agent", [Roles.DisputeAgent]);

    public static string Detail(Guid id) => $"{Queue}/{id}";

    public static HttpRequestMessage Transition(BrowserSession session, Guid id, string? ifMatch, string toStatus, string? note = null)
    {
        HttpRequestMessage request = session.Request(HttpMethod.Post, $"{Queue}/{id}/transitions");
        request.Content = JsonContent.Create(new { toStatus, note });
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation(HeaderNames.IfMatch, ifMatch);
        }

        return request;
    }

    public static HttpRequestMessage TransitionJson(BrowserSession session, Guid id, string ifMatch, string json)
    {
        HttpRequestMessage request = session.Request(HttpMethod.Post, $"{Queue}/{id}/transitions");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation(HeaderNames.IfMatch, ifMatch);
        return request;
    }
}
