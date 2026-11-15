using System.Text.Json;
using System.Text.Json.Serialization;

namespace RichForever.Core;

/// <summary>Discord activity as sent with SET_ACTIVITY. Null members are omitted.</summary>
public sealed record Activity
{
    public int Type { get; init; }
    public string? Details { get; init; }
    public string? State { get; init; }
    public ActivityTimestamps? Timestamps { get; init; }
    public ActivityAssets? Assets { get; init; }
    public ActivityParty? Party { get; init; }
    public IReadOnlyList<ActivityButton>? Buttons { get; init; }

    public string ToJson() => JsonSerializer.Serialize(this, DiscordJson.Options);
}

public sealed record ActivityTimestamps(long Start);

public sealed record ActivityAssets
{
    public string? LargeImage { get; init; }
    public string? LargeText { get; init; }
    public string? SmallImage { get; init; }
    public string? SmallText { get; init; }
}

public sealed record ActivityParty(string Id, IReadOnlyList<int> Size);

public sealed record ActivityButton(string Label, string Url);

public static class DiscordJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
