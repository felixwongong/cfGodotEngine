#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using cfEngine;
using cfEngine.Core;
using cfEngine.Info;
using cfEngine.Serialize;
using Godot;

namespace cfGodotEngine.Debug;

/// <summary>
/// Builds the content for the <c>info</c> debug window ("Info Viewer"). The returned subtree is a
/// fresh, parentless ordinary <see cref="Control"/>; assembling it performs no scene-tree attachment
/// and no worker/async work. The host invokes <see cref="Build()"/> from its main-thread factory,
/// attaches the result and owns the window chrome (title, close, drag, stacking).
///
/// Content: one tab per <see cref="InfoLayer.InfoMap"/> entry (name derived from the manager type),
/// a reflection table of the manager's rows (public readable instance properties, ordinal name order),
/// and a JSON detail pane for the tapped row. Rows are always gathered in full; only rendering is
/// capped at <see cref="RowCap"/>. Layout is orientation-aware against the current viewport
/// visible-rect <em>size</em>: a portrait viewport (height &gt; width) stacks the table above the
/// scrollable JSON detail, while a landscape viewport keeps the existing narrow replace / wide
/// side-by-side behaviour. The InfoLayer is re-resolved on every build and refresh and reads
/// currently-loaded runtime objects only (no pipeline/network calls).
///
/// Editing: a game-supplied <see cref="InfoDebugTableBinding"/> per manager enables cell editing,
/// row add/remove, Reload/Revert and clipboard export. Every write is a clone → edit-detached →
/// validate → serialize → write → reload transaction; the parameterless factory stays a read-only
/// viewer with no capability and explains that editing is unavailable. The injected editing path is
/// a <c>#if DEBUG || TOOLS</c> compile-time boundary: a release build never evaluates the binding
/// resolver, so no game-supplied writer can run there.
/// </summary>
public static class InfoDebugWindow
{
    private const int WideBreakpointWidth = 1000;
    private const int ViewerFontSize = 28;
    private const int RowCap = 500;
    private const float CellWidth = 240f;
    private const float TableStretchRatio = 0.55f;
    private const float DetailStretchRatio = 0.45f;
    private const float ActionSeparation = 8f;

    /// <summary>
    /// Assembles and returns a new parentless read-only Info-viewer subtree. No scene mutation, no
    /// <c>SceneTree.Root</c> access, no subscriptions to game state, no editing capability.
    /// </summary>
    public static Control Build() => Build(static _ => null!);

    /// <summary>
    /// Assembles a new parentless Info-viewer subtree whose per-manager editing capability is
    /// supplied by <paramref name="resolveBinding"/>. A binding enables editing for that tab only;
    /// a null resolution keeps the tab readable and explains that editing is unavailable. The
    /// resolver is never invoked in a non-debug/non-tools build.
    /// </summary>
    public static Control Build(Func<IInfoManager, InfoDebugTableBinding> resolveBinding)
    {
        if (resolveBinding == null)
            throw new ArgumentNullException(nameof(resolveBinding));

        var viewer = new Viewer(resolveBinding);
        return viewer.Root;
    }

    /// <summary>
    /// Compile-time editing boundary. Debug/tools builds resolve the caller's binding; any other
    /// build returns null without invoking the resolver, so no injected writer can ever run.
    /// </summary>
    private static InfoDebugTableBinding? ResolveBindingFor(
        Func<IInfoManager, InfoDebugTableBinding> resolveBinding, IInfoManager manager)
    {
#if DEBUG || TOOLS
        try
        {
            return resolveBinding(manager);
        }
        catch (Exception ex)
        {
            Log.LogException(ex, "InfoDebugWindow: binding resolution threw.");
            return null;
        }
#else
        _ = resolveBinding;
        _ = manager;
        return null;
#endif
    }

    /// <summary>
    /// Freshly resolves the current <see cref="InfoLayer"/> through <see cref="Domain.Current"/>.
    /// Handles a thrown lookup, an error result and null explicitly; always returns null on failure.
    /// </summary>
    private static InfoLayer? ResolveInfoLayer()
    {
        try
        {
            var result = Domain.Current.Get<InfoLayer>();
            if (result.HasError(out var error))
            {
                Log.LogException(error, "InfoDebugWindow: InfoLayer lookup returned an error.");
                return null;
            }

            return result.value;
        }
        catch (Exception ex)
        {
            Log.LogException(ex, "InfoDebugWindow: InfoLayer lookup threw.");
            return null;
        }
    }

    private static string DisplayNameFor(Type managerType)
    {
        var name = managerType.Name;
        const string infoSuffix = "InfoManager";
        if (name.EndsWith(infoSuffix, StringComparison.Ordinal))
            return name.Substring(0, name.Length - infoSuffix.Length);

        const string managerSuffix = "Manager";
        if (name.EndsWith(managerSuffix, StringComparison.Ordinal))
            return name.Substring(0, name.Length - managerSuffix.Length);

        return name;
    }

    private static string FormatCellValue(object? value)
    {
        switch (value)
        {
            case null:
                return "null";
            case string text:
                return text;
            case bool flag:
                return flag ? "true" : "false";
            case IEnumerable enumerable:
                return $"{value.GetType().Name}[{CountOf(enumerable)}]";
            case IFormattable formattable:
                return formattable.ToString(null, CultureInfo.InvariantCulture);
            default:
                return $"{{{value.GetType().Name}}}";
        }
    }

    private static int CountOf(IEnumerable enumerable)
    {
        if (enumerable is ICollection collection)
            return collection.Count;

        var count = 0;
        foreach (var _ in enumerable)
            count++;
        return count;
    }

    /// <summary>
    /// Public instance properties with a public getter and no index parameters, including inherited
    /// readable properties, ordered by ordinal property name for deterministic column alignment.
    /// </summary>
    private static List<PropertyInfo> ReadableColumns(Type infoType)
    {
        var columns = new List<PropertyInfo>();
        foreach (var property in infoType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length != 0)
                continue;

            var getter = property.GetGetMethod();
            if (getter == null || !getter.IsPublic || getter.IsStatic)
                continue;

            columns.Add(property);
        }

