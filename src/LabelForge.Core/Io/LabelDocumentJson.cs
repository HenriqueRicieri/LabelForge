using System.Text.Json;
using System.Text.Json.Serialization;
using LabelForge.Core.Model;

namespace LabelForge.Core.Io;

/// <summary>
/// JSON serialization for label documents. This single format serves two purposes:
/// the .lfl project file on disk and the in-memory undo snapshots, so a document
/// that undoes correctly is also guaranteed to save and reopen correctly.
/// </summary>
public static class LabelDocumentJson
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed record Envelope(int SchemaVersion, LabelDocument Document);

    public static string Serialize(LabelDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(new Envelope(CurrentSchemaVersion, document), Options);
    }

    public static LabelDocument Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        Envelope? envelope = JsonSerializer.Deserialize<Envelope>(json, Options);
        if (envelope?.Document is null)
        {
            throw new JsonException("The file does not contain a label document.");
        }

        if (envelope.SchemaVersion > CurrentSchemaVersion)
        {
            throw new JsonException(
                $"The file uses schema version {envelope.SchemaVersion}; this build supports up to {CurrentSchemaVersion}.");
        }

        return envelope.Document;
    }
}
