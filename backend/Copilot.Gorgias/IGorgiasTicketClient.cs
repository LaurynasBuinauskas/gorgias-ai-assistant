using Copilot.Domain;

namespace Copilot.Gorgias;

public interface IGorgiasTicketClient
{
    /// <summary>Fetches a ticket with its conversation; returns null when the ticket does not exist.</summary>
    Task<TicketContext?> GetTicketAsync(long ticketId, CancellationToken cancellationToken);
}
