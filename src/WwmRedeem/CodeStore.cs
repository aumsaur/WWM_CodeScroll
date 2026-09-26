using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace WwmRedeem;

public sealed record CodeList(IReadOnlyList<string> Codes, DateTimeOffset UpdatedAt, bool FromCache);

// Codes come from codes.yar.gg. The last good response, the codes already pasted
// and the log all live next to the exe, so the app stays portable.
public static class CodeStore
{
    const string ApiUrl = "https://codes.yar.gg/api/codes";

    // Not AppContext.BaseDirectory or Assembly.Location: in a single-file build
    // only the exe's own path reliably points at the folder the user put it in.
    public static readonly string Folder = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
    static readonly string CachePath = Path.Combine(Folder, "wwm-codes-cache.json");
    static readonly string UsedPath = Path.Combine(Folder, "wwm-used.txt");
    static readonly string LogPath = Path.Combine(Folder, "wwm-redeem.log");
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<CodeList> LoadAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("WWM-Redeem/1.0");
            string json = await http.GetStringAsync(ApiUrl);
            var list = Parse(json, fromCache: false);
            TryWrite(() => File.WriteAllText(CachePath, json));
            return list;
        }
        catch (Exception ex) when (File.Exists(CachePath))
        {
            Log("Fetch failed, using the saved list: " + ex.Message);
            return Parse(File.ReadAllText(CachePath), fromCache: true);
        }
    }

    static CodeList Parse(string json, bool fromCache)
    {
        var payload = JsonSerializer.Deserialize<Payload>(json, Json) ?? throw new InvalidDataException("Empty response");
        var codes = payload.Active
            .Select(a => (a.Code ?? "").Trim())
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new CodeList(codes, payload.UpdatedAt, fromCache);
    }

    public static HashSet<string> LoadUsed()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(UsedPath))
            used.UnionWith(File.ReadAllLines(UsedPath).Select(l => l.Trim()).Where(l => l.Length > 0));
        return used;
    }

    public static void MarkUsed(IEnumerable<string> codes) => TryWrite(() => File.AppendAllLines(UsedPath, codes));

    public static void ForgetUsed() => TryWrite(() => File.Delete(UsedPath));

    public static void Log(string message) =>
        TryWrite(() => File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}"));

    // A read-only folder only costs the memory of what was pasted, so don't crash over it.
    static void TryWrite(Action write)
    {
        try { write(); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    sealed class Payload
    {
        public DateTimeOffset UpdatedAt { get; set; }
        public List<Entry> Active { get; set; } = [];
    }

    sealed class Entry
    {
        public string? Code { get; set; }
    }
}
