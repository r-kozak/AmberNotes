using System;

namespace AmberNotes.Models;

/// <summary>
/// Represents a single row in the Amber Cloud Explorer list.
/// Produced by the aggregation algorithm (Full Outer Join of cloud ↔ local data).
/// Read-only data object — used only for display.
/// </summary>
public sealed class CloudFileItem
{
    /// <summary>
    /// Human-readable display name.
    /// For notes: note title (or "Нотатка [UUID…]" if title is empty).
    /// For the salt: "Криптографічний якір".
    /// </summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>
    /// Human-friendly file size (e.g. "12.5 КБ", "1.23 МБ").
    /// Shows "-" for LocalOnly items (not yet in cloud, size unknown).
    /// </summary>
    public string SizeFormatted { get; init; } = "-";

    /// <summary>Raw size in bytes. 0 for LocalOnly items.</summary>
    public long SizeBytes { get; init; }

    /// <summary>
    /// Last modification date (cloud modifiedTime, or local UpdatedAt for LocalOnly).
    /// </summary>
    public DateTime ModifiedDate { get; init; }

    /// <summary>Formatted date for display (local time zone, "dd.MM.yyyy HH:mm").</summary>
    public string ModifiedDateFormatted =>
        ModifiedDate == default
            ? "—"
            : ModifiedDate.ToLocalTime().ToString("dd.MM.yyyy HH:mm");

    /// <summary>Sync / audit status of this entry.</summary>
    public CloudItemStatus Status { get; init; }

    // ── Computed presentation helpers ─────────────────────────────────────────

    /// <summary>Emoji icon for the status indicator.</summary>
    public string StatusIcon => Status switch
    {
        CloudItemStatus.Synced        => "🟢",
        CloudItemStatus.CloudOnly     => "🔵",
        CloudItemStatus.LocalOnly     => "🟡",
        CloudItemStatus.PendingDelete => "🔴",
        CloudItemStatus.Tombstone     => "🪦",
        CloudItemStatus.SystemFile    => "🟣",
        _                             => "⚪"
    };

    /// <summary>Short textual label for the status column.</summary>
    public string StatusLabel => Status switch
    {
        CloudItemStatus.Synced        => "Синхронізовано",
        CloudItemStatus.CloudOnly     => "Тільки в хмарі",
        CloudItemStatus.LocalOnly     => "Тільки локально",
        CloudItemStatus.PendingDelete => "Очікує видалення",
        CloudItemStatus.Tombstone     => "Надгробок",
        CloudItemStatus.SystemFile    => "Системний файл",
        _                             => "Невідомий"
    };

    /// <summary>
    /// Tooltip text shown when the user hovers over the status icon.
    /// Provides a clear, non-technical explanation of the current state
    /// and what it means for the user's data.
    /// </summary>
    public string StatusTooltip => Status switch
    {
        CloudItemStatus.Synced =>
            "✅ Синхронізовано\n\n" +
            "Цей файл присутній як у Google Drive, так і на пристрої.\n" +
            "UUID збігаються, нотатка активна (не видалена). Все гаразд.",

        CloudItemStatus.CloudOnly =>
            "🔵 Тільки в хмарі (Очікує Pull)\n\n" +
            "Файл знайдено в Google Drive, але відповідний UUID\n" +
            "відсутній у локальній базі даних.\n" +
            "Нотатка з'явиться на пристрої після наступної синхронізації.",

        CloudItemStatus.LocalOnly =>
            "🟡 Тільки локально (Очікує Push)\n\n" +
            "Нотатка активна на цьому пристрої, але ще не\n" +
            "вивантажена в Google Drive.\n" +
            "Файл з'явиться в хмарі після наступної синхронізації.",

        CloudItemStatus.PendingDelete =>
            "🔴 Очікує видалення в хмарі\n\n" +
            "Нотатку позначено на видалення на цьому пристрої,\n" +
            "але файл у Google Drive містить застарілу версію\n" +
            "(дата хмарного файлу СТАРША за час видалення).\n\n" +
            "Після наступного Push хмарний файл оновиться\n" +
            "з міткою видалення (стане надгробком).",

        CloudItemStatus.Tombstone =>
            "🪦 Надгробок\n\n" +
            "Нотатка видалена і на пристрої, і в хмарі.\n" +
            "Хмарний файл вже містить мітку про видалення.\n\n" +
            "Надгробки зберігаються навмисно — щоб інші пристрої\n" +
            "дізналися про видалення під час синхронізації.",

        CloudItemStatus.SystemFile =>
            "🟣 Системний файл — Криптографічний якір\n\n" +
            "Це файл ambernotes.salt — унікальна 256-бітна сіль,\n" +
            "яка разом із майстер-паролем захищає ваше сховище\n" +
            "через PBKDF2-SHA256 (256 000 ітерацій).\n\n" +
            "Сіль не є секретом (безпека забезпечується паролем),\n" +
            "але критично важлива для доступу до зашифрованих даних.",

        _ => string.Empty
    };
}
