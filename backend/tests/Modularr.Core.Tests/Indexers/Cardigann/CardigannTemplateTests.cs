// Ported from Prowlarr NzbDrone.Core.Test/IndexerTests/CardigannTests/ApplyGoTemplateTextFixture.cs; logic-function cases added (no upstream fixture).
using System.Globalization;
using Modularr.Core.Indexers.Cardigann;

namespace Modularr.Core.Tests.Indexers.Cardigann;

public sealed class CardigannTemplateTests
{
    private readonly Dictionary<string, object?> _variables = new()
    {
        [".Config.sitelink"] = "https://somesite.com/",
        [".True"] = "True",
        [".False"] = null,
        [".Today.Year"] = DateTime.Today.Year.ToString(CultureInfo.InvariantCulture),
        [".Categories"] = new[] { "tv", "movies" },
    };

    [Theory]
    [InlineData("{{ range .Categories}}&categories[]={{.}}{{end}}", "&categories[]=tv&categories[]=movies")]
    [InlineData("{{ range $i, $e := .Categories}}&categories[{{$i}}]={{.}}{{end}}", "&categories[0]=tv&categories[1]=movies")]
    [InlineData("{{ range $index, $element := .Categories}}&categories[{{$index}}]={{.}}+postIndex[{{$index}}]{{end}}", "&categories[0]=tv+postIndex[0]&categories[1]=movies+postIndex[1]")]
    public void HandlesRangeStatements(string template, string expected)
    {
        Assert.Equal(expected, CardigannTemplate.Apply(template, _variables));
    }

    [Theory]
    [InlineData("{{ re_replace .Query.Keywords \"[^a-zA-Z0-9]+\" \"%\" }}", "abc%def")]
    public void HandlesReReplaceStatements(string template, string expected)
    {
        _variables[".Query.Keywords"] = string.Join(" ", ["abc", "def"]);

        Assert.Equal(expected, CardigannTemplate.Apply(template, _variables));
    }

    [Theory]
    [InlineData("{{ join .Categories \", \" }}", "tv, movies")]
    public void HandlesJoinStatements(string template, string expected)
    {
        Assert.Equal(expected, CardigannTemplate.Apply(template, _variables));
    }

    [Theory]
    [InlineData("{{ .Today.Year }}")]
    public void HandlesVariablesStatements(string template)
    {
        Assert.Equal(DateTime.Now.Year.ToString(CultureInfo.InvariantCulture), CardigannTemplate.Apply(template, _variables));
    }

    [Theory]
    [InlineData("{{if .False }}0{{else}}1{{end}}", "1")]
    [InlineData("{{if .True }}0{{else}}1{{end}}", "0")]
    public void HandlesIfStatements(string template, string expected)
    {
        Assert.Equal(expected, CardigannTemplate.Apply(template, _variables));
    }

    [Theory]
    [InlineData("{{ if and (.Keywords) (eq .Config.disablesort .False) }}sort-{{ else }}{{ end }}", "sort-")]
    [InlineData("{{ if and (.Keywords) (eq .Config.disablesort .True) }}sort-{{ else }}{{ end }}", "")]
    [InlineData("{{ or .Result.a .Result.b .Result.c }}", "b")]
    [InlineData("{{ if eq .Result.type \"web\" }}WEBRip{{ else }}BRRip{{ end }}", "WEBRip")]
    [InlineData("{{ if ne .Result.type \"web\" }}BRRip{{ else }}WEBRip{{ end }}", "WEBRip")]
    [InlineData("{{ if and (and (eq .Result.type \"web\") (ne .Result.b \"YES\")) (.Keywords) }}yes{{ else }}no{{ end }}", "yes")]
    public void HandlesLogicFunctions(string template, string expected)
    {
        _variables[".Keywords"] = "ubuntu";
        _variables[".Config.disablesort"] = null;
        _variables[".Result.a"] = null;
        _variables[".Result.b"] = "b";
        _variables[".Result.c"] = "c";
        _variables[".Result.type"] = "web";

        Assert.Equal(expected, CardigannTemplate.Apply(template, _variables));
    }

    [Fact]
    public void ModifierEncodesSubstitutedValuesOnly()
    {
        _variables[".Keywords"] = "a b&c";

        Assert.Equal("search/a+b%26c/?x", CardigannTemplate.Apply("search/{{ .Keywords }}/?x", _variables, System.Net.WebUtility.UrlEncode));
    }
}
