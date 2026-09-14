// A message waiting to be delivered to another system, written in the same transaction as
// the change that caused it.
//
// An award is recorded and its "tell the ERP" message is written together, so either both
// happen or neither does. The portal never waits for the other system to answer, and
// nothing is lost while that system is down: the message sits here until a background job
// picks it up.
//
// OutboxSyncStatus is how far one message has got. New messages are Pending; the
// dispatcher marks them Sent or Failed.
//
// Type names the event and PayloadJson carries it. Both are written once and never
// edited, which is why they are init-only: a queued message is a record of something that
// already happened.

namespace MotsSupplierPortal.Domain.Common;

public enum OutboxSyncStatus
{
    Pending,
    Sent,
    Failed,
}

public sealed class OutboxMessage
{
    public Guid Id { get; init; }
    public required string Type { get; init; }
    public required string PayloadJson { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public OutboxSyncStatus SyncStatus { get; set; } = OutboxSyncStatus.Pending;
    public DateTimeOffset? ProcessedAt { get; set; }
}
