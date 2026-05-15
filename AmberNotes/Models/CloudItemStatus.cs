namespace AmberNotes.Models;

/// <summary>
/// Represents the sync status of a single entry in the Amber Cloud Explorer.
/// Each value corresponds to a distinct visual indicator and user-facing explanation.
/// </summary>
public enum CloudItemStatus
{
    /// <summary>
    /// 🟢 Synced.
    /// File exists in Google Drive AND the note is active locally (is_deleted = false,
    /// UUID matches). Everything is in order.
    /// </summary>
    Synced,

    /// <summary>
    /// 🔵 Cloud Only (Awaiting Pull).
    /// File found in Google Drive, but its UUID is absent from the local database.
    /// The note will appear locally after the next sync.
    /// </summary>
    CloudOnly,

    /// <summary>
    /// 🟡 Local Only (Awaiting Push).
    /// Note is active on this device (is_deleted = false) but no cloud file found yet.
    /// Will be uploaded during the next sync.
    /// </summary>
    LocalOnly,

    /// <summary>
    /// 🔴 Pending Delete (Awaiting Push).
    /// Note is soft-deleted locally (is_deleted = true), but the cloud file's modifiedTime
    /// is OLDER than the local deletion timestamp. The tombstone has not been pushed yet.
    /// </summary>
    PendingDelete,

    /// <summary>
    /// 🪦 Tombstone.
    /// Note is soft-deleted locally AND the cloud file's modifiedTime is newer than
    /// the local deletion time — meaning the cloud already holds the tombstone marker.
    /// </summary>
    Tombstone,

    /// <summary>
    /// 🟣 System File.
    /// The cryptographic salt file (ambernotes.salt). Special non-note entry used by PBKDF2.
    /// </summary>
    SystemFile
}
