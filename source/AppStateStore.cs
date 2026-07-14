using System;
using System.IO;
using System.Text.Json;

namespace ProjectStructureSummary;

public sealed class AppState
{
    public string? LastFolder { get; set; }
}

public static class AppStateStore
{
    // quick win: state in LocalAppData, without Settings.Designer and other fuss
    private static readonly string AppDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ProjectStructureSummary");

    private static readonly string StatePath = Path.Combine(AppDir, "state.json");

    public static AppState Load()
    {
        try
        {
            if (!File.Exists(StatePath))
                return new AppState();

            var json = File.ReadAllText(StatePath);
            return JsonSerializer.Deserialize<AppState>(json) ?? new AppState();
        }
        catch
        {
            // if state is corrupted - just start with an empty one, no drama
            return new AppState();
        }
    }

    public static void SaveLastFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(AppDir);

            var state = new AppState { LastFolder = folder };
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(StatePath, json);
        }
        catch
        {
            // here we could log, but for a utility it's fine as is
        }
    }
}
