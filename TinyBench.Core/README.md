# TinyBench.Core

The small, transport-independent value contract shared by the SDK and platform.
This library targets .NET 10 and has no package dependencies. Driver attributes,
C# signature mapping, JSON transport, and workflow execution live outside Core.

## Four public types

- `TinyValueKind`: `String`, `Boolean`, `Integer`, `Float`, `Array`, `Table`.
- `TinyType`: describes a kind and, for arrays, a scalar `ElementKind`.
- `TinyTableColumn`: a name, scalar kind, and `IsNullable` flag (false by default).
- `TinyTable`: a validated, immutable snapshot of a schema and its rows.

Scalars use ordinary C# values. Integer uses `int`, retaining the SDK's existing
32-bit range. Float accepts finite `float` and `double` values; there is one shared
floating-point kind. Integers and floats are distinct, without implicit coercion.
`NaN` and infinity are excluded so values can travel through JSON unchanged.

Arrays are one-dimensional typed arrays of a single scalar kind. Null elements,
`object[]`, jagged arrays, multidimensional arrays, and arrays of tables are not
supported. `TinyType.Accepts` validates values against the shared model.

## Tables

```csharp
using TinyBench.Core;

var measurements = new TinyTable(
    [
        new("well", TinyValueKind.String),
        new("replicate", TinyValueKind.Integer),
        new("absorbance", TinyValueKind.Float, isNullable: true)
    ],
    [
        ["A1", 1, 0.126],
        ["A2", 1, null]
    ]);
```

Column names must be non-blank and unique (case-sensitive). Every row must have
exactly one cell per column, in schema order. Cells must match their column's
scalar kind. A null cell is allowed only when that column is nullable. Arrays and
nested tables cannot be cells. Empty tables retain their schema.

Construction copies the supplied columns and rows and exposes read-only
collections, preventing later mutations from invalidating the table. Invalid
schemas or rows throw `ArgumentException` with the offending row/column when
applicable.

## SDK integration

Driver attributes remain in `TinyBench.Sdk.Core`. That library references Core
and provides `TinyTypeRules.Describe(typeof(TinyTable))` and the corresponding
scalar/array mappings. Drivers can accept `TinyTable` inputs and return tables
through `out TinyTable` parameters. Column schemas accompany table values;
they are not inferred from a method signature or executed during scaffolding.

Generated YAML retains the existing `type`/`array` format. Tables use
`type: "Table"` and `array: false`; both C# floating-point types use `type: "Float"`.
Consumers of the former SDK enum should import `TinyBench.Core`; the old
`TinyValueKind.Double` is replaced by `TinyValueKind.Float`.

The REST adapter owns JSON conversion. A table is transported as:

```json
{
  "columns": [
    { "name": "well", "kind": "String", "isNullable": false },
    { "name": "absorbance", "kind": "Float", "isNullable": true }
  ],
  "rows": [["A1", 0.126], ["A2", null]]
}
```

Omitting `isNullable` on input means false. JSON integers must fit `int`; floating
cells are read as `double`. Core itself has no JSON format dependency. Existing
SDK optional nullable scalar parameters continue to work; their null handling is
an SDK concern separate from table column nullability.

## Verification

From the repository root:

```powershell
dotnet test .\TinyBench.Tests\TinyBench.Tests.csproj
```

Tests cover shared value validation, table immutability and nullability, analyzer
signatures, REST conversion, generated metadata preservation, and a table input
and output crossing a dynamically loaded driver boundary.
