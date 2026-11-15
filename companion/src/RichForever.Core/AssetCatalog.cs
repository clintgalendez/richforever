using System.Text.Json;
using System.Text.RegularExpressions;

namespace RichForever.Core;

/// <summary>
/// Maps game data to Discord image values. A value is either an asset key uploaded to the Discord
/// application (lowercase letters, digits, underscores) or an https image URL. The catalog should only
/// list keys that exist in the application; anything not listed falls back to a type badge or the
/// default image.
/// </summary>
public sealed partial class AssetCatalog
{
    private readonly Dictionary<int, string> _zonesByMap = [];
    private readonly Dictionary<string, string> _zonesByName = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> _instancesById = [];
    private readonly Dictionary<string, string> _instancesByName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _classes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _types = new(StringComparer.Ordinal);

    public string? Default { get; private set; }
    public int Count => _zonesByName.Count + _instancesByName.Count + _classes.Count + _types.Count;

    [GeneratedRegex("^[a-z0-9_]{1,32}$")]
    private static partial Regex KeyPattern();

    public static bool IsValidImage(string? value) =>
        value is not null && (KeyPattern().IsMatch(value)
            || (value.Length <= 256 && Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps));

    /// <summary>Case-insensitive, letters and digits only, so "Un'Goro Crater" matches "ungoro crater".</summary>
    public static string Normalize(string name) =>
        string.Concat(name.Where(char.IsLetterOrDigit)).ToUpperInvariant();

    public static AssetCatalog LoadDefault()
    {
        using var stream = typeof(AssetCatalog).Assembly.GetManifestResourceStream("RichForever.Core.catalog.json")
            ?? throw new InvalidOperationException("Embedded asset catalog is missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>Parse catalog JSON. Invalid entries are skipped; malformed JSON throws JsonException.</summary>
    public static AssetCatalog Parse(string json)
    {
        var catalog = new AssetCatalog();
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = document.RootElement;

        if (root.TryGetProperty("default", out var fallback) && IsValidImage(fallback.GetString()))
        {
            catalog.Default = fallback.GetString();
        }
        ReadMap(root, "classes", catalog._classes, static key => key.ToUpperInvariant());
        ReadMap(root, "types", catalog._types, static key => key.ToLowerInvariant());
        ReadEntries(root, "zones", "mapIds", catalog._zonesByMap, catalog._zonesByName);
        ReadEntries(root, "instances", "instanceIds", catalog._instancesById, catalog._instancesByName);
        return catalog;
    }

    public string? ForClass(string? token) => token is null ? null : _classes.GetValueOrDefault(token.ToUpperInvariant());

    public string? ForType(InstanceKind kind) => _types.GetValueOrDefault(kind == InstanceKind.None ? "world" : kind.ToString().ToLowerInvariant());

    public string? ForZone(int? mapId, string? name) => Find(_zonesByMap, _zonesByName, mapId, name);

    public string? ForInstance(int? instanceId, string? name) => Find(_instancesById, _instancesByName, instanceId, name);

    private static string? Find(Dictionary<int, string> byId, Dictionary<string, string> byName, int? id, string? name)
    {
        if (id is int value && byId.TryGetValue(value, out var key)) return key;
        return name is null ? null : byName.GetValueOrDefault(Normalize(name));
    }

    private static void ReadMap(JsonElement root, string property, Dictionary<string, string> target, Func<string, string> normalizeKey)
    {
        if (!root.TryGetProperty(property, out var map) || map.ValueKind != JsonValueKind.Object) return;
        foreach (var item in map.EnumerateObject())
        {
            if (item.Value.ValueKind == JsonValueKind.String && IsValidImage(item.Value.GetString()))
            {
                target[normalizeKey(item.Name)] = item.Value.GetString()!;
            }
        }
    }

    private static void ReadEntries(JsonElement root, string property, string idProperty, Dictionary<int, string> byId, Dictionary<string, string> byName)
    {
        if (!root.TryGetProperty(property, out var list) || list.ValueKind != JsonValueKind.Array) return;
        foreach (var entry in list.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("key", out var keyElement)) continue;
            var key = keyElement.ValueKind == JsonValueKind.String ? keyElement.GetString() : null;
            if (!IsValidImage(key)) continue;
            if (entry.TryGetProperty(idProperty, out var ids) && ids.ValueKind == JsonValueKind.Array)
            {
                foreach (var id in ids.EnumerateArray())
                {
                    if (id.ValueKind == JsonValueKind.Number && id.TryGetInt32(out var number)) byId[number] = key!;
                }
            }
            if (entry.TryGetProperty("names", out var names) && names.ValueKind == JsonValueKind.Array)
            {
                foreach (var name in names.EnumerateArray())
                {
                    if (name.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(name.GetString()))
                    {
                        byName[Normalize(name.GetString()!)] = key!;
                    }
                }
            }
        }
    }
}
