using System.Net.Http.Json;
using System.Text;
using Microsoft.Net.Http.Headers;

namespace DisputePortal.IntegrationTests;

/// <summary>Requests to the Disputes endpoints, as the web app sends them.</summary>
public static class DisputeRequests
{
    public const string Disputes = "/api/v1/disputes";
    public const string Reasons = "/api/v1/dispute-reasons";
    public const string Description = "Charged twice for one order.";
    public const string Reason = "duplicate_charge";

    public static HttpRequestMessage Submit(
        BrowserSession session, Guid id, Guid transactionId, string? amount = null, string description = Description, string reasonCode = Reason)
    {
        HttpRequestMessage request = session.Request(HttpMethod.Post, Disputes);
        request.Content = JsonContent.Create(new { id, transactionId, reasonCode, description, amount });
        return request;
    }

    public static HttpRequestMessage SubmitJson(BrowserSession session, string json)
    {
        HttpRequestMessage request = session.Request(HttpMethod.Post, Disputes);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }

    public static HttpRequestMessage Withdraw(BrowserSession session, Guid id, string? ifMatch)
    {
        HttpRequestMessage request = session.Request(HttpMethod.Post, $"{Disputes}/{id}/withdraw");
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation(HeaderNames.IfMatch, ifMatch);
        }

        return request;
    }

    public static string? ETagOf(HttpResponseMessage response) => response.Headers.ETag?.ToString();
}
