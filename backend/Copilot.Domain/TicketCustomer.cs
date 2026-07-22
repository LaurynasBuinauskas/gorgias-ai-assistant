namespace Copilot.Domain;

/// <summary>The minimal customer identity a draft needs — never the full profile.</summary>
public sealed record TicketCustomer(string? Name, string? Email);
