using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AmberNotes.Services;

/// <summary>
/// Persists small application-level flags to a JSON file next to the databases.
///
/// Currently tracks:
///   • PendingCloudWipe — set when the master password was changed while offline.
///     Signals that on the next sync, the cloud must be fully overwritten with
///     the current notes under the new key (skipping the Pull step).
///     Reset ONLY after the entire upload succeeds.
/// </summary>
public class AppSettingsService
{
    private readonly string   _filePath;
    private          Settings _data = new();

    public AppSettingsService(string dataDirectory)
    {
        _filePath = Path.Combine(dataDirectory, "app_settings.json");
        Load();
    }

    // ── Properties ────────────────────────────────────────────────────────────

    public bool PendingCloudWipe
    {
        get => _data.PendingCloudWipe;
        set { _data.PendingCloudWipe = value; Save(); }
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
                _data = JsonSerializer.Deserialize<Settings>(File.ReadAllText(_filePath))
                        ?? new Settings();
        }
        catch
        {
            _data = new Settings();
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_filePath,
                JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* non-critical — best effort */ }
    }

    // ── Data model ────────────────────────────────────────────────────────────

    private sealed class Settings
    {
        [JsonPropertyName("pendingCloudWipe")]
        public bool PendingCloudWipe { get; set; }
    }
}
