using System.Net;
using System.Net.Http.Json;
using Copilot.Domain;

namespace Copilot.Gorgias;

public sealed class GorgiasTicketClient(HttpClient httpClient) : IGorgiasTicketClient
{
    public async Task<TicketContext?> GetTicketAsync(long ticketId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"api/tickets/{ticketId}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new GorgiasApiException(
                response.StatusCode,
                $"Gorgias returned {(int)response.StatusCode} for ticket {ticketId}.");
        }

        var ticket = await response.Content.ReadFromJsonAsync<GorgiasTicketDto>(GorgiasJson.Options, cancellationToken)
            ?? throw new GorgiasApiException(response.StatusCode, $"Empty response body for ticket {ticketId}.");

        return GorgiasTicketMapper.ToTicketContext(ticket);
    }
}
