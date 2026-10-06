// ======================================================================
// StateStore.cs — saving and loading SpaceKeeper's data
// ======================================================================
// Everything in SavedState (pins, list order, settings) is written as a
// JSON text file:
//     %LOCALAPPDATA%\SpaceKeeper\state.json
// (on most PCs: C:\Users\<you>\AppData\Local\SpaceKeeper\state.json)
//
// Desktop NAMES are not stored here — Windows 11 stores those itself.
// Used by MainViewModel: Load() once at start-up, Save() after each change.
// ======================================================================

using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpaceKeeper.Core;

public sealed class StateStore(string? filePath = null)
{
    /// <summary>Where the file lives. Tests pass their own path.</summary>
    public string FilePath { get; } = filePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SpaceKeeper",
        "state.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // Store enums as words ("BottomLeft") rather than numbers, so the file is readable.
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Loads the saved state, or a fresh default one if there isn't one (or it's damaged).</summary>
    public SavedState Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new SavedState();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<SavedState>(json, Options) ?? new SavedState();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A damaged file shouldn't stop the app from starting.
            return new SavedState();
        }
    }

    /// <summary>
    /// Saves the state. Writes to a temporary file first and then swaps it in,
    /// so a crash mid-save can never leave a half-written file behind.
    /// </summary>
    public void Save(SavedState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(state, Options));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Saving is best-effort; the app keeps working with what's in memory.
        }
    }
}
