using System.IO;
using System.Text.Json;

namespace LaundryPractice;

public sealed class Settings
{
    public string Theme { get; set; } = "Light";
    public int FailedLogins { get; set; }
    public static Settings Current { get; private set; } = new();
    public static string FileName => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaundryPractice", "settings.json");

    public static void Load()
    {
        try { Current = File.Exists(FileName) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(FileName)) ?? new() : new(); }
        catch (JsonException) { Current = new(); }
        Current.Theme = Current.Theme == "Dark" ? "Dark" : "Light";
        Current.FailedLogins = Math.Clamp(Current.FailedLogins, 0, 100);
    }

    public static void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FileName)!);
        File.WriteAllText(FileName, JsonSerializer.Serialize(Current));
    }
}
