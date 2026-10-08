using System.IO;
using System.Text.Json;

namespace ModpackMerger.App.Services;

public sealed class AppSettings
{
    /// <summary>Clé API CurseForge optionnelle (https://console.curseforge.com) pour obtenir les noms exacts des mods.</summary>
    public string? CurseForgeApiKey { get; set; }

    public string? LastTargetFolder { get; set; }
}

/// <summary>Persistance des réglages dans %AppData%\ModpackMerger\settings.json.</summary>
public sealed class SettingsService
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ModpackMerger", "settings.json");

    public AppSettings Current { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Current = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Réglages non critiques : on ignore l'échec d'écriture.
        }
    }
}
