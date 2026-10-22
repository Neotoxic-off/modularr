using System.Globalization;
using System.Text.RegularExpressions;

namespace Modularr.Core.Indexers.Cardigann;

// Prowlarr CardigannBase.ApplyGoTemplateText: the subset of Go templates definitions use, evaluated in upstream order
// (re_replace, join, and/or/eq/ne, if/else/end, range, variables). Values are strings, string lists or null.
public static partial class CardigannTemplate
{
    /// <param name="modifier">Applied to every substituted value (e.g. URL encoding for search paths).</param>
    /// <exception cref="NotSupportedException">An if condition that is not a variable.</exception>
    public static string? Apply(string? template, IReadOnlyDictionary<string, object?> variables, Func<string?, string?>? modifier = null)
    {
        ArgumentNullException.ThrowIfNull(variables);
        if (string.IsNullOrWhiteSpace(template) || !template.Contains("{{", StringComparison.Ordinal))
        {
            return template;
        }

        modifier ??= value => value;

        // {{ re_replace .Query.Keywords "[^a-zA-Z0-9]+" "%" }}
        template = ReReplaceRegex().Replace(template, match =>
            modifier(Regex.Replace(Get(variables, match.Groups[1].Value) as string ?? "", match.Groups[2].Value, match.Groups[3].Value)) ?? "");

        // {{ join .Categories "," }}
        template = JoinRegex().Replace(template, match =>
            modifier(string.Join(match.Groups[2].Value, List(variables, match.Groups[1].Value))) ?? "");

        template = ApplyLogicFunctions(template, variables);

        template = IfElseRegex().Replace(template, match =>
        {
            string condition = match.Groups[1].Value;
            if (!condition.StartsWith('.'))
            {
                throw new NotSupportedException($"CardigannIndexer: Condition operation '{condition}' not implemented");
            }

            bool isTrue = Get(variables, condition) switch
            {
                null => false,
                string value => !string.IsNullOrWhiteSpace(value),
                IEnumerable<string> values => values.Any(),
                object other => throw new NotSupportedException($"Unexpected type for variable {condition}: {other.GetType()}"),
            };
            return isTrue ? match.Groups[2].Value : match.Groups[3].Value;
        });

        // {{ range .Categories }}&cat[]={{.}}{{end}}, optionally with an index: {{ range $i, $e := .Categories }}[{{$i}}]
        template = RangeRegex().Replace(template, match =>
        {
            string index = "{{" + match.Groups["index"].Value + "}}";
            bool hasIndex = match.Groups["index"].Length > 0;
            return string.Concat(List(variables, match.Groups["variable"].Value).Select((value, i) =>
            {
                string position = i.ToString(CultureInfo.InvariantCulture);
                string prefix = hasIndex ? match.Groups["prefix"].Value.Replace(index, position, StringComparison.Ordinal) : match.Groups["prefix"].Value;
                string postfix = hasIndex ? match.Groups["postfix"].Value.Replace(index, position, StringComparison.Ordinal) : match.Groups["postfix"].Value;
                return prefix + modifier(value) + postfix;
            }));
        });

        return VariableRegex().Replace(template, match => modifier(Get(variables, match.Groups[1].Value)?.ToString()) ?? "");
    }

    // and: first empty argument, else the last; or: first non-empty, else the last; eq/ne: .True or .False.
    // The result replaces the call, so functions nest: {{ if and eq (.A) "x" eq (.B) "y" }}.
    private static string ApplyLogicFunctions(string template, IReadOnlyDictionary<string, object?> variables)
    {
        Match match = LogicFunctionRegex().Match(template);
        while (match.Success)
        {
            int start = match.Index;
            int length = match.Length;
            string function = match.Groups[1].Value;
            List<string> parameters = [.. match.Groups[2].Captures.Select(capture => capture.Value.Trim('(', ')'))];
            string result = "";

            if (function is "and" or "or")
            {
                // and/or ignore string literals.
                parameters.RemoveAll(parameter => parameter.StartsWith('"'));
                bool isAnd = function == "and";
                foreach (string parameter in parameters)
                {
                    result = parameter;
                    if (string.IsNullOrWhiteSpace(Get(variables, parameter) as string) == isAnd)
                    {
                        break;
                    }
                }
            }
            else
            {
                // eq/ne take exactly two arguments; the rest of the match stays in the template.
                if (parameters.Count > 2)
                {
                    length = match.Groups[2].Captures[2].Index - start;
                }

                bool isEqual = parameters.Take(2).Select(parameter => parameter.StartsWith('"') ? parameter.Trim('"') : Get(variables, parameter) as string).Distinct().Count() == 1;
                result = isEqual == (function == "eq") ? ".True" : ".False";
            }

            template = template.Remove(start, length).Insert(start, result);
            match = LogicFunctionRegex().Match(template);
        }

        return template;
    }

    // shortcut: an unknown variable reads as empty where upstream throws, so a definition typo blanks a value instead of failing the row.
    private static object? Get(IReadOnlyDictionary<string, object?> variables, string name) => variables.GetValueOrDefault(name);

    private static IEnumerable<string> List(IReadOnlyDictionary<string, object?> variables, string name) => Get(variables, name) as IEnumerable<string> ?? [];

    [GeneratedRegex(@"{{\s*re_replace\s+(\..+?)\s+""(.*?)""\s+""(.*?)""\s*}}")]
    private static partial Regex ReReplaceRegex();

    [GeneratedRegex(@"{{\s*join\s+(\..+?)\s+""(.*?)""\s*}}")]
    private static partial Regex JoinRegex();

    [GeneratedRegex(@"\b(and|or|eq|ne)(?:\s+(\(?\.[^\)\s]+\)?|""[^""]+"")){2,}")]
    private static partial Regex LogicFunctionRegex();

    [GeneratedRegex(@"{{\s*if\s*(.+?)\s*}}(.*?){{\s*else\s*}}(.*?){{\s*end\s*}}")]
    private static partial Regex IfElseRegex();

    [GeneratedRegex(@"{{\s*range\s*(((?<index>\$.+?),)((\s*(?<element>.+?)\s*(:=)\s*)))?(?<variable>.+?)\s*}}(?<prefix>.*?){{\.}}(?<postfix>.*?){{end}}")]
    private static partial Regex RangeRegex();

    [GeneratedRegex(@"{{\s*(\..+?)\s*}}")]
    private static partial Regex VariableRegex();
}
