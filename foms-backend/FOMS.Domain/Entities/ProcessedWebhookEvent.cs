using System;

namespace FOMS.Domain.Entities;

/// <summary>
/// Legacy webhook event identifiers retained as historical data after provider integration removal.
/// </summary>
public class ProcessedWebhookEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string EventId { get; set; } = string.Empty;

    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}
