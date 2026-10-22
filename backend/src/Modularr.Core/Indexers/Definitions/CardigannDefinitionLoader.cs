using System.Collections.Frozen;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Modularr.Core.Indexers.Definitions;

public static class CardigannDefinitionLoader
{
    private static readonly FrozenSet<string> Types = FrozenSet.Create("public", "semi-private", "private");

    // Unknown keys are ignored like Prowlarr does, so newer definitions still load.
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .WithCaseInsensitivePropertyMatching()
        .WithEnforceRequiredMembers()
        .WithEnforceNullability()
        .WithNodeDeserializer(new SearchFieldsDeserializer(), syntax => syntax.OnTop())
        .WithNodeTypeResolver(new ReadOnlyCollectionTypeResolver())
        .Build();

    /// <exception cref="InvalidDataException">The YAML is malformed or is not a valid Cardigann definition.</exception>
    public static CardigannDefinition Load(string yaml)
    {
        CardigannDefinition? definition;
        try
        {
            definition = Deserializer.Deserialize<CardigannDefinition?>(yaml);
        }
        catch (YamlException ex)
        {
            throw new InvalidDataException($"Invalid indexer definition: {(ex.InnerException ?? ex).Message}", ex);
        }

        if (definition is null)
        {
            throw new InvalidDataException("Invalid indexer definition: empty document.");
        }

        Validate(definition);

        // A single search path is shorthand for one inheriting entry in paths.
        return definition.Search.Path is null
            ? definition
            : definition with { Search = definition.Search with { Paths = [.. definition.Search.Paths, new SearchPathBlock { Path = definition.Search.Path }] } };
    }

    private static void Validate(CardigannDefinition definition)
    {
        string? error = definition switch
        {
            _ when string.IsNullOrWhiteSpace(definition.Id) => "id is empty",
            _ when string.IsNullOrWhiteSpace(definition.Name) => "name is empty",
            _ when !Types.Contains(definition.Type) => $"type '{definition.Type}' is not public, semi-private or private",
            { Links.Count: 0 } => "links is empty",
            _ => definition.Links.Concat(definition.LegacyLinks).FirstOrDefault(link => !IsHttpUrl(link)) is { } bad ? $"'{bad}' is not an absolute http(s) URL" : null,
        };

        if (error is not null)
        {
            throw new InvalidDataException($"Invalid indexer definition '{definition.Id}': {error}.");
        }
    }

    private static bool IsHttpUrl(string link) =>
        Uri.TryCreate(link, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    // YamlDotNet only builds mutable collection interfaces; the model exposes read-only ones.
    private sealed class ReadOnlyCollectionTypeResolver : INodeTypeResolver
    {
        public bool Resolve(NodeEvent? nodeEvent, ref Type currentType)
        {
            Type? concrete = currentType.IsGenericType ? currentType.GetGenericTypeDefinition() switch
            {
                Type definition when definition == typeof(IReadOnlyList<>) => typeof(List<>),
                Type definition when definition == typeof(IReadOnlyDictionary<,>) => typeof(Dictionary<,>),
                _ => null,
            } : null;

            if (concrete is null)
            {
                return false;
            }

            currentType = concrete.MakeGenericType(currentType.GetGenericArguments());
            return true;
        }
    }

    // search.fields is a map whose keys may repeat, so it is read as an ordered list of pairs.
    private sealed class SearchFieldsDeserializer : INodeDeserializer
    {
        public bool Deserialize(IParser reader, Type expectedType, Func<IParser, Type, object?> nestedObjectDeserializer, out object? value, ObjectDeserializer rootDeserializer)
        {
            if (expectedType != typeof(List<SearchField>))
            {
                value = null;
                return false;
            }

            reader.Consume<MappingStart>();
            List<SearchField> fields = [];
            while (!reader.TryConsume<MappingEnd>(out _))
            {
                string name = reader.Consume<Scalar>().Value;
                SelectorBlock block = nestedObjectDeserializer(reader, typeof(SelectorBlock)) as SelectorBlock
                    ?? throw new YamlException($"search field '{name}' is empty");
                fields.Add(new SearchField(name, block));
            }

            value = fields;
            return true;
        }
    }
}