        columns.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));
        return columns;
    }

    /// <summary>
    /// Writes a <see cref="Vector2I"/> as numeric <c>{"X":..,"Y":..}</c> so nested coordinate arrays
    /// (e.g. floor grids) are useful instead of an empty object or a recursive derived-property loop.
    /// </summary>
    private sealed partial class Vector2IConverter : JsonConverter<Vector2I>
    {
        public override Vector2I Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotSupportedException("Vector2I reading is not supported.");
        }

        public override void Write(Utf8JsonWriter writer, Vector2I value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("X", value.X);
            writer.WriteNumber("Y", value.Y);
            writer.WriteEndObject();
        }
    }

    // =============================================================================================
    // Engine-free editing helpers. Kept as narrow, testable private operations (no native Control
    // construction, no global registration) so the managed behaviour can be exercised directly.
    // =============================================================================================

    private enum EditorKind
    {
        None,
        Text,
        Number,
        Bool,
        Enum,
        Vector2I
    }

    /// <summary>One reflected table column with its editing support and sheet constraint.</summary>
    private sealed class ColumnInfo
    {
        public PropertyInfo Property = null!;
        public string Name = string.Empty;
        public Type ValueType = typeof(object);
        public bool IsKey;
        public InfoFieldAttribute? Field;
        public EditorKind Kind;

        public bool CanEdit => Kind != EditorKind.None && Property.GetSetMethod(false) != null;
    }

    /// <summary>Reflected columns for <paramref name="infoType"/> carrying their editing support.</summary>
    private static List<ColumnInfo> DescribeColumns(Type infoType)
        => ReadableColumns(infoType).Select(DescribeColumn).ToList();

    private static ColumnInfo DescribeColumn(PropertyInfo property)
    {
        var type = property.PropertyType;
        var field = property.GetCustomAttribute<InfoFieldAttribute>();
        return new ColumnInfo
        {
            Property = property,
            Name = property.Name,
            ValueType = type,
            IsKey = property.GetCustomAttribute<InfoKeyAttribute>() != null,
            Field = field,
            Kind = KindOf(type)
        };
    }

    private static EditorKind KindOf(Type type)
    {
        if (type == typeof(string))
            return EditorKind.Text;
        if (type == typeof(bool))
            return EditorKind.Bool;
        if (type.IsEnum)
            return EditorKind.Enum;
        if (type == typeof(Vector2I))
            return EditorKind.Vector2I;
        if (IsNumeric(type))
            return EditorKind.Number;
        return EditorKind.None;
    }

    private static bool IsNumeric(Type type)
    {
        switch (Type.GetTypeCode(type))
        {
            case TypeCode.Byte:
            case TypeCode.SByte:
            case TypeCode.Int16:
            case TypeCode.UInt16:
            case TypeCode.Int32:
            case TypeCode.UInt32:
            case TypeCode.Int64:
            case TypeCode.UInt64:
            case TypeCode.Single:
            case TypeCode.Double:
            case TypeCode.Decimal:
                return true;
            default:
                return false;
        }
    }

    /// <summary>Invariant-culture cell parsing/validation, invariant formatting and value equality.</summary>
    private static class Codec
    {
        /// <summary>
        /// Parses <paramref name="text"/> into a value of <paramref name="type"/>, enforcing
        /// invariant culture, rejecting overflow/non-finite numerics and rejecting blank Required
        /// strings or out-of-bound numbers (Min/Max are inclusive). Never clamps silently.
        /// </summary>
        public static bool TryParse(Type type, string text, InfoFieldAttribute? field, out object? value, out string? error)
        {
            value = null;
            error = null;
            var raw = text ?? string.Empty;
            var trimmed = raw.Trim();

            if (type == typeof(string))
            {
                if (field is { Required: true } && string.IsNullOrWhiteSpace(raw))
                {
                    error = "required value must not be blank";
                    return false;
                }

                value = raw;
                return true;
            }

            if (type == typeof(bool))
            {
                if (!bool.TryParse(trimmed, out var flag))
                {
                    error = "not a boolean (true/false)";
                    return false;
                }

                value = flag;
                return true;
            }

            if (type.IsEnum)
            {
                if (trimmed.Length == 0)
                {
                    error = "not an enum value";
                    return false;
                }

                try
                {
                    var parsed = Enum.Parse(type, trimmed, ignoreCase: false);
                    if (!Enum.IsDefined(type, parsed))
                    {
                        error = $"'{trimmed}' is not a defined {type.Name} value";
                        return false;
                    }

                    value = parsed;
                    return true;
                }
                catch (Exception)
                {
                    error = $"'{trimmed}' is not a defined {type.Name} value";
                    return false;
                }
            }

            if (IsNumeric(type))
            {
                if (!TryParseNumber(type, trimmed, out var number))
                {
                    error = "not a number";
                    return false;
                }

                if (!CheckBounds(field, Convert.ToDouble(number, CultureInfo.InvariantCulture), out error))
                    return false;

                value = number;
                return true;
            }

            error = $"type {type.Name} is not editable";
            return false;
        }

        public static bool TryParseVector2I(string xText, string yText, out Vector2I value, out string? error)
        {
            value = default;
            if (!int.TryParse((xText ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var x))
            {
                error = "X is not an integer";
                return false;
            }

            if (!int.TryParse((yText ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
            {
                error = "Y is not an integer";
                return false;
            }

            error = null;
            value = new Vector2I(x, y);
            return true;
        }

        /// <summary>Validates an already-typed value against Required/Min/Max constraints.</summary>
        public static bool ValidateValue(Type type, object? value, InfoFieldAttribute? field, out string? error)
        {
            error = null;
            if (type == typeof(string))
            {
                if (field is { Required: true } && string.IsNullOrWhiteSpace(value as string))
                {
                    error = "required value must not be blank";
                    return false;
                }

                return true;
            }

            if (value == null)
            {
                error = "value is required";
                return false;
            }

            if (IsNumeric(type))
            {
                double number;
                try
                {
                    number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                }
                catch (Exception)
                {
                    error = "value is not numeric";
                    return false;
                }

                if (double.IsNaN(number) || double.IsInfinity(number))
                {
                    error = "value must be finite";
                    return false;
                }

                return CheckBounds(field, number, out error);
            }

            return true;
        }

        private static bool TryParseNumber(Type type, string text, out object? value)
        {
            const NumberStyles integerStyle = NumberStyles.Integer;
            const NumberStyles floatStyle = NumberStyles.Float;
            var inv = CultureInfo.InvariantCulture;
            value = null;

            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Byte when byte.TryParse(text, integerStyle, inv, out var u8):
                    value = u8;
                    return true;
                case TypeCode.SByte when sbyte.TryParse(text, integerStyle, inv, out var i8):
                    value = i8;
                    return true;
                case TypeCode.Int16 when short.TryParse(text, integerStyle, inv, out var i16):
                    value = i16;
                    return true;
                case TypeCode.UInt16 when ushort.TryParse(text, integerStyle, inv, out var u16):
                    value = u16;
                    return true;
                case TypeCode.Int32 when int.TryParse(text, integerStyle, inv, out var i32):
                    value = i32;
                    return true;
                case TypeCode.UInt32 when uint.TryParse(text, integerStyle, inv, out var u32):
                    value = u32;
                    return true;
                case TypeCode.Int64 when long.TryParse(text, integerStyle, inv, out var i64):
                    value = i64;
                    return true;
                case TypeCode.UInt64 when ulong.TryParse(text, integerStyle, inv, out var u64):
                    value = u64;
                    return true;
                case TypeCode.Single when float.TryParse(text, floatStyle, inv, out var f) && float.IsFinite(f):
                    value = f;
                    return true;
                case TypeCode.Double when double.TryParse(text, floatStyle, inv, out var d) && double.IsFinite(d):
                    value = d;
                    return true;
                case TypeCode.Decimal when decimal.TryParse(text, NumberStyles.Number, inv, out var m):
                    value = m;
                    return true;
                default:
                    return false;
            }
        }

        private static bool CheckBounds(InfoFieldAttribute? field, double number, out string? error)
        {
            error = null;
            if (field == null)
                return true;

            if (number < field.Min)
            {
                error = $"value {number.ToString(CultureInfo.InvariantCulture)} is below Min {field.Min.ToString(CultureInfo.InvariantCulture)}";
                return false;
            }

            if (number > field.Max)
            {
                error = $"value {number.ToString(CultureInfo.InvariantCulture)} is above Max {field.Max.ToString(CultureInfo.InvariantCulture)}";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Structural equality: scalar values compare by value, collections compare element-by-element
        /// with order significant, and nested objects compare property-by-property with property order
        /// ignored. Numbers parsed from quoted canonical JSON still compare as their typed values.
        /// </summary>
        public static bool ValuesEqual(object? left, object? right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null)
                return false;

            if (left is string || right is string)
                return string.Equals(left as string, right as string, StringComparison.Ordinal);

            if (left is IEnumerable leftEnumerable && left is not string)
            {
                if (right is not IEnumerable rightEnumerable || right is string)
                    return false;

                var leftItems = leftEnumerable.Cast<object?>().ToList();
                var rightItems = rightEnumerable.Cast<object?>().ToList();
                if (leftItems.Count != rightItems.Count)
                    return false;

                for (var i = 0; i < leftItems.Count; i++)
                {
                    if (!ValuesEqual(leftItems[i], rightItems[i]))
                        return false;
                }

                return true;
            }

            var type = left.GetType();
            if (type != right.GetType())
                return false;

            if (type.IsClass && type != typeof(string))
            {
                var properties = ReadableColumns(type);
                foreach (var property in properties)
                {
                    object? leftValue;
                    object? rightValue;
                    try
                    {
                        leftValue = property.GetValue(left);
                        rightValue = property.GetValue(right);
                    }
                    catch (Exception)
                    {
                        return false;
                    }

                    if (!ValuesEqual(leftValue, rightValue))
                        return false;
                }

                return true;
            }

            return left.Equals(right);
        }
    }

    /// <summary>Mandatory + unique <see cref="InfoKeyAttribute"/> handling.</summary>
    private static class Keying
    {
        public static PropertyInfo? FindKey(Type infoType)
        {
            foreach (var property in infoType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetCustomAttribute<InfoKeyAttribute>() != null)
                    return property;
            }

            return null;
        }

        public static string? FormatKey(PropertyInfo keyProperty, object row)
        {
            var value = keyProperty.GetValue(row);
            return FormatKeyValue(value);
        }

        public static string? FormatKeyValue(object? value)
        {
            if (value == null)
                return null;

            return value is IFormattable formattable
                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                : value.ToString();
        }

        public static string? ValidateKeyValue(object? value)
        {
            if (value == null)
                return "key is required";
            if (value is string text && string.IsNullOrWhiteSpace(text))
                return "key must not be blank";
            return null;
        }

        /// <summary>
        /// Every row's key must be present and unique across the whole table (not just displayed rows).
        /// A numeric key of zero is valid; a null/blank string key is not.
        /// </summary>
        public static bool ValidateTableKeys(Type infoType, IList rows, out string? error)
        {
            error = null;
            var keyProperty = FindKey(infoType);
            if (keyProperty == null)
            {
                error = $"type {infoType.Name} has no [InfoKey] property";
                return false;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                if (row == null)
                {
                    error = "table contains a null row";
                    return false;
                }

                object? value;
                try
                {
                    value = keyProperty.GetValue(row);
                }
                catch (Exception ex)
                {
                    error = $"key read failed: {ex.GetType().Name}";
                    return false;
                }

                var keyError = ValidateKeyValue(value);
                if (keyError != null)
                {
                    error = keyError;
                    return false;
                }

                var key = FormatKeyValue(value);
                if (!seen.Add(key!))
                {
                    error = $"duplicate key '{key}'";
                    return false;
                }
            }

            return true;
        }

        public static int IndexOfKey(Type infoType, IList rows, string? key)
        {
            var keyProperty = FindKey(infoType);
            if (keyProperty == null || key == null)
                return -1;

            for (var i = 0; i < rows.Count; i++)
            {
                if (string.Equals(FormatKey(keyProperty, rows[i]!), key, StringComparison.Ordinal))
                    return i;
            }

            return -1;
        }

        public static object? DefaultInstance(Type infoType)
        {
            try
            {
                return Activator.CreateInstance(infoType);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>Fresh-canon diff keyed by the single <see cref="InfoKeyAttribute"/>.</summary>
    private static class Diffing
    {
        public static DiffResult Compute(
            Type infoType,
            IReadOnlyList<ColumnInfo> columns,
            IReadOnlyList<object> current,
            IReadOnlyDictionary<string, object> canonByKey,
            bool canonKnown)
        {
            var result = new DiffResult { CanonKnown = canonKnown };
            if (!canonKnown)
                return result;

            var keyProperty = Keying.FindKey(infoType);
            var dataColumns = columns.Where(static column => !column.IsKey).ToList();
            var currentKeys = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < current.Count; i++)
            {
                var row = current[i];
                var key = keyProperty != null ? Keying.FormatKey(keyProperty, row) : null;
                if (key != null)
                    currentKeys.Add(key);

                var diff = new RowDiff { Row = row, Key = key ?? string.Empty };

                if (key != null && canonByKey.TryGetValue(key, out var canonRow))
                {
                    foreach (var column in dataColumns)
                    {
                        if (CellsEqual(column.Property, row, canonRow))
                            continue;

                        diff.ChangedProperties.Add(column.Name);
                        diff.ChangedCells++;
                        result.ChangedCells++;
                    }
                }
                else
                {
                    diff.Added = true;
                    foreach (var column in dataColumns)
                    {
                        diff.ChangedProperties.Add(column.Name);
                        diff.ChangedCells++;
                        result.ChangedCells++;
                    }
                }

                if (diff.ChangedCells > 0 || diff.Added)
                {
                    result.ChangedRowIndices.Add(i);
                    result.ByIndex[i] = diff;
                }
            }

            foreach (var pair in canonByKey)
            {
                if (currentKeys.Contains(pair.Key))
                    continue;

                result.DeletedKeys.Add(pair.Key);
                result.ChangedCells += dataColumns.Count;
            }

            result.DeletedKeys.Sort(StringComparer.Ordinal);
            return result;
        }

        private static bool CellsEqual(PropertyInfo property, object left, object right)
        {
            object? leftValue;
            object? rightValue;
            try
            {
                leftValue = property.GetValue(left);
                rightValue = property.GetValue(right);
            }
            catch (Exception)
            {
                return false;
            }

            return Codec.ValuesEqual(leftValue, rightValue);
        }
    }

    private sealed class RowDiff
    {
        public object Row = null!;
        public string Key = string.Empty;
        public bool Added;
        public readonly HashSet<string> ChangedProperties = new(StringComparer.Ordinal);
        public int ChangedCells;
    }

    private sealed class DiffResult
    {
        public bool CanonKnown;
        public int ChangedCells;
        public readonly List<int> ChangedRowIndices = new();
        public readonly Dictionary<int, RowDiff> ByIndex = new();
        public readonly List<string> DeletedKeys = new();
    }

    /// <summary>Clipboard TSV: full uncapped rows projected to the table's local-order sheet matrix.</summary>
    private static class Tsv
    {
        /// <summary>Ordinary TSV cell escaping (quotes/duplicated quotes around tabs/newlines/quotes).</summary>
        public static string Escape(string? value)
        {
            if (value == null)
                return string.Empty;
            if (value.IndexOfAny(new[] { '\t', '\n', '\r', '"' }) < 0)
                return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>
        /// Projects the full current row list through <paramref name="project"/> and joins the
        /// resulting matrix as TSV. Any projection failure (including an explicit sectioned-layout
        /// refusal) returns false and never produces partial/lossy output.
        /// </summary>
        public static bool TryProjectFlat(
            Func<IReadOnlyList<object>, IReadOnlyList<IReadOnlyList<string>>> project,
            IReadOnlyList<object> fullRows,
            out string? tsv,
            out string? error)
        {
            tsv = null;
            error = null;

            IReadOnlyList<IReadOnlyList<string>> matrix;
            try
            {
                matrix = project(fullRows);
            }
            catch (Exception ex)
            {
                error = $"flat sheet projection refused or failed: {ex.Message}";
                return false;
            }

            if (matrix == null)
            {
                error = "flat sheet projection returned no sheet";
                return false;
            }

            var builder = new StringBuilder();
            foreach (var row in matrix)
            {
                if (row == null)
                {
                    error = "flat sheet projection returned a null row";
                    return false;
                }

                for (var c = 0; c < row.Count; c++)
                {
                    if (c > 0)
                        builder.Append('\t');
                    builder.Append(Escape(row[c]));
                }

                builder.Append('\n');
            }

            tsv = builder.ToString();
            return true;
        }

        /// <summary>
        /// Builds the flat TSV and replaces the clipboard only on success. A refused/failed
        /// projection leaves <paramref name="setClipboard"/> untouched.
        /// </summary>
        public static bool CopyFlat(
            Func<IReadOnlyList<object>, IReadOnlyList<IReadOnlyList<string>>> project,
            IReadOnlyList<object> fullRows,
            Action<string> setClipboard,
            out string? error)
        {
            if (!TryProjectFlat(project, fullRows, out var tsv, out error))
                return false;

            setClipboard(tsv!);
            return true;
        }

        /// <summary>
        /// Deterministic changed-cell TSV: current-row order, ordinal column order, deleted rows
        /// last (ordinal key order), missing cells marked explicitly.
        /// </summary>
        public static string BuildChanges(
            Type infoType,
            IReadOnlyList<ColumnInfo> columns,
            IReadOnlyList<object> current,
            IReadOnlyDictionary<string, object> canonByKey,
            DiffResult diff)
        {
            const string missing = "<missing>";
            var builder = new StringBuilder();
            builder.Append("#key\tproperty\tcanon\tcurrent\n");
            var keyProperty = Keying.FindKey(infoType);
            var dataColumns = columns.Where(static column => !column.IsKey).ToList();

            foreach (var rowIndex in diff.ChangedRowIndices)
            {
                var row = current[rowIndex];
                var key = keyProperty != null ? Keying.FormatKey(keyProperty, row) ?? string.Empty : string.Empty;
                var rowDiff = diff.ByIndex[rowIndex];
                foreach (var column in dataColumns)
                {
                    if (!rowDiff.ChangedProperties.Contains(column.Name))
                        continue;

                    var currentText = FormatCellValue(column.Property.GetValue(row));
                    string canonText;
                    if (rowDiff.Added || !canonByKey.TryGetValue(key, out var canonRow))
                        canonText = missing;
                    else
                        canonText = FormatCellValue(column.Property.GetValue(canonRow));

                    builder.Append(Escape(key)).Append('\t')
                        .Append(Escape(column.Name)).Append('\t')
                        .Append(Escape(canonText)).Append('\t')
                        .Append(Escape(currentText)).Append('\n');
                }
            }

            foreach (var deletedKey in diff.DeletedKeys)
            {
                if (!canonByKey.TryGetValue(deletedKey, out var canonRow))
                    continue;

                foreach (var column in dataColumns)
                {
                    builder.Append(Escape(deletedKey)).Append('\t')
                        .Append(Escape(column.Name)).Append('\t')
                        .Append(Escape(FormatCellValue(column.Property.GetValue(canonRow)))).Append('\t')
                        .Append(Escape(missing)).Append('\n');
                }
            }

            return builder.ToString();
        }
    }

    /// <summary>
    /// Conservative, label-only <see cref="InfoRefAttribute"/> reference scan used before deletion.
    /// Exact scalar/array keys and delimited tokens inside string values count as candidate
    /// references; an incomplete scan is reported as such and never as "no references".
    /// </summary>
    private static class Refs
    {
        public static RefScan Scan(IEnumerable<IInfoManager> managers, string? targetKey)
        {
            var scan = new RefScan();
            if (string.IsNullOrWhiteSpace(targetKey))
            {
                scan.Complete = false;
                scan.Error = "target key unavailable; reference checking was incomplete";
                return scan;
            }

            foreach (var manager in managers)
            {
                Type infoType;
                try
                {
                    infoType = manager.infoType;
                }
                catch (Exception ex)
                {
                    scan.Complete = false;
                    scan.Error = $"a manager type could not be read ({ex.GetType().Name}); reference checking was incomplete";
                    continue;
                }

                if (infoType == null)
                    continue;

                var refProperties = new List<PropertyInfo>();
                foreach (var property in infoType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (property.GetIndexParameters().Length != 0)
                        continue;
                    if (property.GetCustomAttribute<InfoRefAttribute>() == null)
                        continue;
                    refProperties.Add(property);
                }

                if (refProperties.Count == 0)
                    continue;

                List<object> rows;
                try
                {
                    rows = manager.GetAllValue().ToList();
                }
                catch (Exception ex)
                {
                    scan.Complete = false;
                    scan.Error = $"a manager's rows could not be enumerated ({ex.GetType().Name}); reference checking was incomplete";
                    continue;
                }

                var keyProperty = Keying.FindKey(infoType);
                foreach (var row in rows)
                {
                    if (row == null)
                        continue;

                    var rowKey = keyProperty != null ? Keying.FormatKey(keyProperty, row) ?? "?" : "?";
                    foreach (var property in refProperties)
                    {
                        object? value;
                        try
                        {
                            value = property.GetValue(row);
                        }
                        catch (Exception ex)
                        {
                            scan.Complete = false;
                            scan.Error = $"{infoType.Name}.{property.Name} could not be read ({ex.GetType().Name}); reference checking was incomplete";
                            continue;
                        }

                        if (TokenContains(value, targetKey))
                        {
                            scan.Candidates.Add($"{infoType.Name}.{property.Name} [{rowKey}]: {FormatCellValue(value)}");
                        }
                    }
                }
            }

            return scan;
        }

        private static bool TokenContains(object? value, string key)
        {
            switch (value)
            {
                case null:
                    return false;
                case string text:
                    return TokenMatches(text, key);
                case IEnumerable enumerable:
                    foreach (var item in enumerable)
                    {
                        if (TokenContains(item, key))
                            return true;
                    }

                    return false;
                default:
                    var formatted = value is IFormattable formattable
                        ? formattable.ToString(null, CultureInfo.InvariantCulture)
                        : value.ToString();
                    return string.Equals(formatted, key, StringComparison.Ordinal);
            }
        }

        /// <summary>
        /// True when the whole value equals the key or any delimited token equals it exactly, so
        /// <c>(farm_mine_1,5)</c> matches <c>farm_mine_1</c> but never <c>farm_mine_10</c>.
        /// </summary>
        private static bool TokenMatches(string text, string key)
        {
            if (string.Equals(text, key, StringComparison.Ordinal))
                return true;

            var start = -1;
            for (var i = 0; i <= text.Length; i++)
            {
                var isTokenChar = i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_');
                if (isTokenChar)
                {
                    if (start < 0)
                        start = i;
                    continue;
                }

                if (start >= 0)
                {
                    if (i - start == key.Length && string.CompareOrdinal(text, start, key, 0, key.Length) == 0)
                        return true;
                    start = -1;
                }
            }

            return false;
        }
    }

    private sealed class RefScan
    {
        public bool Complete = true;
        public string? Error;
        public readonly List<string> Candidates = new();
    }

    /// <summary>
    /// Clone → edit-detached → validate → serialize → write → reload transaction shared by every
    /// editing operation. Never mutates a published row; never reports success on a failed write;
    /// restores the previous published document when the reload fails.
    /// </summary>
    private static class Transaction
    {
        public enum Stage
        {
            Serialize,
            Mutate,
            Validate,
            Write,
            Reload,
            Recovery
        }

        public sealed class Result
        {
            public bool Success;
            public bool WriteFailed;
            public bool ReloadFailed;
            public bool RecoveryFailed;
            public Stage FailedStage;
            public string? Error;
            public int RowCount;
        }

        public static Result Commit(
            ISerializer serializer,
            Type infoType,
            IReadOnlyList<object> currentRows,
            Func<IList, string?> mutateAndValidate,
            Action<string> writeOverride,
            Func<bool> reload)
        {
            var result = new Result();

            string oldJson;
            IList candidate;
            try
            {
                oldJson = SerializeRows(serializer, infoType, currentRows);
                candidate = DeserializeRows(serializer, infoType, Encoding.UTF8.GetBytes(oldJson));
            }
            catch (Exception ex)
            {
                return Fail(result, Stage.Serialize, Describe(ex));
            }

            string? mutationError;
            try
            {
                mutationError = mutateAndValidate(candidate);
            }
            catch (Exception ex)
            {
                return Fail(result, Stage.Mutate, Describe(ex));
            }

            if (mutationError != null)
                return Fail(result, Stage.Validate, mutationError);

            string candidateJson;
            try
            {
                candidateJson = SerializeRows(serializer, infoType, candidate);
            }
            catch (Exception ex)
            {
                return Fail(result, Stage.Serialize, Describe(ex));
            }

            try
            {
                writeOverride(candidateJson);
            }
            catch (Exception ex)
            {
                result.WriteFailed = true;
                return Fail(result, Stage.Write, Describe(ex));
            }

            bool reloaded;
            try
            {
                reloaded = reload();
            }
            catch (Exception ex)
            {
                reloaded = false;
                result.Error = Describe(ex);
            }

            if (!reloaded)
            {
                result.ReloadFailed = true;
                try
                {
                    writeOverride(oldJson);
                    if (!reload())
                        result.RecoveryFailed = true;
                }
                catch (Exception ex)
                {
                    result.RecoveryFailed = true;
                    result.Error ??= Describe(ex);
                }

                if (result.RecoveryFailed)
                {
                    result.Error = $"{result.Error ?? "reload failed"}; restoring the previous document failed — the file and the published table may now disagree";
                }

                return Fail(result, Stage.Reload, result.Error ?? "reload failed");
            }

            result.RowCount = candidate.Count;
            result.Success = true;
            return result;
        }

        private static Result Fail(Result result, Stage stage, string? error)
        {
            result.Success = false;
            result.FailedStage = stage;
            result.Error ??= error;
            return result;
        }

        private static string Describe(Exception ex) => $"{ex.GetType().Name}: {ex.Message}";

        private static string SerializeRows(ISerializer serializer, Type infoType, IEnumerable rows)
        {
            var listType = typeof(List<>).MakeGenericType(infoType);
            var list = (IList)Activator.CreateInstance(listType)!;
            foreach (var row in rows)
                list.Add(row);

            return serializer.Serialize(list);
        }

        private static IList DeserializeRows(ISerializer serializer, Type infoType, byte[] bytes)
        {
            var listType = typeof(List<>).MakeGenericType(infoType);
            var method = typeof(ISerializer).GetMethod(nameof(ISerializer.DeserializeAs))!;
            var generic = method.MakeGenericMethod(listType);
            var value = generic.Invoke(serializer, new object?[] { bytes, null });
            if (value is not IList list)
                throw new InvalidOperationException($"deserialized candidate for {infoType.Name} was not a list");

            return list;
        }
    }

    /// <summary>Per-window state and behaviour. One instance per built window; no static state.</summary>
    private sealed class Viewer
    {
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly Func<IInfoManager, InfoDebugTableBinding> _resolveBinding;
        private readonly SynchronizationContext? _syncContext;

        private readonly VBoxContainer _column;
        private readonly BoxContainer _body;
        private readonly Label _status;
        private readonly Button _refreshButton;
        private readonly Button _reloadButton;
        private readonly Button _revertButton;
        private readonly Button _addButton;
        private readonly CheckBox _changesOnly;
        private readonly Button _copyFlatButton;
        private readonly Button _copyChangesButton;
        private readonly VBoxContainer _addPanel;
        private LineEdit _addKeyInput = null!;
        private Label _addError = null!;
        private readonly VBoxContainer _confirmPanel;
        private Label _confirmText = null!;
        private readonly TabContainer _tabContainer;
        private readonly VBoxContainer _detailPane;
        private readonly Button _backButton;
        private readonly RichTextLabel _detailText;

        private readonly List<Subscription> _subscriptions = new();

        private List<TabState>? _tabStates;
        private string _placeholderTitle = "InfoLayer unavailable";
        private int _selectedTab = -1;
        private string? _selectedKey;
        private bool _narrowDetailOpen;
        private bool _suppressTabChanged;
        private bool _changesOnlyMode;
        private bool _editingDisabled;
        private bool _selfCommit;
        private bool _refreshQueued;
        private bool _exiting;
        private string? _pendingDeleteKey;

        private Viewport? _viewport;

        public Viewer(Func<IInfoManager, InfoDebugTableBinding> resolveBinding)
        {
            _resolveBinding = resolveBinding;
            _syncContext = SynchronizationContext.Current;

            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                IncludeFields = true,
                MaxDepth = 64
            };
            _jsonOptions.Converters.Add(new Vector2IConverter());

            Root = new Control();
            Root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

            _column = new VBoxContainer();
            _column.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            _column.AddThemeConstantOverride("separation", (int)ActionSeparation);
            Root.AddChild(_column);

            // Header: a dedicated status line above two width-wrapping action rows (all 28px).
            var statusRow = new HBoxContainer();
            statusRow.AddThemeConstantOverride("separation", (int)ActionSeparation);
            _column.AddChild(statusRow);

            _status = new Label
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Text = _placeholderTitle
            };
            _status.AddThemeFontSizeOverride("font_size", ViewerFontSize);
            statusRow.AddChild(_status);

            var actionRow1 = NewActionRow();
            _refreshButton = NewActionButton("⟳ Refresh", Refresh);
            _reloadButton = NewActionButton("Reload", ReloadSelected);
            _revertButton = NewActionButton("Revert", RevertSelected);
            _addButton = NewActionButton("Add", OpenAddPanel);
            actionRow1.AddChild(_refreshButton);
            actionRow1.AddChild(_reloadButton);
            actionRow1.AddChild(_revertButton);
            actionRow1.AddChild(_addButton);

            var actionRow2 = NewActionRow();
            _changesOnly = new CheckBox
            {
                Text = "Changes only",
                FocusMode = Control.FocusModeEnum.None,
                ButtonPressed = false
            };
            _changesOnly.AddThemeFontSizeOverride("font_size", ViewerFontSize);
            _changesOnly.Toggled += OnChangesOnlyToggled;
            _copyFlatButton = NewActionButton("Copy TSV", CopyFlatToClipboard);
            _copyChangesButton = NewActionButton("Copy changes", CopyChangesToClipboard);
            actionRow2.AddChild(_changesOnly);
            actionRow2.AddChild(_copyFlatButton);
            actionRow2.AddChild(_copyChangesButton);

            _column.AddChild(actionRow1);
            _column.AddChild(actionRow2);

            _addPanel = BuildAddPanel();
            _column.AddChild(_addPanel);

            _confirmPanel = BuildConfirmPanel();
            _column.AddChild(_confirmPanel);

            // Body: table tabs and the JSON detail pane, one orientation-switchable container. Vertical
            // in portrait (table above detail); horizontal otherwise (side-by-side when wide).
            _body = new BoxContainer
            {
                Vertical = false,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            _body.AddThemeConstantOverride("separation", (int)ActionSeparation);
            _column.AddChild(_body);

            _tabContainer = new TabContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                SizeFlagsStretchRatio = TableStretchRatio
            };
            _tabContainer.AddThemeFontSizeOverride("font_size", ViewerFontSize);
            var tabBar = _tabContainer.GetTabBar();
            tabBar.ClipTabs = true;
            tabBar.MaxTabWidth = 150;
            _tabContainer.TabChanged += OnTabChanged;
            _body.AddChild(_tabContainer);

            _detailPane = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                SizeFlagsStretchRatio = DetailStretchRatio,
                Visible = false
            };
            _detailPane.AddThemeConstantOverride("separation", 6);
            _body.AddChild(_detailPane);

            _backButton = new Button
            {
                Text = "‹ back",
                FocusMode = Control.FocusModeEnum.None,
                Visible = false
            };
            _backButton.AddThemeFontSizeOverride("font_size", ViewerFontSize);
            _backButton.Pressed += OnBack;
            _detailPane.AddChild(_backButton);

            var mono = new SystemFont { FontNames = new[] { "monospace" }, AllowSystemFallback = true };
            _detailText = new RichTextLabel
            {
                BbcodeEnabled = false,
                SelectionEnabled = true,
                ScrollActive = true,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            _detailText.AddThemeFontOverride("normal_font", mono);
            _detailText.AddThemeFontOverride("mono_font", mono);
            _detailText.AddThemeFontSizeOverride("normal_font_size", ViewerFontSize);
            _detailText.AddThemeFontSizeOverride("mono_font_size", ViewerFontSize);
            _detailPane.AddChild(_detailText);

            Root.TreeEntered += OnTreeEntered;
            Root.TreeExiting += OnTreeExiting;

            Refresh();
            ApplyResponsive(Vector2.Zero);
        }

        public Control Root { get; }

        // ---- header construction --------------------------------------------

        private static HBoxContainer NewActionRow()
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", (int)ActionSeparation);
            return row;
        }

        private static Button NewActionButton(string text, Action onPressed)
        {
            var button = new Button
            {
                Text = text,
                FocusMode = Control.FocusModeEnum.None
            };
            button.AddThemeFontSizeOverride("font_size", ViewerFontSize);
            button.Pressed += onPressed;
            return button;
        }

        private VBoxContainer BuildAddPanel()
        {
            var panel = new VBoxContainer { Visible = false };
            panel.AddThemeConstantOverride("separation", 6);

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", (int)ActionSeparation);
            var caption = new Label { Text = "New key:" };
            caption.AddThemeFontSizeOverride("font_size", ViewerFontSize);
            caption.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(caption);

            _addKeyInput = new LineEdit
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                PlaceholderText = "key"
            };
            _addKeyInput.AddThemeFontSizeOverride("font_size", ViewerFontSize);
            _addKeyInput.TextSubmitted += _ => CommitAdd();
            row.AddChild(_addKeyInput);

            var confirm = NewActionButton("Add row", CommitAdd);
            var cancel = NewActionButton("Cancel", CloseAddPanel);
            row.AddChild(confirm);
            row.AddChild(cancel);
            panel.AddChild(row);

            _addError = new Label { Visible = false, AutowrapMode = TextServer.AutowrapMode.WordSmart };
            _addError.AddThemeFontSizeOverride("font_size", ViewerFontSize);
            panel.AddChild(_addError);
            return panel;
        }

        private VBoxContainer BuildConfirmPanel()
        {
            var panel = new VBoxContainer { Visible = false };
            panel.AddThemeConstantOverride("separation", 6);

            _confirmText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
            _confirmText.AddThemeFontSizeOverride("font_size", ViewerFontSize);
            panel.AddChild(_confirmText);

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", (int)ActionSeparation);
            row.AddChild(NewActionButton("Delete", CommitDelete));
            row.AddChild(NewActionButton("Cancel", CloseConfirmPanel));
            panel.AddChild(row);
            return panel;
        }

        // ---- lifecycle -------------------------------------------------------

        private void OnTreeEntered()
        {
            _viewport = Root.GetViewport();
            if (_viewport != null)
                _viewport.SizeChanged += OnViewportSizeChanged;
            Root.Resized += OnRootResized;
            // Re-entry must clear the exiting flag: a re-entered Root has to keep receiving manager
            // notifications and deferred refreshes, not silently drop them.
            _exiting = false;
            ApplyResponsive(CurrentViewportSize());
            // Refresh immediately on re-entry: transient state from an off-tree period is discarded.
            Refresh();
        }

        private void OnTreeExiting()
        {
            _exiting = true;
            UnsubscribeAll();
            if (_viewport != null)
            {
                _viewport.SizeChanged -= OnViewportSizeChanged;
                _viewport = null;
            }

            Root.Resized -= OnRootResized;
        }

        private void OnViewportSizeChanged()
        {
            ApplyResponsive(CurrentViewportSize());
        }

        private void OnRootResized()
        {
            ApplyResponsive(CurrentViewportSize());
        }

        private Vector2 CurrentViewportSize()
        {
            var viewport = _viewport ?? Root.GetViewport();
            return viewport?.GetVisibleRect().Size ?? Vector2.Zero;
        }

        // ---- manager subscriptions ------------------------------------------

        private readonly struct Subscription
        {
            public Subscription(IInfoManager manager, Action handler)
            {
                Manager = manager;
                Handler = handler;
            }

            public IInfoManager Manager { get; }
            public Action Handler { get; }
        }

        private void UnsubscribeAll()
        {
            foreach (var subscription in _subscriptions)
                subscription.Manager.Reloaded -= subscription.Handler;
            _subscriptions.Clear();
        }

        private void Resubscribe()
        {
            UnsubscribeAll();
            if (_tabStates == null)
                return;

            foreach (var tabState in _tabStates)
            {
                var captured = tabState;
                var handler = new Action(OnManagerReloaded);
                captured.Manager.Reloaded += handler;
                _subscriptions.Add(new Subscription(captured.Manager, handler));
            }
        }

        private void OnManagerReloaded()
        {
            if (_exiting || _selfCommit)
                return;

            // Reload/Revert may complete off the main thread; managed notifications are posted back.
            if (_syncContext != null && SynchronizationContext.Current != _syncContext)
            {
                try
                {
                    _syncContext.Post(static state => ((Viewer)state!).OnManagerReloaded(), this);
                    return;
                }
                catch (Exception ex)
                {
                    Log.LogException(ex, "InfoDebugWindow: failed to post a reload notification.");
                    return;
                }
            }

            RequestRefresh();
        }

        private void RequestRefresh()
        {
            if (_exiting || _refreshQueued)
                return;

            _refreshQueued = true;
            if (Root.IsInsideTree())
                Callable.From(DrainRefresh).CallDeferred();
            else
                DrainRefresh();
        }

        private void DrainRefresh()
        {
            _refreshQueued = false;
            if (!_exiting)
                Refresh();
        }

        // ---- refresh ---------------------------------------------------------

        private void Refresh()
        {
            var previousType = SelectedType();
            var previousKey = _selectedKey;
            var infoLayer = ResolveInfoLayer();
            if (infoLayer == null)
            {
                ShowMessage("InfoLayer unavailable", "InfoLayer unavailable. Tap ⟳ Refresh to retry.");
                return;
            }

            IReadOnlyDictionary<Type, IInfoManager> map;
            try
            {
                map = infoLayer.InfoMap;
            }
            catch (Exception ex)
            {
                Log.LogException(ex, "InfoDebugWindow: InfoMap enumeration failed.");
                ShowMessage("InfoLayer unavailable", "Info managers could not be enumerated. Tap ⟳ Refresh to retry.");
                return;
            }

            var timestamp = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            var tabs = new List<TabState>();
            try
            {
                foreach (var entry in map)
                    tabs.Add(BuildTab(entry.Key, entry.Value, timestamp));
            }
            catch (Exception ex)
            {
                Log.LogException(ex, "InfoDebugWindow: manager enumeration failed.");
                ShowMessage("InfoLayer unavailable", "Info managers could not be enumerated. Tap ⟳ Refresh to retry.");
                return;
            }

            if (tabs.Count == 0)
            {
                ShowMessage("no managers", "No Info managers registered. Tap ⟳ Refresh to retry.");
                return;
            }

            _tabStates = tabs;
            _placeholderTitle = "InfoLayer unavailable";
            Resubscribe();
            BuildTabUi();

            _suppressTabChanged = true;
            _selectedTab = IndexOfType(previousType);
            _tabContainer.CurrentTab = _selectedTab;
            _suppressTabChanged = false;

            _selectedKey = previousKey;
            _narrowDetailOpen = false;
            _detailText.Text = string.Empty;

            UpdateTitle();
            UpdateStatus();
            ApplyResponsive(CurrentViewportSize());
        }

        private TabState BuildTab(Type managerType, IInfoManager manager, string timestamp)
        {
            var tab = new TabState(managerType, DisplayNameFor(managerType), timestamp)
            {
                Manager = manager,
                Binding = ResolveBindingFor(_resolveBinding, manager)
            };

            try
            {
                tab.InfoType = manager.infoType ?? typeof(object);
                tab.Columns = DescribeColumns(tab.InfoType);
            }
            catch (Exception ex)
            {
                tab.Error = $"infoType failed: {ex.GetType().Name}";
                return tab;
            }

            try
            {
                // Always gather the full table; only rendering is capped. Commits, exports and
                // reference scans must see every row, not just the displayed window.
                var rows = new List<object>();
                foreach (var value in manager.GetAllValue())
                    rows.Add(value);

                tab.Rows = rows;
                tab.TotalRows = rows.Count;
            }
            catch (Exception ex)
            {
                tab.Error = $"GetAllValue failed: {ex.GetType().Name}";
                return tab;
            }

            LoadCanon(tab);
            return tab;
        }

        private void LoadCanon(TabState tab)
        {
            tab.CanonKnown = false;
            tab.CanonByKey = new Dictionary<string, object>(StringComparer.Ordinal);
            tab.Diff = new DiffResult();

            if (tab.Binding == null)
                return;

            try
            {
                var bytes = tab.Binding.readCanonical();
                if (bytes == null)
                    return;

                var canonRows = DeserializeRows(tab.Binding.serializer, tab.InfoType, bytes);
                var keyProperty = Keying.FindKey(tab.InfoType);
                if (keyProperty == null)
                    return;

                var map = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var row in canonRows)
                {
                    if (row == null)
                        continue;
                    var key = Keying.FormatKey(keyProperty, row);
                    if (key == null)
                        continue;
                    map[key] = row;
                }

                tab.CanonByKey = map;
                tab.CanonKnown = true;
            }
            catch (Exception ex)
            {
                Log.LogException(ex, "InfoDebugWindow: canonical read failed.");
                tab.CanonKnown = false;
            }

            tab.Diff = Diffing.Compute(tab.InfoType, tab.Columns, tab.Rows, tab.CanonByKey, tab.CanonKnown);
        }

        private static IList DeserializeRows(ISerializer serializer, Type infoType, byte[] bytes)
        {
            var listType = typeof(List<>).MakeGenericType(infoType);
            var method = typeof(ISerializer).GetMethod(nameof(ISerializer.DeserializeAs))!;
            var generic = method.MakeGenericMethod(listType);
            var value = generic.Invoke(serializer, new object?[] { bytes, null });
            if (value is not IList list)
                throw new InvalidOperationException($"deserialized canonical table for {infoType.Name} was not a list");

            return list;
        }

        private void ShowMessage(string tabTitle, string bodyText)
        {
            UnsubscribeAll();
            _tabStates = null;
            _placeholderTitle = tabTitle;
            _selectedTab = -1;
            _selectedKey = null;
            _narrowDetailOpen = false;
            _detailText.Text = string.Empty;

            ClearChildren(_tabContainer);
            var content = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            var label = new Label { Text = bodyText, AutowrapMode = TextServer.AutowrapMode.WordSmart };
            label.AddThemeFontSizeOverride("font_size", ViewerFontSize);
            content.AddChild(label);
            _tabContainer.AddChild(content);
            _tabContainer.SetTabTitle(0, tabTitle);

            _suppressTabChanged = true;
            _tabContainer.CurrentTab = 0;
            _suppressTabChanged = false;

            UpdateTitle();
            UpdateStatus();
            ApplyResponsive(CurrentViewportSize());
        }

        // ---- tab UI ----------------------------------------------------------

        private void BuildTabUi()
        {
            ClearChildren(_tabContainer);

            foreach (var tab in _tabStates!)
            {
                var content = new VBoxContainer
                {
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                    SizeFlagsVertical = Control.SizeFlags.ExpandFill
                };
                content.AddThemeConstantOverride("separation", 6);

                tab.Note = new Label { Visible = false, AutowrapMode = TextServer.AutowrapMode.WordSmart };
                tab.Note.AddThemeFontSizeOverride("font_size", ViewerFontSize);
                content.AddChild(tab.Note);

                var scroll = new ScrollContainer
                {
                    ClipContents = true,
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                    SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                    HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
                    VerticalScrollMode = ScrollContainer.ScrollMode.Auto
                };
                tab.Grid = new GridContainer { Columns = 1 };
                scroll.AddChild(tab.Grid);
                content.AddChild(scroll);

                _tabContainer.AddChild(content);
                BuildTable(tab);
            }

            for (var i = 0; i < _tabStates.Count; i++)
                _tabContainer.SetTabTitle(i, TabTitle(_tabStates[i]));
        }

        private void BuildTable(TabState tab)
        {
            ClearChildren(tab.Grid);
            tab.RowControls.Clear();

            if (tab.Error != null)
            {
                tab.Grid.Columns = 1;
                SetNote(tab, $"Error: {tab.Error}");
                return;
            }

            var columns = tab.Columns;
            var notes = new List<string>();
            if (columns.Count == 0)
                notes.Add("No readable properties.");
            if (!tab.CanonKnown)
                notes.Add("canon unknown (canonical read unavailable)");

            ComputeVisible(tab);
            var renderCount = Math.Min(tab.Visible.Count, RowCap);
            if (tab.Visible.Count > RowCap)
                notes.Add($"Showing {renderCount} of {tab.Visible.Count} rows (truncated)");

            var editable = tab.Binding != null;
            if (_editingDisabled)
                notes.Add("Editing is disabled after a failed recovery; Reload or Revert to re-enable.");
            else if (!editable)
                notes.Add("Read-only: editing is unavailable for this tab (no editing capability).");

            SetNote(tab, string.Join("\n", notes));

            if (columns.Count == 0)
            {
                tab.Grid.Columns = 1;
                AddHeaderCell(tab.Grid, "#");
                for (var i = 0; i < renderCount; i++)
                    AddPlainCell(tab, FormatCellValue(tab.Visible[i].Row));
                return;
            }

            tab.Grid.Columns = columns.Count + 1;
            AddHeaderCell(tab.Grid, string.Empty);
            foreach (var column in columns)
                AddHeaderCell(tab.Grid, column.Name);

            for (var i = 0; i < renderCount; i++)
                BuildRow(tab, tab.Visible[i], editable);

            HighlightSelectedRow();
        }

        private void ComputeVisible(TabState tab)
        {
            tab.Visible.Clear();
            var diff = tab.Diff;

            if (_changesOnlyMode && tab.CanonKnown && diff != null)
            {
                for (var i = 0; i < tab.Rows.Count; i++)
                {
                    if (diff.ByIndex.ContainsKey(i))
                        tab.Visible.Add(new VisibleRow(i, tab.Rows[i], diff.ByIndex[i].Key));
                }

                foreach (var deletedKey in diff.DeletedKeys)
                {
                    if (tab.CanonByKey.TryGetValue(deletedKey, out var canonRow))
                        tab.Visible.Add(new VisibleRow(-1, canonRow, deletedKey) { Deleted = true });
                }
            }
            else
            {
                for (var i = 0; i < tab.Rows.Count; i++)
                {
                    var key = tab.KeyProperty != null ? Keying.FormatKey(tab.KeyProperty, tab.Rows[i]) : null;
                    tab.Visible.Add(new VisibleRow(i, tab.Rows[i], key ?? string.Empty));
                }
            }
        }

        private void BuildRow(TabState tab, VisibleRow visible, bool editable)
        {
            var controls = new List<Control>();

            var selectRow = new HBoxContainer();
            selectRow.AddThemeConstantOverride("separation", 4);

            var selectButton = new Button
            {
                Text = "#",
                Flat = true,
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(48f, 0)
            };
            selectButton.AddThemeFontSizeOverride("font_size", ViewerFontSize);
            selectButton.Pressed += () => SelectRow(tab, visible);
            if (visible.Deleted)
                selectButton.Disabled = true;
            selectRow.AddChild(selectButton);

            if (editable && !visible.Deleted && !_editingDisabled)
            {
                var deleteButton = new Button
                {
                    Text = "✕",
                    Flat = true,
                    FocusMode = Control.FocusModeEnum.None,
                    CustomMinimumSize = new Vector2(48f, 0)
                };
                deleteButton.AddThemeFontSizeOverride("font_size", ViewerFontSize);
                deleteButton.Pressed += () => OpenDeleteConfirm(tab, visible.Key);
                selectRow.AddChild(deleteButton);
            }

            tab.Grid.AddChild(selectRow);
            controls.Add(selectRow);

            foreach (var column in tab.Columns)
            {
                var control = BuildCell(tab, visible, column, editable);
                controls.Add(control);
            }

            tab.RowControls.Add(controls);
        }

        private Control BuildCell(TabState tab, VisibleRow visible, ColumnInfo column, bool editable)
        {
            object? value = null;
            try
            {
                value = column.Property.GetValue(visible.Row);
            }
            catch (Exception ex)
            {
                value = $"<error: {ex.GetType().Name}>";
            }

            var changed = tab.Diff != null && tab.Diff.ByIndex.TryGetValue(visible.Index, out var diff)
                && diff.ChangedProperties.Contains(column.Name);

            object? canonValue = null;
            if (tab.CanonKnown && !visible.Deleted && visible.Index >= 0
                && tab.CanonByKey.TryGetValue(visible.Key, out var canonRow))
                canonValue = column.Property.GetValue(canonRow);


            var wrapper = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            wrapper.AddThemeConstantOverride("separation", 0);

            Control editor;
            if (editable && !visible.Deleted && !_editingDisabled && column.CanEdit)
                editor = BuildEditor(tab, column, visible, value);
            else
                editor = BuildPlainLabel(value);

            wrapper.AddChild(editor);

            if (changed && tab.CanonKnown)
            {
                var comparison = new Label
                {
                    Text = visible.Deleted || !tab.CanonByKey.ContainsKey(visible.Key)
                        ? "≠ (missing in canon)"
                        : $"≠ {FormatCellValue(canonValue)}",
                    ClipText = true,
                    TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                    MouseFilter = Control.MouseFilterEnum.Ignore
                };
                comparison.AddThemeFontSizeOverride("font_size", ViewerFontSize);
                comparison.Modulate = new Color(1f, 0.65f, 0.35f, 1f);
                wrapper.AddChild(comparison);
            }

            if (changed)
                editor.Modulate = new Color(1f, 0.75f, 0.45f, 1f);

            return wrapper;
        }

        private static Label BuildPlainLabel(object? value)
        {
            var label = new Label
            {
                Text = FormatCellValue(value),
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                CustomMinimumSize = new Vector2(CellWidth, 0),
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            label.AddThemeFontSizeOverride("font_size", ViewerFontSize);
            return label;
        }

        private static string ScalarText(object? value)
        {
            return value switch
            {
                null => string.Empty,
                string text => text,
                bool flag => flag ? "true" : "false",
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? string.Empty
            };
        }

        private Control BuildEditor(TabState tab, ColumnInfo column, VisibleRow visible, object? value)
        {
            switch (column.Kind)
            {
                case EditorKind.Bool:
                {
                    var checkBox = new CheckBox
                    {
                        Text = column.Name,
                        ButtonPressed = value is true,
                        FocusMode = Control.FocusModeEnum.None,
                        CustomMinimumSize = new Vector2(CellWidth, 0)
                    };
                    checkBox.AddThemeFontSizeOverride("font_size", ViewerFontSize);
                    var suppress = true;
                    checkBox.Toggled += pressed =>
                    {
                        if (suppress)
                            return;
                        CommitCell(tab, column, visible.Key, pressed);
                    };
                    suppress = false;
                    return checkBox;
                }
                case EditorKind.Enum:
                {
                    var optionButton = new OptionButton
                    {
                        FocusMode = Control.FocusModeEnum.None,
                        CustomMinimumSize = new Vector2(CellWidth, 0)
                    };
                    optionButton.AddThemeFontSizeOverride("font_size", ViewerFontSize);
                    var names = Enum.GetNames(column.ValueType);
                    foreach (var name in names)
                        optionButton.AddItem(name);
                    var current = value?.ToString();
                    var index = current != null ? Array.IndexOf(names, current) : -1;
                    if (index >= 0)
                        optionButton.Selected = index;

                    var suppress = true;
                    optionButton.ItemSelected += selected =>
                    {
                        if (suppress)
                            return;
                        var name = names[(int)selected];
                        CommitCell(tab, column, visible.Key, Enum.Parse(column.ValueType, name));
                    };
                    suppress = false;
                    return optionButton;
                }
                case EditorKind.Vector2I:
                {
                    var pair = value is Vector2I vector ? vector : default;
                    var row = new HBoxContainer { CustomMinimumSize = new Vector2(CellWidth, 0) };
                    row.AddThemeConstantOverride("separation", 4);
                    var xInput = NewCellLineEdit(pair.X.ToString(CultureInfo.InvariantCulture));
                    var yInput = NewCellLineEdit(pair.Y.ToString(CultureInfo.InvariantCulture));
                    xInput.TextSubmitted += _ => CommitVector2I(tab, column, visible.Key, xInput, yInput);
                    yInput.TextSubmitted += _ => CommitVector2I(tab, column, visible.Key, xInput, yInput);
                    row.AddChild(xInput);
                    row.AddChild(yInput);
                    return row;
                }
                case EditorKind.Number:
                case EditorKind.Text:
                {
                    var lineEdit = NewCellLineEdit(ScalarText(value));
                    lineEdit.TextSubmitted += text => CommitCellText(tab, column, visible.Key, lineEdit, text);
                    return lineEdit;
                }
                default:
                    return BuildPlainLabel(value);
            }
        }

        private static LineEdit NewCellLineEdit(string text)
        {
            var lineEdit = new LineEdit
            {
                Text = text,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(CellWidth / 2f, 0)
            };
            lineEdit.AddThemeFontSizeOverride("font_size", ViewerFontSize);
            return lineEdit;
        }

        private void CommitCellText(TabState tab, ColumnInfo column, string rowKey, LineEdit lineEdit, string text)
        {
            if (!Codec.TryParse(column.ValueType, text, column.Field, out var value, out var error))
            {
                ShowCellError(lineEdit, error);
                return;
            }

            CommitCell(tab, column, rowKey, value);
        }

        private void CommitVector2I(TabState tab, ColumnInfo column, string rowKey, LineEdit xInput, LineEdit yInput)
        {
            if (!Codec.TryParseVector2I(xInput.Text, yInput.Text, out var value, out var error))
            {
                ShowCellError(xInput, error);
                return;
            }

            CommitCell(tab, column, rowKey, value);
        }

        private void CommitCell(TabState tab, ColumnInfo column, string rowKey, object? value)
        {
            if (tab.Binding == null)
                return;

            if (_editingDisabled)
            {
                FlashStatus("Editing is disabled after a failed recovery; Reload or Revert first.");
                return;
            }

            // The old value used for the audit line is the pre-edit value as it exists in the
            // detached candidate (a round-trip clone of the published table), not the canon value:
            // that is exactly the cell the transaction replaces, and it stays correct even when the
            // cell had already been tweaked locally.
            string? oldText = null;
            var result = RunCommit(tab, candidate =>
            {
                var index = Keying.IndexOfKey(tab.InfoType, candidate, rowKey);
                if (index < 0)
                    return "the row is no longer present; refresh and retry";

                oldText = FormatCellValue(column.Property.GetValue(candidate[index]));
                column.Property.SetValue(candidate[index], value);
                return ValidateCandidate(tab, candidate);
            });

            // Audit only a committed edit; a rejected/failed commit must not claim a change.
            if (result?.Success == true && oldText != null)
                Log.LogInfo($"{tab.InfoType.Name}.{rowKey}.{column.Name}: {oldText} → {FormatCellValue(value)}");
        }

        private void CommitAdd()
        {
            var tab = SelectedTabState();
            if (tab == null || tab.Binding == null)
            {
                ShowAddError("Editing is unavailable for this tab.");
                return;
            }

            if (_editingDisabled)
            {
                ShowAddError("Editing is disabled after a failed recovery; Reload or Revert first.");
                return;
            }

            var keyProperty = tab.KeyProperty;
            if (keyProperty == null)
            {
                ShowAddError("This tab has no [InfoKey] column.");
                return;
            }

            if (!Codec.TryParse(keyProperty.PropertyType, _addKeyInput.Text, null, out var keyValue, out var keyError))
            {
                ShowAddError(keyError ?? "invalid key");
                return;
            }

            var instance = Keying.DefaultInstance(tab.InfoType);
            if (instance == null)
            {
                ShowAddError("A new row could not be created (no public parameterless constructor).");
                return;
            }

            var keyText = Keying.FormatKeyValue(keyValue) ?? string.Empty;
            var keyProp = keyProperty;
            var newRow = instance!;
            var result = RunCommit(tab, candidate =>
            {
                if (Keying.IndexOfKey(tab.InfoType, candidate, keyText) >= 0)
                    return $"duplicate key '{keyText}'";

                keyProp.SetValue(newRow, keyValue);
                candidate.Add(newRow);
                return ValidateCandidate(tab, candidate);
            });

            if (result?.Success == true)
            {
                Log.LogInfo($"{tab.InfoType.Name}: added key {keyText}");
                CloseAddPanel();
            }
        }

        private void CommitDelete()
        {
            var tab = SelectedTabState();
            if (tab == null || tab.Binding == null || _pendingDeleteKey == null)
                return;

            var key = _pendingDeleteKey;
            var result = RunCommit(tab, candidate =>
            {
                var index = Keying.IndexOfKey(tab.InfoType, candidate, key);
                if (index < 0)
                    return "the row is no longer present; refresh and retry";

                candidate.RemoveAt(index);
                return ValidateCandidate(tab, candidate);
            });

            if (result?.Success == true)
            {
                Log.LogInfo($"{tab.InfoType.Name}: removed key {key}");

                // Surface the tail-cleanup reminder where the user sees it: a clipboard paste at A1
                // replaces rows, but it does not erase rows the remote sheet still has.
                _confirmText.Text =
                    $"Deleted '{key}' from {tab.InfoType.Name}. " +
                    "Clipboard paste-at-A1 replaces rows but does not erase rows the remote sheet still has — " +
                    "remove the trailing row(s) from the sheet (the info diff removal anchor) before pasting.";
                _confirmPanel.Visible = true;
                _pendingDeleteKey = null;
            }
        }

        private string? ValidateCandidate(TabState tab, IList candidate)
        {
            if (!Keying.ValidateTableKeys(tab.InfoType, candidate, out var keyError))
                return keyError;

            foreach (var column in tab.Columns)
            {
                if (!column.CanEdit)
                    continue;

                foreach (var row in candidate)
                {
                    object? value;
                    try
                    {
                        value = column.Property.GetValue(row);
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    if (!Codec.ValidateValue(column.ValueType, value, column.Field, out var error))
                        return $"{column.Name}: {error}";
                }
            }

            return null;
        }

        private Transaction.Result? RunCommit(TabState tab, Func<IList, string?> mutate)
        {
            if (tab.Binding == null)
                return null;

            var currentRows = new List<object>();
            try
            {
                foreach (var row in tab.Manager.GetAllValue())
                    currentRows.Add(row);
            }
            catch (Exception ex)
            {
                FlashStatus($"Could not read the current table: {ex.GetType().Name}");
                return null;
            }

            _selfCommit = true;
            Transaction.Result result;
            try
            {
                result = Transaction.Commit(
                    tab.Binding.serializer,
                    tab.InfoType,
                    currentRows,
                    mutate,
                    tab.Binding.writeOverride,
                    tab.Manager.Reload);
            }
            finally
            {
                _selfCommit = false;
            }

            if (result.RecoveryFailed)
                _editingDisabled = true;

            if (result.Success)
            {
                // Defer the destructive grid rebuild until the emitting editor callback returns.
                RequestRefresh();
            }
            else
            {
                FlashStatus(result.Error ?? "edit failed");
            }

            return result;
        }

        private void ShowCellError(LineEdit lineEdit, string? error)
        {
            lineEdit.AddThemeColorOverride("font_color", new Color(1f, 0.45f, 0.45f, 1f));
            FlashStatus(error ?? "invalid value");
        }

        private void FlashStatus(string message)
        {
            var tab = SelectedTabState();
            var prefix = tab != null ? tab.DisplayName + " · " : string.Empty;
            _status.Text = prefix + message;
        }

        private void ShowAddError(string message)
        {
            _addError.Text = message;
            _addError.Visible = true;
        }

        private void OpenAddPanel()
        {
            var tab = SelectedTabState();
            _addPanel.Visible = true;
            _addError.Visible = false;
            _addError.Text = string.Empty;
            _confirmPanel.Visible = false;
            _addKeyInput.Text = string.Empty;
            if (tab != null && tab.Binding == null)
                ShowAddError("Editing is unavailable for this tab.");
        }

        private void CloseAddPanel()
        {
            _addPanel.Visible = false;
            _addError.Visible = false;
        }

        private void OpenDeleteConfirm(TabState tab, string key)
        {
            _pendingDeleteKey = key;
            _addPanel.Visible = false;

            var scan = RefScanFor(tab, key);
            var lines = new List<string> { $"Delete '{key}' from {tab.InfoType.Name}?" };
            if (!scan.Complete)
                lines.Add($"Reference checking was incomplete: {scan.Error}");
            else if (scan.Candidates.Count == 0)
                lines.Add("No candidate references found.");
            else
            {
                lines.Add($"{scan.Candidates.Count} potential reference(s):");
                foreach (var candidate in scan.Candidates)
                    lines.Add("  " + candidate);
            }

            _confirmText.Text = string.Join("\n", lines);
            _confirmPanel.Visible = true;
        }

        private RefScan RefScanFor(TabState tab, string key)
        {
            try
            {
                var infoLayer = ResolveInfoLayer();
                if (infoLayer == null)
                {
                    var incomplete = new RefScan { Complete = false, Error = "InfoLayer unavailable; reference checking was incomplete" };
                    return incomplete;
                }

                return Refs.Scan(infoLayer.InfoMap.Values, key);
            }
            catch (Exception ex)
            {
                Log.LogException(ex, "InfoDebugWindow: reference scan failed.");
                return new RefScan { Complete = false, Error = $"{ex.GetType().Name}; reference checking was incomplete" };
            }
        }

        private void CloseConfirmPanel()
        {
            _confirmPanel.Visible = false;
            _pendingDeleteKey = null;
        }

        private static void SetNote(TabState tab, string text)
        {
            tab.Note.Text = text;
            tab.Note.Visible = !string.IsNullOrEmpty(text);
        }

        private static void AddHeaderCell(GridContainer grid, string text)
        {
            var label = new Label
            {
                Text = text,
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                CustomMinimumSize = new Vector2(CellWidth, 0),
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            label.AddThemeFontSizeOverride("font_size", ViewerFontSize);
            grid.AddChild(label);
        }

        private static void AddPlainCell(TabState tab, string text)
        {
            var label = new Label
            {
                Text = text,
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                CustomMinimumSize = new Vector2(CellWidth, 0),
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            label.AddThemeFontSizeOverride("font_size", ViewerFontSize);
            tab.Grid.AddChild(label);
        }

        private static string TabTitle(TabState tab)
        {
            // Short, stable tab labels: the tab strip must stay reachable on a phone-width card.
            // Counts, timestamps and error state belong on the status line (see UpdateStatus).
            return tab.DisplayName;
        }

        // ---- selection / detail ---------------------------------------------

        private void SelectRow(TabState tab, VisibleRow visible)
        {
            _selectedKey = visible.Key;
            _narrowDetailOpen = true;
            _detailText.Text = SerializeDetail(tab, visible);
            HighlightSelectedRow();
            ApplyResponsive(CurrentViewportSize());
        }

        private void OnBack()
        {
            _narrowDetailOpen = false;
            ApplyResponsive(CurrentViewportSize());
        }

        private void HighlightSelectedRow()
        {
            var tab = SelectedTabState();
            if (tab == null)
                return;

            for (var r = 0; r < tab.RowControls.Count; r++)
            {
                var key = r < tab.Visible.Count ? tab.Visible[r].Key : null;
                var selected = key != null && string.Equals(key, _selectedKey, StringComparison.Ordinal);
                var color = selected ? new Color(1f, 1f, 0.4f, 1f) : new Color(1f, 1f, 1f, 1f);
                foreach (var control in tab.RowControls[r])
                    control.Modulate = color;
            }
        }

        private string SerializeDetail(TabState tab, VisibleRow visible)
        {
            if (visible.Row == null)
                return "null";

            var header = visible.Deleted
                ? $"# deleted canon row: {visible.Key}\n"
                : $"# {tab.InfoType.Name} · {visible.Key}\n";

            try
            {
                var body = System.Text.Json.JsonSerializer.Serialize(visible.Row, visible.Row.GetType(), _jsonOptions);
                return header + body;
            }
            catch (Exception ex)
            {
                return $"{header}<detail serialization failed: {ex.GetType().Name}: {ex.Message}>";
            }
        }

        // ---- tabs ------------------------------------------------------------

        private void OnTabChanged(long tab)
        {
            if (_suppressTabChanged)
                return;

            var index = (int)tab;
            if (_tabStates == null || index < 0 || index >= _tabStates.Count)
                return;

            _selectedTab = index;
            _selectedKey = null;
            _narrowDetailOpen = false;
            _detailText.Text = string.Empty;
            _addPanel.Visible = false;
            _confirmPanel.Visible = false;
            _pendingDeleteKey = null;
            HighlightSelectedRow();
            UpdateTitle();
            UpdateStatus();
            ApplyResponsive(CurrentViewportSize());
        }

        private void OnChangesOnlyToggled(bool pressed)
        {
            _changesOnlyMode = pressed;
            var tab = SelectedTabState();
            if (tab == null)
                return;

            BuildTabUi();
            HighlightSelectedRow();
            UpdateStatus();
        }

        private TabState? SelectedTabState()
        {
            if (_tabStates == null || _selectedTab < 0 || _selectedTab >= _tabStates.Count)
                return null;
            return _tabStates[_selectedTab];
        }

        private Type? SelectedType()
        {
            return SelectedTabState()?.ManagerType;
        }

        private int IndexOfType(Type? managerType)
        {
            if (managerType != null && _tabStates != null)
            {
                for (var i = 0; i < _tabStates.Count; i++)
                {
                    if (_tabStates[i].ManagerType == managerType)
                        return i;
                }
            }

            return 0;
        }

        // ---- reload / revert -------------------------------------------------

        private void ReloadSelected()
        {
            var tab = SelectedTabState();
            if (tab == null)
                return;

            bool ok;
            try
            {
                ok = tab.Manager.Reload();
            }
            catch (Exception ex)
            {
                Log.LogException(ex, "InfoDebugWindow: Reload threw.");
                FlashStatus($"Reload failed: {ex.GetType().Name}");
                return;
            }

            if (!ok)
                FlashStatus("Reload failed; the previous table was kept.");
            else
                FlashStatus($"reloaded {tab.InfoType.Name}");
            RequestRefresh();
        }

        private void RevertSelected()
        {
            var tab = SelectedTabState();
            if (tab == null)
                return;

            bool ok;
            try
            {
                ok = tab.Manager.Revert();
            }
            catch (Exception ex)
            {
                Log.LogException(ex, "InfoDebugWindow: Revert threw.");
                FlashStatus($"Revert failed: {ex.GetType().Name}");
                return;
            }

            if (!ok)
                FlashStatus("Revert failed; the previous table was kept.");
            else
                _editingDisabled = false;
            RequestRefresh();
        }

        // ---- clipboard -------------------------------------------------------

        private void CopyFlatToClipboard()
        {
            var tab = SelectedTabState();
            if (tab == null)
                return;

            if (tab.Binding == null)
            {
                FlashStatus("Copy TSV is unavailable for this tab.");
                return;
            }

            var fullRows = new List<object>(tab.Rows);
            if (!Tsv.CopyFlat(tab.Binding.projectFlatSheet, fullRows, text => DisplayServer.ClipboardSet(text), out var error))
            {
                FlashStatus($"Copy TSV refused: {error}");
                return;
            }

            FlashStatus($"copied {fullRows.Count} rows (paste at A1)");
        }

        private void CopyChangesToClipboard()
        {
            var tab = SelectedTabState();
            if (tab == null)
                return;

            if (tab.Binding == null || tab.Diff == null || !tab.CanonKnown)
            {
                FlashStatus("Copy changes is unavailable (canonical table unknown).");
                return;
            }

            var text = Tsv.BuildChanges(tab.InfoType, tab.Columns, tab.Rows, tab.CanonByKey, tab.Diff);
            DisplayServer.ClipboardSet(text);
            FlashStatus($"copied changes ({tab.Diff.ChangedCells} changed cell(s))");
        }

        // ---- title / status --------------------------------------------------

        private void UpdateTitle()
        {
            var title = _placeholderTitle == "InfoLayer unavailable"
                ? "Info Viewer"
                : $"Info Viewer · {_placeholderTitle}";

            if (_tabStates != null)
            {
                var anyUnknown = false;
                var anyModified = false;
                foreach (var tab in _tabStates)
                {
                    if (tab.Binding != null && !tab.CanonKnown)
                        anyUnknown = true;
                    if (tab.Diff != null && tab.CanonKnown && tab.Diff.ChangedCells > 0)
                        anyModified = true;
                }

                var qualifier = anyUnknown ? "canon unknown" : anyModified ? "modified" : "canon";
                title = $"Info Viewer · {qualifier}";
            }

            Root.SetMeta("info_debug_window_title", title);
        }

        private void UpdateStatus()
        {
            ApplyResponsive(CurrentViewportSize());
            var tab = SelectedTabState();
            if (tab == null)
            {
                _status.Text = _placeholderTitle;
                SetActionAvailability(null);
                return;
            }

            if (tab.Error != null)
            {
                _status.Text = $"{tab.DisplayName} · error · refreshed {tab.Timestamp}";
                SetActionAvailability(tab);
                return;
            }

            var changed = tab.CanonKnown && tab.Diff != null ? tab.Diff.ChangedCells : 0;
            var canon = tab.CanonKnown ? "canon" : "canon unknown";
            _status.Text = $"{tab.DisplayName} · {tab.TotalRows} rows · {changed} changed · {canon} · refreshed {tab.Timestamp}";
            SetActionAvailability(tab);
        }

        private void SetActionAvailability(TabState? tab)
        {
            var editable = tab != null && tab.Binding != null && !_editingDisabled;
            _reloadButton.Disabled = tab == null;
            _revertButton.Disabled = tab == null;
            _addButton.Disabled = !editable;
            _copyFlatButton.Disabled = tab == null || tab.Binding == null;
            _copyChangesButton.Disabled = tab == null || tab.Binding == null || !tab.CanonKnown;
        }

        // ---- responsive ------------------------------------------------------

        private void ApplyResponsive(Vector2 size)
        {
            var selected = SelectedTabState();
            var hasSelection = selected != null && _selectedKey != null
                && selected.Visible.Exists(v => string.Equals(v.Key, _selectedKey, StringComparison.Ordinal));
            // Aspect decides the axis: a portrait viewport (height > width, equality is landscape)
            // stacks; flip the single body container in place instead of reparenting/rebuilding panes.
            var portrait = size.Y > size.X;
            var wide = size.X >= WideBreakpointWidth;
            _body.Vertical = portrait;

            if (!hasSelection)
            {
                _tabContainer.Visible = true;
                _detailPane.Visible = false;
                _backButton.Visible = false;
                return;
            }

            if (portrait || wide)
            {
                _tabContainer.Visible = true;
                _detailPane.Visible = true;
                _backButton.Visible = false;
                return;
            }

            _tabContainer.Visible = !_narrowDetailOpen;
            _detailPane.Visible = _narrowDetailOpen;
            _backButton.Visible = _narrowDetailOpen;
        }

        private static void ClearChildren(Node node)
        {
            foreach (var child in node.GetChildren())
            {
                node.RemoveChild(child);
                child.QueueFree();
            }
        }
    }

    /// <summary>One rendered table row: current index (or -1 for a deleted tombstone), row, key.</summary>
    private sealed class VisibleRow
    {
        public VisibleRow(int index, object row, string key)
        {
            Index = index;
            Row = row;
            Key = key;
        }

        public int Index { get; }
        public object Row { get; }
        public string Key { get; }
        public bool Deleted { get; init; }
    }

    /// <summary>Snapshot of one manager tab: manager, binding, full rows, canon diff, status.</summary>
    private sealed class TabState
    {
        public TabState(Type managerType, string displayName, string timestamp)
        {
            ManagerType = managerType;
            DisplayName = displayName;
            Timestamp = timestamp;
        }

        public Type ManagerType { get; }

        public string DisplayName { get; }

        public string Timestamp { get; }

        public IInfoManager Manager { get; set; } = null!;

        public InfoDebugTableBinding? Binding { get; set; }

        public Type InfoType { get; set; } = typeof(object);

        public List<object> Rows { get; set; } = new();

        public int TotalRows { get; set; }

        public List<ColumnInfo> Columns { get; set; } = new();

        public Dictionary<string, object> CanonByKey { get; set; } = new(StringComparer.Ordinal);

        public bool CanonKnown { get; set; }

        public DiffResult? Diff { get; set; }

        public string? Error { get; set; }

        public PropertyInfo? KeyProperty => Keying.FindKey(InfoType);

        public Label Note { get; set; } = null!;

        public GridContainer Grid { get; set; } = null!;

        public readonly List<VisibleRow> Visible = new();

        public readonly List<List<Control>> RowControls = new();
    }
}
