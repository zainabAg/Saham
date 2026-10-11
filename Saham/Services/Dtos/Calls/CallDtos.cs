using System;
using System.Collections.Generic;
using Saham.Entities.Support;

namespace Saham.Services.Dtos.Calls;

/* An in-app call (TD-03). Deliberately no phone number anywhere: the apps connect through WebRTC
 * with these ICE servers and exchange signalling over OrdersHub.SendCallSignal. */
public class CallSessionDto
{
    public Guid CallId { get; set; }

    public Guid OrderId { get; set; }

    public long OrderNumber { get; set; }

    public ContactOutcome Status { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? AnsweredAt { get; set; }

    public DateTime? EndedAt { get; set; }

    public int? DurationSeconds { get; set; }

    public int RingTimeoutSeconds { get; set; }

    public List<IceServerDto> IceServers { get; set; } = new();
}

public class IceServerDto
{
    public List<string> Urls { get; set; } = new();

    public string? Username { get; set; }

    public string? Credential { get; set; }
}

public class EndCallDto
{
    /* The audio never connected (no ICE path): recorded as a technical failure. */
    public bool Failed { get; set; }
}
