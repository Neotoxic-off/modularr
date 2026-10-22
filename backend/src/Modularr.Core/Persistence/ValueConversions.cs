using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Modularr.Core.Languages;
using Modularr.Core.Qualities;

namespace Modularr.Core.Persistence;

// Quality and Language are stored by id everywhere, in plain columns and inside JSON columns; ReleaseVersion by name.
public sealed class QualityIdConverter() : ValueConverter<Quality, int>(q => q.Id, id => Quality.FindById(id));

public sealed class LanguageIdConverter() : ValueConverter<Language, int>(l => l.Id, id => Language.FindById(id));

public sealed class ReleaseVersionNameConverter() : ValueConverter<ReleaseVersion, string>(v => v.Name, name => ReleaseVersion.FindByName(name));

// SQLite keeps no DateTimeKind: everything is written as UTC and read back as UTC.
public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : DateTime.SpecifyKind(v, DateTimeKind.Utc),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

public sealed class JsonValueConverter<T>() : ValueConverter<T, string>(v => PersistenceJson.Serialize(v), v => PersistenceJson.Deserialize<T>(v));

// Compares the stored form, so in-place edits (a dictionary) and non-identity fields (Revision.IsRepack) are detected.
public sealed class JsonValueComparer<T>() : ValueComparer<T>(
    (a, b) => PersistenceJson.Serialize(a) == PersistenceJson.Serialize(b),
    v => StringComparer.Ordinal.GetHashCode(PersistenceJson.Serialize(v)),
    v => PersistenceJson.Deserialize<T>(PersistenceJson.Serialize(v)));

public static class PersistenceJson
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new LanguageJson(), new QualityModelJson() } };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException($"Null {typeof(T).Name} in a non-null column.");

    // Detection sources describe how a title was parsed, not the file: not stored, so equal models store equal JSON.
    private sealed class QualityModelJson : JsonConverter<QualityModel>
    {
        public override QualityModel Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            Stored stored = JsonSerializer.Deserialize<Stored>(ref reader, options) ?? throw new JsonException("Null quality.");
            return new QualityModel(stored.Quality, stored.Revision);
        }

        public override void Write(Utf8JsonWriter writer, QualityModel value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, new Stored(value.Quality, value.Revision), options);

        private sealed record Stored(Quality Quality, Revision Revision);
    }

    // Ids, where the API's own converter writes names.
    private sealed class LanguageJson : JsonConverter<Language>
    {
        public override Language Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Language.FindById(reader.GetInt32());

        public override void Write(Utf8JsonWriter writer, Language value, JsonSerializerOptions options) => writer.WriteNumberValue(value.Id);
    }
}
