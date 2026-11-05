using System.Text.Json;
using NUnit.Framework;
using TinyBench.Core;
using TinyBench.Sdk.RestHost.Services;

namespace TinyBench.Tests;

public class TableTransportTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new TinyTableJsonConverter() }
    };

    [Test]
    public void TableRoundTripPreservesSchemaScalarTypesAndNulls()
    {
        var table = new TinyTable(
            [new("text", TinyValueKind.String), new("flag", TinyValueKind.Boolean),
             new("count", TinyValueKind.Integer), new("signal", TinyValueKind.Float, true)],
            [["A1", true, 1, 0.25], ["A2", false, 2, null]]);
        var json = JsonSerializer.SerializeToElement(table, Options);
        Assert.That(json.GetProperty("columns")[3].GetProperty("kind").GetString(), Is.EqualTo("Float"));
        var result = (TinyTable)TinyJsonInputs.Convert(typeof(TinyTable), json)!;
        Assert.That(result.Columns, Is.EqualTo(table.Columns));
        Assert.That(result.Rows, Is.EqualTo(table.Rows));
        Assert.That(result.Rows[0][2], Is.TypeOf<int>());
        Assert.That(result.Rows[0][3], Is.TypeOf<double>());
    }

    [TestCase("null")]
    [TestCase("{}")]
    [TestCase("{\"columns\":[{\"name\":\"x\",\"kind\":\"Table\"}],\"rows\":[]}")]
    [TestCase("{\"columns\":[{\"name\":\"x\",\"kind\":\"2\"}],\"rows\":[]}")]
    [TestCase("{\"columns\":[{\"name\":\"x\",\"kind\":\"Integer\"}],\"rows\":[[null]]}")]
    [TestCase("{\"columns\":[{\"name\":\"x\",\"kind\":\"Integer\"}],\"rows\":[[1.5]]}")]
    [TestCase("{\"columns\":[{\"name\":\"x\",\"kind\":\"Integer\"}],\"rows\":[[\"1\"]]}")]
    [TestCase("{\"columns\":[{\"name\":\"x\",\"kind\":\"Integer\"}],\"rows\":[[[1]]]}")]
    [TestCase("{\"columns\":[{\"name\":\"x\",\"kind\":\"Integer\"}],\"rows\":[[]]}")]
    [TestCase("{\"columns\":[{\"name\":\"x\",\"kind\":\"Integer\"}],\"rows\":[[1,2]]}")]
    [TestCase("{\"columns\":[{\"name\":\"x\",\"kind\":\"String\"},{\"name\":\"x\",\"kind\":\"String\"}],\"rows\":[]}")]
    public void InvalidJsonTablesAreRejected(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Throws<JsonException>(() => TinyJsonInputs.Convert(typeof(TinyTable), document.RootElement));
    }

    [Test]
    public void ScalarAndArrayInputsRetainTheirCSharpTypes()
    {
        Assert.That(TinyJsonInputs.Convert(typeof(float), JsonSerializer.SerializeToElement(1.5)), Is.TypeOf<float>());
        Assert.That(TinyJsonInputs.Convert(typeof(double), JsonSerializer.SerializeToElement(1.5)), Is.TypeOf<double>());
        Assert.That(TinyJsonInputs.Convert(typeof(int?), JsonSerializer.SerializeToElement(1)), Is.EqualTo(1));
        Assert.That(TinyJsonInputs.Convert(typeof(int?), JsonSerializer.SerializeToElement<object?>(null)), Is.Null);
        Assert.That(TinyJsonInputs.Convert(typeof(string), JsonSerializer.SerializeToElement<object?>(null)), Is.Null);
        Assert.That(TinyJsonInputs.Convert(typeof(double[]), JsonSerializer.SerializeToElement(new[] { 1.5, 2.5 })),
            Is.EqualTo(new[] { 1.5, 2.5 }));
        Assert.Throws<JsonException>(() => TinyJsonInputs.Convert(typeof(string[]), JsonSerializer.SerializeToElement(new string?[] { null })));
    }
}
