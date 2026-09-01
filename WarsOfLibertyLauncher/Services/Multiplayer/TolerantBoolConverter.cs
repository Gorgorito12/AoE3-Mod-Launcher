using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Reads a boolean that may arrive as a JSON number, because the lobby API is backed by SQLite
/// and SQLite has no boolean type.
///
/// <para><b>Why this is not defensive programming for its own sake.</b> A column stored as
/// <c>INTEGER</c> and handed to the client without coercion arrives as <c>0</c> or <c>1</c>.
/// <see cref="JsonSerializer"/> refuses to bind a number to a <c>bool</c> — it throws — and the
/// throw does not skip the field, it aborts the ENTIRE response. So one integer takes down a
/// whole page of data that was otherwise perfectly readable.</para>
///
/// <para>That is not hypothetical: launcher 1.0.13l added <c>rated</c> to the match-history row
/// on both sides, the server selected the raw column, the DTO declared <c>bool?</c>, and the
/// Profile's History section showed "Loading…" for ever — with the real error stored and never
/// drawn. The server coerces that field now; this converter is what stops the NEXT column
/// somebody exposes from doing it again, which is why it is registered on the shared options
/// rather than on a single property.</para>
///
/// <para>Deliberately narrow: it accepts <c>true</c>/<c>false</c> and the numbers a database
/// produces, and nothing else. A string "true" is NOT accepted — that would be inventing a
/// convention this API does not have, and quietly accepting garbage is how a contract stops
/// meaning anything. Writing always emits a real JSON boolean.</para>
/// </summary>
internal sealed class TolerantBoolConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => ReadBool(ref reader);

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
        => writer.WriteBooleanValue(value);

    /// <summary>
    /// The shared rule. Any non-zero number is true, matching how every SQL dialect reads a
    /// numeric truth value — and matching what the server itself does when it coerces.
    /// </summary>
    internal static bool ReadBool(ref Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.True => true,
        JsonTokenType.False => false,
        JsonTokenType.Number => reader.TryGetInt64(out var n)
            ? n != 0
            : reader.GetDouble() != 0,
        _ => throw new JsonException(
            $"Expected a boolean or a number for a boolean field, got {reader.TokenType}."),
    };
}

/// <summary>
/// The nullable half. A separate converter because <see cref="JsonConverter{T}"/> is matched by
/// exact type — registering only the non-nullable one leaves every <c>bool?</c> on the original
/// throwing path, which is precisely where the bug lived (<c>rated</c> is <c>bool?</c>).
///
/// <para><b>Null is preserved, never folded into false.</b> On this API a null <c>rated</c> means
/// the row predates the migration that added the column: "we don't know", which is a different
/// claim from "it did not count". Flattening it would make every old match assert it was
/// unrated.</para>
/// </summary>
internal sealed class TolerantNullableBoolConverter : JsonConverter<bool?>
{
    public override bool? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.Null ? null : TolerantBoolConverter.ReadBool(ref reader);

    public override void Write(Utf8JsonWriter writer, bool? value, JsonSerializerOptions options)
    {
        if (value.HasValue) writer.WriteBooleanValue(value.Value);
        else writer.WriteNullValue();
    }
}
