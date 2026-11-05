using NUnit.Framework;
using TinyBench.Core;
using TinyBench.Sdk.Core;

namespace TinyBench.Tests;

public class ValueModelTests
{
    [Test]
    public void SharedClrRegistryCannotBeMutated() =>
        Assert.Throws<NotSupportedException>(() => ((IDictionary<Type, TinyType>)TinyType.ClrTypes).Clear());

    [TestCase(typeof(string), TinyValueKind.String)]
    [TestCase(typeof(bool), TinyValueKind.Boolean)]
    [TestCase(typeof(int), TinyValueKind.Integer)]
    [TestCase(typeof(float), TinyValueKind.Float)]
    [TestCase(typeof(double), TinyValueKind.Float)]
    [TestCase(typeof(TinyTable), TinyValueKind.Table)]
    public void SdkMapsToSharedKinds(Type type, TinyValueKind kind) =>
        Assert.That(TinyTypeRules.Describe(type), Is.EqualTo(new TinyType(kind)));

    [TestCase(typeof(string[]), TinyValueKind.String)]
    [TestCase(typeof(bool[]), TinyValueKind.Boolean)]
    [TestCase(typeof(int[]), TinyValueKind.Integer)]
    [TestCase(typeof(float[]), TinyValueKind.Float)]
    [TestCase(typeof(double[]), TinyValueKind.Float)]
    public void ArrayDescriptionsIncludeScalarElementKind(Type type, TinyValueKind kind) =>
        Assert.That(TinyTypeRules.Describe(type), Is.EqualTo(new TinyType(TinyValueKind.Array, kind)));

    [TestCase(typeof(object))]
    [TestCase(typeof(decimal))]
    [TestCase(typeof(DateTime))]
    [TestCase(typeof(object[]))]
    [TestCase(typeof(int[][]))]
    [TestCase(typeof(int[,]))]
    [TestCase(typeof(int?[]))]
    [TestCase(typeof(TinyTable[]))]
    public void SdkRejectsValuesOutsideTheModel(Type type)
    {
        Assert.That(TinyTypeRules.IsAllowed(type), Is.False);
        Assert.Throws<ArgumentOutOfRangeException>(() => TinyTypeRules.Describe(type));
    }

    [Test]
    public void TypeDescriptionsRejectInvalidCombinations()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TinyType((TinyValueKind)99));
        Assert.Throws<ArgumentException>(() => new TinyType(TinyValueKind.Array));
        Assert.Throws<ArgumentException>(() => new TinyType(TinyValueKind.Array, TinyValueKind.Table));
        Assert.Throws<ArgumentException>(() => new TinyType(TinyValueKind.Array, TinyValueKind.Array));
        Assert.Throws<ArgumentException>(() => new TinyType(TinyValueKind.String, TinyValueKind.String));
    }

    [Test]
    public void ArraysRequireHomogeneousTypedScalarsWithoutNulls()
    {
        var strings = new TinyType(TinyValueKind.Array, TinyValueKind.String);
        Assert.That(strings.Accepts(Array.Empty<string>()), Is.True);
        Assert.That(strings.Accepts(new[] { "a", "b" }), Is.True);
        Assert.That(strings.Accepts(new string?[] { "a", null }), Is.False);
        Assert.That(strings.Accepts(new object[] { "a", "b" }), Is.False);
        Assert.That(strings.Accepts(new[] { 1, 2 }), Is.False);
        Assert.That(strings.Accepts(new string[0, 0]), Is.False);
    }

    [Test]
    public void NumbersPreserveIntegerAndFloatDistinction()
    {
        Assert.That(new TinyType(TinyValueKind.Integer).Accepts(1), Is.True);
        Assert.That(new TinyType(TinyValueKind.Integer).Accepts(1.0), Is.False);
        var floating = new TinyType(TinyValueKind.Float);
        Assert.That(floating.Accepts(1.0), Is.True);
        Assert.That(floating.Accepts(1.0f), Is.True);
        Assert.That(floating.Accepts(1), Is.False);
        Assert.That(floating.Accepts(double.NaN), Is.False);
        Assert.That(floating.Accepts(float.PositiveInfinity), Is.False);
    }

    [Test]
    public void TablesSupportAllScalarsAndExplicitNullCells()
    {
        var table = new TinyTable(
            [new("well", TinyValueKind.String), new("count", TinyValueKind.Integer),
             new("valid", TinyValueKind.Boolean), new("signal", TinyValueKind.Float, true)],
            [["A1", 1, true, 0.25], ["A2", 2, false, null]]);
        Assert.That(table.Rows[1][3], Is.Null);
        Assert.That(new TinyType(TinyValueKind.Table).Accepts(table), Is.True);
        Assert.That(table.Columns[0].IsNullable, Is.False);
    }

    [Test]
    public void NullabilityIsPerColumnAndDoesNotWeakenItsType()
    {
        Assert.Throws<ArgumentException>(() => new TinyTable([new("x", TinyValueKind.Integer)], [[null]]));
        Assert.Throws<ArgumentException>(() => new TinyTable([new("x", TinyValueKind.Integer, true)], [["1"]]));
        Assert.Throws<ArgumentException>(() => new TinyTable([new("x", TinyValueKind.Integer, true)], [[1.5]]));
        Assert.DoesNotThrow(() => new TinyTable([new("x", TinyValueKind.Integer, true)], [[null], [1]]));
    }

    [Test]
    public void TablesRejectMalformedSchemasRowsAndNestedCells()
    {
        Assert.Throws<ArgumentException>(() => new TinyTableColumn(" ", TinyValueKind.String));
        Assert.Throws<ArgumentException>(() => new TinyTableColumn("x", TinyValueKind.Array));
        Assert.Throws<ArgumentException>(() => new TinyTableColumn("x", TinyValueKind.Table));
        Assert.Throws<ArgumentException>(() => new TinyTable([new("x", TinyValueKind.String), new("x", TinyValueKind.Integer)], []));
        Assert.Throws<ArgumentException>(() => new TinyTable([new("x", TinyValueKind.Integer)], [[1, 2]]));
        Assert.Throws<ArgumentException>(() => new TinyTable([new("x", TinyValueKind.Integer)], [[]]));
        Assert.Throws<ArgumentException>(() => new TinyTable([new("x", TinyValueKind.Integer)], [[new[] { 1 }]]));
        Assert.Throws<ArgumentException>(() => new TinyTable([new("x", TinyValueKind.Integer)], [[new TinyTable([], [])]]));
    }

    [Test]
    public void TableCopiesInputsAndExposesReadOnlyCollections()
    {
        var columns = new[] { new TinyTableColumn("x", TinyValueKind.Integer) };
        object?[] row = [1];
        var rows = new List<object?[]> { row };
        var table = new TinyTable(columns, rows);
        columns[0] = new("changed", TinyValueKind.String);
        row[0] = "invalid";
        rows.Clear();
        Assert.That(table.Columns[0].Name, Is.EqualTo("x"));
        Assert.That(table.Rows[0][0], Is.EqualTo(1));
        Assert.Throws<NotSupportedException>(() => ((IList<object?>)table.Rows[0])[0] = 2);
        Assert.Throws<NotSupportedException>(() => ((IList<TinyTableColumn>)table.Columns).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<IReadOnlyList<object?>>)table.Rows).Clear());
    }

    [Test]
    public void EmptyTablesKeepTheirSchema()
    {
        var table = new TinyTable([new("signal", TinyValueKind.Float, true)], []);
        Assert.That(table.Rows, Is.Empty);
        Assert.That(table.Columns, Has.Count.EqualTo(1));
    }
}
