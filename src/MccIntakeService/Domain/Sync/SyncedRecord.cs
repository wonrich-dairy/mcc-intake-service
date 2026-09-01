namespace MccIntakeService.Domain.Sync;

/// <summary>The kinds of record an officer can capture offline and upload later (SCRUM-10).</summary>
public enum SyncOperationKind
{
    /// <summary>Register an arriving consignment at the gate (SCRUM-6).</summary>
    RegisterConsignment = 0,

    /// <summary>Record the gate quality test panel and its verdict (SCRUM-7).</summary>
    RecordQualityTest = 1,

    /// <summary>Pour an accepted consignment into a chilling tank (SCRUM-52).</summary>
    PourToTank = 2
}

/// <summary>
/// A record the client captured offline and has since uploaded (SCRUM-10). Kept so a replayed
/// queue applies once, however many times the client retries.
/// </summary>
/// <remarks>
/// <para>
/// A handheld that loses connectivity mid-upload cannot know whether the server took the record.
/// Its only safe move is to send the queue again, so the server has to be the side that
/// remembers. The client's own identifier for the record is what makes that possible.
/// </para>
/// <para>
/// The reference the operation produced is stored alongside, so a replay can return the same
/// answer the first attempt did rather than a bare "already applied" the client cannot act on.
/// </para>
/// </remarks>
public class SyncedRecord
{
    /// <summary>EF Core materialisation constructor.</summary>
    private SyncedRecord()
    {
        ClientRecordId = string.Empty;
    }

    public SyncedRecord(
        Guid id,
        string clientRecordId,
        SyncOperationKind kind,
        string? resultReference,
        string? syncedBy,
        DateTimeOffset syncedAtUtc)
    {
        Id = id;
        ClientRecordId = clientRecordId.Trim();
        Kind = kind;
        ResultReference = resultReference;
        SyncedBy = syncedBy;
        SyncedAtUtc = syncedAtUtc.UtcDateTime;
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// The identifier the client gave this record when it was captured offline. Unique across the
    /// system, and the thing that makes an upload idempotent.
    /// </summary>
    public string ClientRecordId { get; private set; }

    public SyncOperationKind Kind { get; private set; }

    /// <summary>Reference the operation produced, replayed verbatim on a duplicate upload.</summary>
    public string? ResultReference { get; private set; }

    /// <summary>Identity of the officer whose queue this came from.</summary>
    public string? SyncedBy { get; private set; }

    public DateTime SyncedAtUtc { get; private set; }
}
