using System.IO;
using System.Text.Json;

namespace WwmRedeem;

// User preferences, kept next to the exe with the other data files.
public sealed class Settings
{
    static readonly string FilePath = Path.Combine(CodeStore.Folder, "wwm-settings.json");

    /// <summary>Beep on Ctrl+Left/Right and after the last code. Off by default.</summary>
    public bool Sounds { get; set; }

    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); }
        catch (IOException) { return new(); }
        catch (UnauthorizedAccessException) { return new(); }
        catch (JsonException) { return new(); }
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this)); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
