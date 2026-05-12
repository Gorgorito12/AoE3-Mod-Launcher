using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WarsOfLibertyLauncher.Models;

/// <summary>
/// One entry in the catalog's news.json feed. Rendered as a card in the
/// Noticias tab. All fields except <see cref="Title"/> and <see cref="PublishedAt"/>
/// are optional, so a minimal entry is just a heading and a timestamp.
/// </summary>
public class NewsItem
{
    /// <summary>Stable per-entry id used to deduplicate when "seen" is tracked later.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>Headline shown at the top of the card.</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    /// <summary>
    /// ISO-8601 timestamp (e.g. "2026-05-12T10:00:00Z"). Used to sort the
    /// feed newest-first and to format the relative "5 days ago" line.
    /// </summary>
    [JsonPropertyName("publishedAt")]
    public string PublishedAt { get; set; } = "";

    /// <summary>Short summary body, plain text. One paragraph max.</summary>
    [JsonPropertyName("body")]
    public string Body { get; set; } = "";

    /// <summary>
    /// Optional "Read more" link rendered as a footer button on the card.
    /// HTTP and HTTPS only; anything else gets dropped at render time.
    /// </summary>
    [JsonPropertyName("link")]
    public string? Link { get; set; }

    /// <summary>
    /// Optional per-language overrides for Title/Body. Keyed by ISO 639-1
    /// ("en", "es", …). Resolved against the user's UI language at render
    /// time, falling back to the top-level Title/Body when the requested
    /// language isn't present.
    /// </summary>
    [JsonPropertyName("locale")]
    public Dictionary<string, NewsItemLocale>? Locale { get; set; }

    /// <summary>
    /// Optional mod-id filter: when set, the entry only renders if the user's
    /// active mod is in the list. Empty / null means "show for all mods".
    /// </summary>
    [JsonPropertyName("modIds")]
    public List<string>? ModIds { get; set; }
}

public class NewsItemLocale
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("body")]
    public string Body { get; set; } = "";
}

/// <summary>
/// Wire format of news.json — a list of entries plus a schema version so
/// the client can refuse a future incompatible format.
/// </summary>
public class NewsFeed
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("items")]
    public List<NewsItem> Items { get; set; } = new();
}
