using System;

namespace Saham.Infrastructure.Idempotency;

/* Marks an application service method as requiring the Idempotency-Key header.
 * Required on: order create/cancel, driver actions, cash handover,
 * settlement confirm/pay, ledger adjustments and manual refunds (SRS EX-11). */
[AttributeUsage(AttributeTargets.Method)]
public class IdempotentAttribute : Attribute
{
}
