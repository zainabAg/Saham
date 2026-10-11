using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace Saham.Entities.Notifications;

/* Writes business events to the outbox, inside the caller's transaction (SRS I-6). */
public class OutboxManager : DomainService
{
    private readonly IRepository<OutboxEvent, long> _outboxRepository;

    public OutboxManager(IRepository<OutboxEvent, long> outboxRepository)
    {
        _outboxRepository = outboxRepository;
    }

    /* The key is "{type}:{aggregate}" for events that happen once per aggregate (ORDER_PICKED_UP);
     * events that may repeat (a second driver assigned, an item running out twice) pass a discriminator. */
    public async Task EnqueueAsync(string eventType, string aggregateType, Guid aggregateId,
        IReadOnlyDictionary<string, string?> payload, string? discriminator = null)
    {
        var key = discriminator == null ? $"{eventType}:{aggregateId:N}" : $"{eventType}:{aggregateId:N}:{discriminator}";
        await _outboxRepository.InsertAsync(new OutboxEvent(eventType, key, aggregateType, aggregateId, payload, Clock.Now));
    }
}
