using System;
using System.Collections.Generic;

namespace Saham.Services.Dtos.Ops;

/* Phase 6 queue health (no payloads, phone numbers or codes). */
public class NotificationQueueHealthDto
{
    public Dictionary<string, int> NotificationsByStatus { get; set; } = new();

    public int DeadNotifications { get; set; }

    /* Due now (Queued/Failed, NextAttemptAt passed) and the age of the oldest one. */
    public int DueNotifications { get; set; }

    public int? OldestDueAgeSeconds { get; set; }

    /* Processing rows whose worker lease ran out (a worker died or hung). */
    public int ExpiredLeases { get; set; }

    public int UnpublishedEvents { get; set; }

    public int? OldestUnpublishedAgeSeconds { get; set; }

    /* Events the relay gave up on (planning failed MaxPublishAttempts times). */
    public int FailedEvents { get; set; }

    /* This backend instance's worker. */
    public DateTime? WorkerLastRunAt { get; set; }

    public DateTime? WorkerLastErrorAt { get; set; }

    public string? WorkerLastErrorCode { get; set; }

    public int WorkerConsecutiveFailures { get; set; }

    public long WorkerLostLeases { get; set; }
}
