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
        Vector2I,

        /// <summary>
        /// A one-level <c>List&lt;TChild&gt;</c> column at parent depth. It is not edited in place;
        /// a resolved binding turns it into a drill-down child table instead of a read-only label.
        /// </summary>
        ChildList
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

        /// <summary>Element type when <see cref="Kind"/> is <see cref="EditorKind.ChildList"/>.</summary>
        public Type? ChildElementType;

        public bool CanEdit => Kind != EditorKind.None && Property.GetSetMethod(false) != null;
    }

    /// <summary>
    /// Reflected columns for <paramref name="infoType"/> carrying their editing support, at parent
    /// depth (one-level child lists are detected). Named without an overload so the reflection-based
    /// test seam stays unambiguous.
    /// </summary>
    private static List<ColumnInfo> DescribeColumns(Type infoType)
        => BuildColumns(infoType, allowChildLists: true);

    /// <summary>
    /// Reflected columns with one-level child-list detection enabled only at parent depth. A
    /// <c>List&lt;TChild&gt;</c> at child depth stays read-only: the child table is one level only.
    /// </summary>
    private static List<ColumnInfo> BuildColumns(Type infoType, bool allowChildLists)
        => ReadableColumns(infoType).Select(property => DescribeColumn(property, allowChildLists)).ToList();

    private static ColumnInfo DescribeColumn(PropertyInfo property, bool allowChildLists)
    {
        var type = property.PropertyType;
        var field = property.GetCustomAttribute<InfoFieldAttribute>();
        var kind = KindOf(type);
        Type? childElementType = null;

        // One level only: a generic List<T> whose element is a class other than string. Arrays,
        // List<string>, structs (incl. Vector2I), and every other collection stay read-only JSON.
        if (allowChildLists && TryChildElementType(type, out var element))
        {
            kind = EditorKind.ChildList;
            childElementType = element;
        }

        return new ColumnInfo
        {
            Property = property,
            Name = property.Name,
            ValueType = type,
            IsKey = property.GetCustomAttribute<InfoKeyAttribute>() != null,
            Field = field,
            Kind = kind,
            ChildElementType = childElementType
        };
    }

    /// <summary>
    /// True only for exactly <c>List&lt;T&gt;</c> with a reference-type element other than
    /// <see cref="string"/>. Mirrors the schema builder's collection classification, so parity with
    /// the sheet converter is structural rather than a reflected member inspection.
    /// </summary>
    private static bool TryChildElementType(Type type, out Type elementType)
    {
        elementType = typeof(object);
        if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(List<>))
            return false;

        var element = type.GetGenericArguments()[0];
        if (!element.IsClass || element == typeof(string))
            return false;

        elementType = element;
        return true;
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

        /// <summary>
        /// Every public instance property carrying <see cref="InfoKeyAttribute"/>, in declaration
        /// order. Used to enforce "exactly one" child identity: zero keys means no natural identity,
        /// two or more is ambiguous and must not be silently resolved to the first.
        /// </summary>
        public static List<PropertyInfo> FindKeys(Type infoType)
        {
            var keys = new List<PropertyInfo>();
            foreach (var property in infoType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetCustomAttribute<InfoKeyAttribute>() != null)
                    keys.Add(property);
            }

            return keys;
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

    /// <summary>How a one-level child list identifies its rows.</summary>
    private enum ChildIdentitySource
    {
        InfoKey,
        Composition,
        Positional
    }

    /// <summary>
    /// The resolved identity rule for one child element type: an <see cref="InfoKeyAttribute"/>
    /// property, an explicit composition callback property, or explicit positional mode.
    /// </summary>
    private sealed class ChildIdentity
    {
        public Type ElementType = typeof(object);
        public ChildIdentitySource Source;
        public PropertyInfo? Property;

        public bool Positional => Property == null;

        /// <summary>Sheet/status label: the property name, or the explicit positional form.</summary>
        public string Label => Property != null ? Property.Name : "items[index]";
    }

    /// <summary>Result of validating one list's declared child identity over all its rows.</summary>
    private sealed class ChildListIdentityResult
    {
        public bool Ok = true;
        public bool Blank;
        public bool Duplicate;
        public string? Error;
    }

    /// <summary>One-level child list identity: resolution, over-the-list validation, row labels.</summary>
    private static class Children
    {
        public static ChildIdentity Resolve(Type elementType, Func<Type, string?>? resolver)
        {
            // Exactly one [InfoKey] wins; two or more is ambiguous, never "the first".
            var keys = Keying.FindKeys(elementType);
            if (keys.Count == 1)
                return new ChildIdentity
                {
                    ElementType = elementType,
                    Source = ChildIdentitySource.InfoKey,
                    Property = keys[0]
                };

            if (resolver != null)
            {
                string? name = null;
                try
                {
                    name = resolver(elementType);
                }
                catch (Exception ex)
                {
                    Log.LogException(ex, "InfoDebugWindow: child identity resolver threw.");
                }

                if (!string.IsNullOrWhiteSpace(name))
                {
                    var property = elementType.GetProperty(name!, BindingFlags.Public | BindingFlags.Instance);
                    if (property != null && property.GetIndexParameters().Length == 0)
                        return new ChildIdentity
                        {
                            ElementType = elementType,
                            Source = ChildIdentitySource.Composition,
                            Property = property
                        };
                }
            }

            return new ChildIdentity
            {
                ElementType = elementType,
                Source = ChildIdentitySource.Positional,
                Property = null
            };
        }

        /// <summary>
        /// Validates a declared identity over the whole sibling list: readable, public-settable,
        /// editor-supported scalar, present and unique. A zero numeric value is valid; a blank or
        /// duplicate value is a visible error and never a positional fallback.
        /// </summary>
        public static ChildListIdentityResult ValidateList(ChildIdentity identity, IList? list, string path)
        {
            var result = new ChildListIdentityResult();
            if (identity.Positional)
                return result;

            var property = identity.Property!;
            var kind = KindOf(property.PropertyType);
            if (kind is EditorKind.None or EditorKind.ChildList || property.GetSetMethod(false) == null)
            {
                result.Ok = false;
                result.Error = $"{path}: child identity '{property.Name}' is not an editor-supported scalar";
                return result;
            }

            if (list == null)
                return result;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < list.Count; i++)
            {
                var child = list[i];
                if (child == null)
                {
                    result.Ok = false;
                    result.Blank = true;
                    result.Error ??= $"{path}: a child row is null";
                    continue;
                }

                object? value;
                try
                {
                    value = property.GetValue(child);
                }
                catch (Exception ex)
                {
                    result.Ok = false;
                    result.Error ??= $"{path}: child identity '{property.Name}' could not be read ({ex.GetType().Name})";
                    continue;
                }

                var text = Keying.FormatKeyValue(value);
                if (text == null || (value is string raw && string.IsNullOrWhiteSpace(raw)))
                {
                    result.Ok = false;
                    result.Blank = true;
                    result.Error ??= $"{path}: child identity '{property.Name}' is blank at index {i}";
                    continue;
                }

                if (!seen.Add(text))
                {
                    result.Ok = false;
                    result.Duplicate = true;
                    result.Error ??= $"{path}: duplicate child identity '{text}'";
                }
            }

            return result;
        }

        /// <summary>Deterministic row label: identity value, or the explicit <c>items[index]</c>.</summary>
        public static string Label(ChildIdentity identity, object? row, int index)
        {
            var name = identity.Property != null ? identity.Property.Name : "items";
            if (identity.Positional || row == null)
                return $"{name}[{index}]";

            try
            {
                return Keying.FormatKeyValue(identity.Property!.GetValue(row)) ?? $"{name}[{index}]";
            }
            catch (Exception)
            {
                return $"{name}[{index}]";
            }
        }

        /// <summary>The runtime list behind one <c>List&lt;TChild&gt;</c> column, or null.</summary>
        public static IList? ListOf(PropertyInfo property, object? row)
        {
            if (row == null)
                return null;

            try
            {
                return property.GetValue(row) as IList;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>One changed/added/deleted child row of a one-level child list.</summary>
    private sealed class ChildRowDiff
    {
        public string Identity = string.Empty;
        public int Index = -1;
        public bool Added;
        public bool Deleted;
        public readonly HashSet<string> ChangedColumns = new(StringComparer.Ordinal);
        public int ChangedCells;
    }

    /// <summary>One-level child list diff for a single parent row and child property.</summary>
    private sealed class ChildListDiff
    {
        public string PropertyName = string.Empty;
        public string IdentityProperty = "items[index]";
        public bool Positional;
        public bool IdentityError;
        public string? IdentityMessage;

        /// <summary>The container itself changed (null ↔ empty/non-null) — one structural change.</summary>
        public bool Structural;

        /// <summary>Membership and values are identical but the order changed — one list change.</summary>
        public bool OrderOnly;

        public int ChangedCells;
        public readonly List<ChildRowDiff> Rows = new();
        public readonly List<ChildRowDiff> Deleted = new();

        public bool AnyChange => ChangedCells > 0 || Structural || OrderOnly || IdentityError;
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
            => ComputeSectioned(infoType, columns, current, canonByKey, canonKnown, null);

        /// <summary>
        /// Fresh-canon diff that also compares every one-level child list. Rows are matched by the
        /// resolved child identity (or explicitly positionally); the parent <c>List</c> column is
        /// never counted as a cell, only its leaves are.
        /// </summary>
        public static DiffResult ComputeSectioned(
            Type infoType,
            IReadOnlyList<ColumnInfo> columns,
            IReadOnlyList<object> current,
            IReadOnlyDictionary<string, object> canonByKey,
            bool canonKnown,
            Func<Type, string?>? resolveChildIdentity)
        {
            var result = new DiffResult { CanonKnown = canonKnown };
            if (!canonKnown)
                return result;

            var keyProperty = Keying.FindKey(infoType);
            var dataColumns = columns.Where(static column => !column.IsKey && column.Kind != EditorKind.ChildList).ToList();
            var childColumns = columns.Where(static column => column.Kind == EditorKind.ChildList).ToList();
            var currentKeys = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < current.Count; i++)
            {
                var row = current[i];
                var key = keyProperty != null ? Keying.FormatKey(keyProperty, row) : null;
                if (key != null)
                    currentKeys.Add(key);

                var diff = new RowDiff { Row = row, Key = key ?? string.Empty };
                object? canonRow = null;
                var hasCanon = key != null;
                if (hasCanon)
                    hasCanon = canonByKey.TryGetValue(key!, out canonRow);

                if (hasCanon)
                {
                    foreach (var column in dataColumns)
                    {
                        if (CellsEqual(column.Property, row, canonRow!))
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

                foreach (var column in childColumns)
                {
                    var childDiff = ComputeChildList(column, row, hasCanon ? canonRow : null, added: !hasCanon, resolveChildIdentity);
                    if (!childDiff.AnyChange)
                        continue;

                    diff.ChildDiffs[column.Name] = childDiff;
                    result.ChangedCells += childDiff.ChangedCells;
                }

                if (diff.ChangedCells > 0 || diff.Added || diff.ChildDiffs.Count > 0)
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
                var diff = new RowDiff { Row = pair.Value, Key = pair.Key };
                diff.ChangedCells += dataColumns.Count;

                foreach (var column in childColumns)
                {
                    var childDiff = ComputeChildList(column, null, pair.Value, added: false, resolveChildIdentity);
                    if (!childDiff.AnyChange)
                        continue;

                    diff.ChildDiffs[column.Name] = childDiff;
                    diff.ChangedCells += childDiff.ChangedCells;
                }

                result.ChangedCells += diff.ChangedCells;
                result.DeletedByKey[pair.Key] = diff;
            }

            result.DeletedKeys.Sort(StringComparer.Ordinal);
            return result;
        }

        private static ChildListDiff ComputeChildList(
            ColumnInfo column,
            object? currentRow,
            object? canonRow,
            bool added,
            Func<Type, string?>? resolveChildIdentity)
        {
            var elementType = column.ChildElementType ?? typeof(object);
            var identity = Children.Resolve(elementType, resolveChildIdentity);

            // Only readable scalar cells participate: a nested list at child depth stays read-only
            // JSON and is neither compared nor counted as a changed cell.
            var childColumns = BuildColumns(elementType, allowChildLists: false)
                .Where(static child => child.Kind != EditorKind.None)
                .ToList();

            var diff = new ChildListDiff
            {
                PropertyName = column.Name,
                IdentityProperty = identity.Label,
                Positional = identity.Positional
            };

            var current = Children.ListOf(column.Property, currentRow);
            var canon = added ? null : Children.ListOf(column.Property, canonRow);

            // An added parent contributes every current child as added; a deleted parent contributes
            // every canon child as deleted. Otherwise null vs non-null is a structural change.
            if (added)
            {
                AddAll(diff, identity, childColumns, current, added: true);
                return diff;
            }

            if (currentRow == null)
            {
                AddAll(diff, identity, childColumns, canon, added: false);
                return diff;
            }

            if (current == null || canon == null)
            {
                if (!(current == null && canon == null))
                {
                    diff.Structural = true;
                    diff.ChangedCells += 1;
                    AddAll(diff, identity, childColumns, current, added: true);
                    AddAll(diff, identity, childColumns, canon, added: false);
                }

                return diff;
            }

            var path = $"{column.Name}";
            var currentIdentity = Children.ValidateList(identity, current, path);
            var canonIdentity = Children.ValidateList(identity, canon, path);
            if (!currentIdentity.Ok || !canonIdentity.Ok)
            {
                // Visible identity error. Never fall back to positional matching, and never claim
                // a zero-change canon comparison.
                diff.IdentityError = true;
                diff.IdentityMessage = currentIdentity.Error ?? canonIdentity.Error
                    ?? $"{path}: child identity is invalid";
                return diff;
            }

            if (identity.Positional)
                MatchPositional(diff, identity, childColumns, current, canon);
            else
                MatchNatural(diff, identity, childColumns, current, canon);

            return diff;
        }

        private static void AddAll(
            ChildListDiff diff, ChildIdentity identity, IReadOnlyList<ColumnInfo> childColumns, IList? rows, bool added)
        {
            if (rows == null)
                return;

            for (var i = 0; i < rows.Count; i++)
            {
                var rowDiff = new ChildRowDiff
                {
                    Identity = Children.Label(identity, rows[i], i),
                    Index = i,
                    Added = added,
                    Deleted = !added,
                    ChangedCells = childColumns.Count
                };

                foreach (var column in childColumns)
                    rowDiff.ChangedColumns.Add(column.Name);

                (added ? diff.Rows : diff.Deleted).Add(rowDiff);
                diff.ChangedCells += childColumns.Count;
            }
        }

        private static void MatchNatural(
            ChildListDiff diff, ChildIdentity identity, IReadOnlyList<ColumnInfo> childColumns, IList current, IList canon)
        {
            var canonByIdentity = new Dictionary<string, (object Row, int Index)>(StringComparer.Ordinal);
            for (var j = 0; j < canon.Count; j++)
            {
                var id = Children.Label(identity, canon[j], j);
                if (!canonByIdentity.ContainsKey(id))
                    canonByIdentity[id] = (canon[j]!, j);
            }

            var matched = new HashSet<int>();
            for (var i = 0; i < current.Count; i++)
            {
                var id = Children.Label(identity, current[i], i);
                var rowDiff = new ChildRowDiff { Identity = id, Index = i };

                if (canonByIdentity.TryGetValue(id, out var entry) && matched.Add(entry.Index))
                {
                    CompareColumns(childColumns, current[i], entry.Row, rowDiff);
                    if (rowDiff.ChangedCells > 0)
                    {
                        diff.Rows.Add(rowDiff);
                        diff.ChangedCells += rowDiff.ChangedCells;
                    }
                }
                else
                {
                    rowDiff.Added = true;
                    foreach (var column in childColumns)
                        rowDiff.ChangedColumns.Add(column.Name);
                    rowDiff.ChangedCells = childColumns.Count;
                    diff.Rows.Add(rowDiff);
                    diff.ChangedCells += rowDiff.ChangedCells;
                }
            }

            for (var j = 0; j < canon.Count; j++)
            {
                if (matched.Contains(j))
                    continue;

                var rowDiff = new ChildRowDiff
                {
                    Identity = Children.Label(identity, canon[j], j),
                    Index = j,
                    Deleted = true,
                    ChangedCells = childColumns.Count
                };
                foreach (var column in childColumns)
                    rowDiff.ChangedColumns.Add(column.Name);

                diff.Deleted.Add(rowDiff);
                diff.ChangedCells += childColumns.Count;
            }

            // Pure reorder: identical membership and values, different order => one list change.
            if (diff.ChangedCells == 0)
            {
                for (var i = 0; i < current.Count && i < canon.Count; i++)
                {
                    if (!string.Equals(
                            Children.Label(identity, current[i], i),
                            Children.Label(identity, canon[i], i),
                            StringComparison.Ordinal))
                    {
                        diff.OrderOnly = true;
                        diff.ChangedCells += 1;
                        break;
                    }
                }
            }
        }

        private static void MatchPositional(
            ChildListDiff diff, ChildIdentity identity, IReadOnlyList<ColumnInfo> childColumns, IList current, IList canon)
        {
            var max = Math.Max(current.Count, canon.Count);
            for (var i = 0; i < max; i++)
            {
                var inCurrent = i < current.Count;
                var inCanon = i < canon.Count;
                var rowDiff = new ChildRowDiff { Identity = Children.Label(identity, inCurrent ? current[i] : null, i), Index = i };

                if (inCurrent && inCanon)
                {
                    CompareColumns(childColumns, current[i], canon[i], rowDiff);
                    if (rowDiff.ChangedCells > 0)
                    {
                        diff.Rows.Add(rowDiff);
                        diff.ChangedCells += rowDiff.ChangedCells;
                    }
                }
                else if (inCurrent)
                {
                    rowDiff.Added = true;
                    rowDiff.ChangedCells = childColumns.Count;
                    foreach (var column in childColumns)
                        rowDiff.ChangedColumns.Add(column.Name);
                    diff.Rows.Add(rowDiff);
                    diff.ChangedCells += rowDiff.ChangedCells;
                }
                else
                {
                    rowDiff.Deleted = true;
                    rowDiff.ChangedCells = childColumns.Count;
                    foreach (var column in childColumns)
                        rowDiff.ChangedColumns.Add(column.Name);
                    diff.Deleted.Add(rowDiff);
                    diff.ChangedCells += rowDiff.ChangedCells;
                }
            }
        }

        private static void CompareColumns(
            IReadOnlyList<ColumnInfo> columns, object? left, object? right, ChildRowDiff rowDiff)
        {
            foreach (var column in columns)
            {
                if (CellsEqual(column.Property, left!, right!))
                    continue;

                rowDiff.ChangedColumns.Add(column.Name);
                rowDiff.ChangedCells++;
            }
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

    /// <summary>
    /// Engine-free candidate validation shared by the parent table and every one-level child list.
    /// A null child list is allowed (a child operation may initialize it); a null child row, a
    /// blank/duplicate declared identity, or a scalar/constraint failure is an error that names the
    /// parent property, child index and column.
    /// </summary>
    private static class Validation
    {
        public static string? ValidateCandidate(
            Type infoType,
            IReadOnlyList<ColumnInfo> columns,
            IList candidate,
            Func<Type, string?>? resolveChildIdentity)
        {
            if (!Keying.ValidateTableKeys(infoType, candidate, out var keyError))
                return keyError;

            foreach (var column in columns)
            {
                if (column.Kind == EditorKind.ChildList)
                {
                    var childError = ValidateChildColumn(column, candidate, resolveChildIdentity);
                    if (childError != null)
                        return childError;
                    continue;
                }

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

        public static string? ValidateChildColumn(
            ColumnInfo column, IList candidate, Func<Type, string?>? resolveChildIdentity)
        {
            var elementType = column.ChildElementType ?? typeof(object);
            var childColumns = BuildColumns(elementType, allowChildLists: false);
            var identity = Children.Resolve(elementType, resolveChildIdentity);

            for (var i = 0; i < candidate.Count; i++)
            {
                var parent = candidate[i];
                var list = Children.ListOf(column.Property, parent);
                if (list == null)
                    continue; // a null list is allowed; child operations may initialize it

                var identityResult = Children.ValidateList(identity, list, column.Name);
                if (!identityResult.Ok)
                    return $"{column.Name}: {identityResult.Error}";

                for (var c = 0; c < list.Count; c++)
                {
                    var child = list[c];
                    if (child == null)
                        return $"{column.Name}[{c}]: a child row is null";

                    foreach (var childColumn in childColumns)
                    {
                        if (!childColumn.CanEdit)
                            continue;

                        object? value;
                        try
                        {
                            value = childColumn.Property.GetValue(child);
                        }
                        catch (Exception ex)
                        {
                            return $"{column.Name}[{c}].{childColumn.Name}: value could not be read ({ex.GetType().Name})";
                        }

                        if (!Codec.ValidateValue(childColumn.ValueType, value, childColumn.Field, out var error))
                            return $"{column.Name}[{c}].{childColumn.Name}: {error}";
                    }
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Engine-free child mutation body shared by every child handler: the reference guard first,
    /// then re-resolve the detached parent by key and the child by declared identity/index, mutate
    /// only the detached candidate, and revalidate. Also drives the managed tests directly.
    /// </summary>
    private static class ChildEdit
    {
        /// <summary>Re-resolves the displayed published parent reference and requires it to match.</summary>
        public static string? Guard(TabState tab, string parentKey, object parentReference, IList current)
        {
            var index = Keying.IndexOfKey(tab.InfoType, current, parentKey);
            if (index < 0)
                return "the parent row is no longer present; refresh and retry";
            if (!ReferenceEquals(current[index], parentReference))
                return "the displayed parent row changed since this editor was shown; refresh and retry";
            return null;
        }

        /// <summary>
        /// The index of the targeted child in <paramref name="list"/>. Rows are matched by the
        /// declared identity when the whole list is currently valid; when the list's identity is
        /// invalid (or positional), the explicit <paramref name="fallbackIndex"/> is used — this is
        /// the in-window <em>positional repair</em> path, and validation still refuses any write
        /// until the complete sibling list becomes valid again.
        /// </summary>
        public static int IndexOfChild(ChildIdentity identity, IList list, string label, int fallbackIndex)
        {
            if (identity.Positional || !Children.ValidateList(identity, list, "items").Ok)
                return fallbackIndex >= 0 && fallbackIndex < list.Count ? fallbackIndex : -1;

            for (var i = 0; i < list.Count; i++)
            {
                if (string.Equals(Children.Label(identity, list[i], i), label, StringComparison.Ordinal))
                    return i;
            }

            return -1;
        }

        /// <summary>Scans the whole list for an equal declared identity (independent of validity).</summary>
        public static bool HasIdentity(ChildIdentity identity, IList list, string value)
        {
            if (identity.Positional)
                return false;

            for (var i = 0; i < list.Count; i++)
            {
                if (string.Equals(Children.Label(identity, list[i], i), value, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        public static string? SetCell(
            TabState tab, ChildContext context, string label, int index, ColumnInfo column, object? value,
            IList candidate, IList current, out string? oldText)
        {
            oldText = null;
            var guard = Guard(tab, context.ParentKey, context.ParentReference, current);
            if (guard != null)
                return guard;

            var parentIndex = Keying.IndexOfKey(tab.InfoType, candidate, context.ParentKey);
            if (parentIndex < 0)
                return "the parent row is no longer present; refresh and retry";

            var list = Children.ListOf(context.Column.Property, candidate[parentIndex]);
            if (list == null)
                return "the child list is no longer present; refresh and retry";

            var childIndex = IndexOfChild(context.Identity, list, label, index);
            if (childIndex < 0)
                return "the child row is no longer present; refresh and retry";

            oldText = FormatCellValue(column.Property.GetValue(list[childIndex]));
            column.Property.SetValue(list[childIndex], value);
            return Validation.ValidateCandidate(tab.InfoType, tab.Columns, candidate, tab.ResolveChildIdentity);
        }

        public static string? Add(
            TabState tab, ChildContext context, object? identityValue, object instance,
            IList candidate, IList current)
        {
            var guard = Guard(tab, context.ParentKey, context.ParentReference, current);
            if (guard != null)
                return guard;

            var parentIndex = Keying.IndexOfKey(tab.InfoType, candidate, context.ParentKey);
            if (parentIndex < 0)
                return "the parent row is no longer present; refresh and retry";

            var parent = candidate[parentIndex];
            var list = Children.ListOf(context.Column.Property, parent);
            if (list == null)
            {
                var listType = typeof(List<>).MakeGenericType(context.ElementType);
                list = (IList)Activator.CreateInstance(listType)!;
                context.Column.Property.SetValue(parent, list);
            }

            if (!context.Identity.Positional)
            {
                context.Identity.Property!.SetValue(instance, identityValue);
                var identityText = Keying.FormatKeyValue(identityValue);
                if (identityText != null && HasIdentity(context.Identity, list, identityText))
                    return $"duplicate child identity '{identityText}'";
            }

            list.Add(instance);
            return Validation.ValidateCandidate(tab.InfoType, tab.Columns, candidate, tab.ResolveChildIdentity);
        }

        public static string? Remove(
            TabState tab, ChildContext context, string label, int index, IList candidate, IList current)
        {
            var guard = Guard(tab, context.ParentKey, context.ParentReference, current);
            if (guard != null)
                return guard;

            var parentIndex = Keying.IndexOfKey(tab.InfoType, candidate, context.ParentKey);
            if (parentIndex < 0)
                return "the parent row is no longer present; refresh and retry";

            var list = Children.ListOf(context.Column.Property, candidate[parentIndex]);
            if (list == null)
                return "the child list is no longer present; refresh and retry";

            var childIndex = IndexOfChild(context.Identity, list, label, index);
            if (childIndex < 0)
                return "the child row is no longer present; refresh and retry";

            list.RemoveAt(childIndex);
            return Validation.ValidateCandidate(tab.InfoType, tab.Columns, candidate, tab.ResolveChildIdentity);
        }
    }

    private sealed class RowDiff
    {
        public object Row = null!;
        public string Key = string.Empty;
        public bool Added;
        public readonly HashSet<string> ChangedProperties = new(StringComparer.Ordinal);
        public readonly Dictionary<string, ChildListDiff> ChildDiffs = new(StringComparer.Ordinal);
        public int ChangedCells;
    }

    private sealed class DiffResult
    {
        public bool CanonKnown;
        public int ChangedCells;
        public readonly List<int> ChangedRowIndices = new();
        public readonly Dictionary<int, RowDiff> ByIndex = new();
        public readonly Dictionary<string, RowDiff> DeletedByKey = new(StringComparer.Ordinal);
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
        /// last (ordinal key order), missing cells marked explicitly. One-level child lists emit
        /// their own rows under <c>&lt;key&gt;.&lt;property&gt;[&lt;identity&gt;].&lt;field&gt;</c>
        /// (positional mode uses <c>[index]</c>), plus explicit identity/order/structure diagnostics.
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
            var dataColumns = columns.Where(static column => !column.IsKey && column.Kind != EditorKind.ChildList).ToList();
            var childColumns = columns.Where(static column => column.Kind == EditorKind.ChildList).ToList();

            foreach (var rowIndex in diff.ChangedRowIndices)
            {
                var row = current[rowIndex];
                var key = keyProperty != null ? Keying.FormatKey(keyProperty, row) ?? string.Empty : string.Empty;
                var rowDiff = diff.ByIndex[rowIndex];
                object? rowCanon = null;
                var hasRowCanon = canonByKey.TryGetValue(key, out rowCanon);
                foreach (var column in dataColumns)
                {
                    if (!rowDiff.ChangedProperties.Contains(column.Name))
                        continue;

                    var currentText = FormatCellValue(column.Property.GetValue(row));
                    var canonText = rowDiff.Added || !hasRowCanon
                        ? missing
                        : FormatCellValue(column.Property.GetValue(rowCanon!));

                    AppendChange(builder, key, column.Name, canonText, currentText);
                }

                AppendChildChanges(builder, key, childColumns, rowDiff, row, rowCanon, missing);
            }

            foreach (var deletedKey in diff.DeletedKeys)
            {
                if (!canonByKey.TryGetValue(deletedKey, out var canonRow))
                    continue;

                var rowDiff = diff.DeletedByKey.TryGetValue(deletedKey, out var deleted) ? deleted : null;
                foreach (var column in dataColumns)
                {
                    var canonText = FormatCellValue(column.Property.GetValue(canonRow));
                    AppendChange(builder, deletedKey, column.Name, canonText, missing);
                }

                if (rowDiff != null)
                    AppendChildChanges(builder, deletedKey, childColumns, rowDiff, null, canonRow, missing);
            }

            return builder.ToString();
        }

        private static void AppendChildChanges(
            StringBuilder builder,
            string key,
            IReadOnlyList<ColumnInfo> childColumns,
            RowDiff rowDiff,
            object? currentRow,
            object? canonRow,
            string missing)
        {
            foreach (var column in childColumns)
            {
                if (!rowDiff.ChildDiffs.TryGetValue(column.Name, out var childDiff))
                    continue;

                var identity = column.ChildElementType ?? typeof(object);
                var currentList = Children.ListOf(column.Property, currentRow);
                var canonList = Children.ListOf(column.Property, canonRow);

                if (childDiff.IdentityError)
                {
                    AppendChange(builder, key, $"{column.Name}[identity]",
                        childDiff.IdentityProperty, childDiff.IdentityMessage ?? "invalid child identity");
                    continue;
                }

                if (childDiff.Structural)
                    AppendChange(builder, key, $"{column.Name}[structure]", "missing/null",
                        currentRow == null ? "missing" : "present");

                if (childDiff.OrderOnly)
                    AppendChange(builder, key, $"{column.Name}[order]", "canon-order", "reordered");

                foreach (var childRowDiff in childDiff.Rows)
                {
                    var path = $"{column.Name}[{childRowDiff.Identity}]";
                    foreach (var changedColumn in childRowDiff.ChangedColumns)
                    {
                        var canonText = childRowDiff.Added
                            ? missing
                            : FormatChildCell(identity, changedColumn, canonList, childRowDiff.Index);
                        var currentText = FormatChildCell(identity, changedColumn, currentList, childRowDiff.Index);
                        AppendChange(builder, key, $"{path}.{changedColumn}", canonText, currentText);
                    }
                }

                foreach (var childRowDiff in childDiff.Deleted)
                {
                    var path = $"{column.Name}[{childRowDiff.Identity}]";
                    foreach (var changedColumn in childRowDiff.ChangedColumns)
                    {
                        var canonText = FormatChildCell(identity, changedColumn, canonList, childRowDiff.Index);
                        AppendChange(builder, key, $"{path}.{changedColumn}", canonText, missing);
                    }
                }
            }
        }

        private static string FormatChildCell(
            Type elementType, string childColumnName, IList? list, int index)
        {
            const string missing = "<missing>";
            if (list == null || index < 0 || index >= list.Count)
                return missing;

            var property = elementType.GetProperty(childColumnName, BindingFlags.Public | BindingFlags.Instance);
            if (property == null)
                return missing;

            var row = list[index];
            if (row == null)
                return missing;

            try
            {
                return FormatCellValue(property.GetValue(row));
            }
            catch (Exception)
            {
                return missing;
            }
        }

        private static void AppendChange(StringBuilder builder, string key, string property, string canon, string current)
        {
            builder.Append(Escape(key)).Append('\t')
                .Append(Escape(property)).Append('\t')
                .Append(Escape(canon)).Append('\t')
                .Append(Escape(current)).Append('\n');
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
            /// <summary>Refused before any work: the editing lock is engaged after a failed recovery.</summary>
            Locked,
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

            /// <summary>True when the commit was refused by the editing lock (nothing was written).</summary>
            public bool Locked;

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

    /// <summary>
    /// Editing-lock decision and the guarded commit entry point. Every parent and child handler
    /// funnels through <see cref="CommitGuarded"/>, so a locked editor (after a failed recovery) can
    /// never serialize, write or reload, whatever the call site — including a confirmation dialog
    /// that was already open when the lock engaged.
    /// </summary>
    private static class Editing
    {
        public const string DisabledMessage =
            "Editing is disabled after a failed recovery; Reload or Revert first.";

        public static bool IsLocked(bool editingDisabled) => editingDisabled;

        /// <summary>A refused, write-free result for a commit attempted while editing is locked.</summary>
        public static Transaction.Result Refused() => new()
        {
            Success = false,
            Locked = true,
            FailedStage = Transaction.Stage.Locked,
            Error = DisabledMessage
        };

        /// <summary>
        /// True when the child pane's editors / Add / per-row Delete are enabled. An invalid declared
        /// identity does NOT disable editing: repair happens in-window under the warning, and the
        /// reference guard plus validation refuse any write until the whole sibling list is valid.
        /// </summary>
        public static bool ChildEditingAllowed(bool bindingPresent, bool editingDisabled)
            => bindingPresent && !editingDisabled;
    }

    /// <summary>
    /// The single guarded commit entry point: refuses without touching the writer when editing is
    /// locked, otherwise runs the shared clone→mutate→validate→write→Reload transaction.
    /// </summary>
    private static Transaction.Result CommitGuarded(
        bool editingDisabled,
        InfoDebugTableBinding binding,
        Type infoType,
        List<object> currentRows,
        Func<IList, IList, string?> mutate,
        Func<bool> reload)
    {
        if (Editing.IsLocked(editingDisabled))
            return Editing.Refused();

        return Transaction.Commit(
            binding.serializer,
            infoType,
            currentRows,
            candidate => mutate(candidate, currentRows),
            binding.writeOverride,
            reload);
    }

    /// <summary>
    /// One open child-delete confirmation. The pending child identity/index and the panel visibility
    /// are a single managed unit, so a lock, a tab change or a context close resets both together and
    /// the reset is directly testable without a native <c>Control</c>.
    /// </summary>
    private sealed class ChildDeleteConfirmation
    {
        public string? PendingIdentity;
        public int PendingIndex = -1;
        public bool Open;

        public void OpenFor(string identity, int index)
        {
            PendingIdentity = identity;
            PendingIndex = index;
            Open = true;
        }

        public void Reset()
        {
            PendingIdentity = null;
            PendingIndex = -1;
            Open = false;
        }
    }

    /// <summary>
    /// Resets an open child-delete confirmation whenever editing is locked (or unconditionally when
    /// <paramref name="force"/> is set). Returns true when a reset happened.
    /// </summary>
    private static bool ResetChildConfirmation(ChildDeleteConfirmation confirmation, bool editingDisabled, bool force = false)
    {
        if (!editingDisabled && !force)
            return false;
        if (!confirmation.Open && confirmation.PendingIdentity == null)
            return false;

        confirmation.Reset();
        return true;
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
        private readonly VBoxContainer _childPane;
        private readonly Button _childBackButton;
        private readonly Label _childBreadcrumb;
        private readonly Label _childIdentityNote;
        private readonly Button _childAddButton;
        private readonly LineEdit _childAddInput;
        private readonly Label _childAddError;
        private readonly GridContainer _childGrid;
        private readonly VBoxContainer _childConfirmPanel;
        private Label _childConfirmText = null!;

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
        private ChildContext? _childContext;
        private readonly ChildDeleteConfirmation _childDelete = new();

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

            _childPane = new VBoxContainer
            {
                Visible = false,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            _childPane.AddThemeConstantOverride("separation", 6);

            var crumbRow = new HBoxContainer();
            crumbRow.AddThemeConstantOverride("separation", (int)ActionSeparation);
            _childBackButton = new Button { Text = "‹ parent", FocusMode = Control.FocusModeEnum.None };
            _childBackButton.AddThemeFontSizeOverride("font_size", ViewerFontSize);
            _childBackButton.Pressed += CloseChildDetail;
            crumbRow.AddChild(_childBackButton);

            _childBreadcrumb = new Label
            {
                ClipText = true,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            _childBreadcrumb.AddThemeFontSizeOverride("font_size", ViewerFontSize);
            crumbRow.AddChild(_childBreadcrumb);
            _childPane.AddChild(crumbRow);

            _childIdentityNote = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
            _childIdentityNote.AddThemeFontSizeOverride("font_size", ViewerFontSize);
            _childPane.AddChild(_childIdentityNote);

            var childAddRow = new HBoxContainer();
            childAddRow.AddThemeConstantOverride("separation", (int)ActionSeparation);
            _childAddInput = new LineEdit { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, PlaceholderText = "identity" };
            _childAddInput.AddThemeFontSizeOverride("font_size", ViewerFontSize);
            _childAddInput.TextSubmitted += _ => CommitChildAdd();
            childAddRow.AddChild(_childAddInput);
            _childAddButton = NewActionButton("Add child", CommitChildAdd);
            childAddRow.AddChild(_childAddButton);
            _childPane.AddChild(childAddRow);

            _childAddError = new Label { Visible = false, AutowrapMode = TextServer.AutowrapMode.WordSmart };
            _childAddError.AddThemeFontSizeOverride("font_size", ViewerFontSize);
            _childPane.AddChild(_childAddError);

            _childConfirmPanel = new VBoxContainer { Visible = false };
            _childConfirmPanel.AddThemeConstantOverride("separation", 6);
            _childConfirmText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
            _childConfirmText.AddThemeFontSizeOverride("font_size", ViewerFontSize);
            _childConfirmPanel.AddChild(_childConfirmText);
            var childConfirmRow = new HBoxContainer();
            childConfirmRow.AddThemeConstantOverride("separation", (int)ActionSeparation);
            childConfirmRow.AddChild(NewActionButton("Delete child", CommitChildDelete));
            childConfirmRow.AddChild(NewActionButton("Cancel", CloseChildConfirm));
            _childConfirmPanel.AddChild(childConfirmRow);
            _childPane.AddChild(_childConfirmPanel);

            var childScroll = new ScrollContainer
            {
                ClipContents = true,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
                VerticalScrollMode = ScrollContainer.ScrollMode.Auto
            };
            _childGrid = new GridContainer { Columns = 1 };
            childScroll.AddChild(_childGrid);
            _childPane.AddChild(childScroll);

            _detailPane.AddChild(_childPane);

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
            _narrowDetailOpen = _childContext != null;
            _detailText.Text = string.Empty;
            RevalidateChildContext();

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
            tab.ResolveChildIdentity = tab.Binding?.resolveChildIdentity;

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

            tab.Diff = Diffing.ComputeSectioned(
                tab.InfoType, tab.Columns, tab.Rows, tab.CanonByKey, tab.CanonKnown, tab.ResolveChildIdentity);
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
            _childContext = null;
            _childDelete.Reset();
            _childPane.Visible = false;
            _childConfirmPanel.Visible = false;
            _detailText.Visible = true;
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

                // Parent every cell: a built-but-unparented editor is invisible and unreachable.
                tab.Grid.AddChild(control);
                controls.Add(control);
            }

            tab.RowControls.Add(controls);
        }

        private Control BuildCell(TabState tab, VisibleRow visible, ColumnInfo column, bool editable)
        {
            if (column.Kind == EditorKind.ChildList)
                return BuildChildCell(tab, visible, column, editable);

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
                editor = BuildEditor(column, value, ParentEditorHost(tab, visible.Key));
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

        /// <summary>
        /// Commit target for one editor. The parent grid and the child mini-table both reuse
        /// <see cref="BuildEditor"/> through this adapter: it parses/validates with the shared
        /// <see cref="Codec"/> and delegates only the final detached commit, so no conversion or
        /// signal wiring is duplicated.
        /// </summary>
        private sealed class EditorHost
        {
            public Action<ColumnInfo, object?> CommitValue = null!;
            public Action<LineEdit, string?> ShowError = null!;
        }

        private EditorHost ParentEditorHost(TabState tab, string rowKey) => new()
        {
            CommitValue = (column, value) => CommitCell(tab, column, rowKey, value),
            ShowError = ShowCellError
        };

        private Control BuildEditor(ColumnInfo column, object? value, EditorHost host)
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
                        host.CommitValue(column, pressed);
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
                        host.CommitValue(column, Enum.Parse(column.ValueType, name));
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
                    Action submit = () =>
                    {
                        if (!Codec.TryParseVector2I(xInput.Text, yInput.Text, out var parsed, out var error))
                        {
                            host.ShowError(xInput, error);
                            return;
                        }

                        host.CommitValue(column, parsed);
                    };
                    xInput.TextSubmitted += _ => submit();
                    yInput.TextSubmitted += _ => submit();
                    row.AddChild(xInput);
                    row.AddChild(yInput);
                    return row;
                }
                case EditorKind.Number:
                case EditorKind.Text:
                {
                    var lineEdit = NewCellLineEdit(ScalarText(value));
                    lineEdit.TextSubmitted += text =>
                    {
                        if (!Codec.TryParse(column.ValueType, text, column.Field, out var parsed, out var error))
                        {
                            host.ShowError(lineEdit, error);
                            return;
                        }

                        host.CommitValue(column, parsed);
                    };
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

        private void CommitCell(TabState tab, ColumnInfo column, string rowKey, object? value)
        {
            if (tab.Binding == null)
                return;

            if (_editingDisabled)
            {
                FlashStatus(Editing.DisabledMessage);
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
                ShowAddError(Editing.DisabledMessage);
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
            => Validation.ValidateCandidate(tab.InfoType, tab.Columns, candidate, tab.ResolveChildIdentity);

        private Transaction.Result? RunCommit(TabState tab, Func<IList, string?> mutate)
            => RunCommitCore(tab, (candidate, _) => mutate(candidate));

        private Transaction.Result? RunCommitCurrent(TabState tab, Func<IList, IList, string?> mutate)
            => RunCommitCore(tab, mutate);

        private Transaction.Result? RunCommitCore(TabState tab, Func<IList, IList, string?> mutate)
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
                // Single guarded entry point: this refuses without touching the writer whenever the
                // editing lock is engaged, so no parent or child handler can slip a write through.
                result = CommitGuarded(
                    _editingDisabled,
                    tab.Binding,
                    tab.InfoType,
                    currentRows,
                    mutate,
                    tab.Manager.Reload);
            }
            finally
            {
                _selfCommit = false;
            }

            if (result.Locked)
            {
                CloseConfirmPanel();
                CloseChildConfirm();
                FlashStatus(result.Error ?? Editing.DisabledMessage);
                return result;
            }

            if (result.RecoveryFailed)
            {
                // The lock engages here: any open confirmation (parent or child) must be reset so it
                // cannot write after the recovery failure.
                _editingDisabled = true;
                CloseConfirmPanel();
                CloseChildConfirm();
            }

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
            CloseChildDetail();
            _selectedKey = visible.Key;
            _narrowDetailOpen = true;
            _detailText.Text = SerializeDetail(tab, visible);
            HighlightSelectedRow();
            ApplyResponsive(CurrentViewportSize());
        }

        private void OnBack()
        {
            if (_childContext != null)
            {
                CloseChildDetail();
                return;
            }

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

        // ---- child drill-down -----------------------------------------------

        /// <summary>
        /// The parent grid cell for a one-level child list: a drill-down button when the tab has an
        /// editing capability, a read-only <c>List[N]</c> label otherwise. Never a JSON object.
        /// </summary>
        private Control BuildChildCell(TabState tab, VisibleRow visible, ColumnInfo column, bool editable)
        {
            var wrapper = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            wrapper.AddThemeConstantOverride("separation", 0);

            var list = Children.ListOf(column.Property, visible.Row);
            var count = list?.Count ?? 0;
            var changed = tab.Diff != null && tab.Diff.ByIndex.TryGetValue(visible.Index, out var diff)
                && diff.ChildDiffs.ContainsKey(column.Name);

            if (editable && !visible.Deleted && !_editingDisabled)
            {
                var button = new Button
                {
                    Text = list == null ? $"{column.Name} (null) ▸" : $"{column.Name}[{count}] ▸",
                    Flat = true,
                    FocusMode = Control.FocusModeEnum.None,
                    CustomMinimumSize = new Vector2(CellWidth, 0),
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
                };
                button.AddThemeFontSizeOverride("font_size", ViewerFontSize);
                button.Pressed += () => OpenChildDetail(tab, visible, column);
                wrapper.AddChild(button);
            }
            else
            {
                wrapper.AddChild(BuildPlainLabel(list == null ? "null" : $"{column.Name}[{count}]"));
            }

            if (changed && tab.CanonKnown)
            {
                var comparison = new Label
                {
                    Text = "≠ child rows",
                    ClipText = true,
                    TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                    MouseFilter = Control.MouseFilterEnum.Ignore
                };
                comparison.AddThemeFontSizeOverride("font_size", ViewerFontSize);
                comparison.Modulate = new Color(1f, 0.65f, 0.35f, 1f);
                wrapper.AddChild(comparison);
            }

            return wrapper;
        }

        private void OpenChildDetail(TabState tab, VisibleRow visible, ColumnInfo column)
        {
            if (tab.Binding == null || column.ChildElementType == null)
                return;

            _selectedKey = visible.Key;
            _narrowDetailOpen = true;
            _childDelete.Reset();
            _childContext = new ChildContext
            {
                Tab = tab,
                ParentKey = visible.Key,
                ParentReference = visible.Row,
                Column = column,
                ElementType = column.ChildElementType,
                ChildColumns = BuildColumns(column.ChildElementType, allowChildLists: false),
                Identity = Children.Resolve(column.ChildElementType, tab.ResolveChildIdentity)
            };

            BuildChildUi();
            RebuildTabUiPreservingTab();
            HighlightSelectedRow();
            UpdateStatus();
            ApplyResponsive(CurrentViewportSize());
        }

        /// <summary>
        /// Rebuilds the parent tab grids while suppressing and restoring the tab selection: adding
        /// new tab contents resets <c>CurrentTab</c> and would otherwise fire <see cref="OnTabChanged"/>,
        /// which clears the open child context.
        /// </summary>
        private void RebuildTabUiPreservingTab()
        {
            var index = _selectedTab;
            _suppressTabChanged = true;
            try
            {
                BuildTabUi();
                if (index >= 0 && index < _tabContainer.GetTabCount())
                    _tabContainer.CurrentTab = index;
            }
            finally
            {
                _suppressTabChanged = false;
            }
        }

        private void CloseChildDetail()
        {
            _childContext = null;
            _childDelete.Reset();
            _narrowDetailOpen = false;
            if (_childConfirmPanel != null)
                _childConfirmPanel.Visible = false;
            if (_childPane != null)
                _childPane.Visible = false;
            if (_detailText != null)
            {
                _detailText.Visible = true;
                _detailText.Text = string.Empty;
            }

            ReserializeParentDetail();
        }

        /// <summary>
        /// Restores the parent JSON in the detail pane after leaving a child table, using the
        /// currently published row (never a stale snapshot). No-op when there is no selection.
        /// </summary>
        private void ReserializeParentDetail()
        {
            var tab = SelectedTabState();
            if (tab == null || _selectedKey == null)
                return;

            var visible = tab.Visible.Find(v => string.Equals(v.Key, _selectedKey, StringComparison.Ordinal));
            var row = visible?.Row ?? (tab.CanonByKey.TryGetValue(_selectedKey, out var canon) ? canon : null);
            if (row == null)
                return;

            _detailText.Text = SerializeDetail(tab, visible ?? new VisibleRow(-1, row, _selectedKey));
        }

        private void BuildChildUi()
        {
            ClearChildren(_childGrid);
            var context = _childContext!;
            var list = Children.ListOf(context.Column.Property, context.ParentReference);
            var identity = context.Identity;
            var childColumns = context.ChildColumns;

            _childBreadcrumb.Text = $"{context.Tab.InfoType.Name} · {context.ParentKey} · {context.Column.Name}";
            var identityText = identity.Positional
                ? $"identity: explicit positional ({identity.Label}); rows match by index"
                : identity.Source == ChildIdentitySource.InfoKey
                    ? $"identity: {identity.Property!.Name} ([InfoKey])"
                    : $"identity: {identity.Property!.Name} (composition)";

            var identityResult = Children.ValidateList(identity, list, context.Column.Name);
            if (!identityResult.Ok)
                identityText += $"\n⚠ identity error: {identityResult.Error}\n" +
                    "Repair by editing the identity cell (rows are matched positionally while the list is invalid); " +
                    "no write is accepted until the whole list becomes valid.";

            _childIdentityNote.Text = identityText;
            _childAddInput.Visible = !identity.Positional;

            // An invalid identity does NOT disable editing: the repair happens in-window here. The
            // reference guard plus ValidateCandidate already refuse any write until the list is valid.
            var editing = Editing.ChildEditingAllowed(context.Tab.Binding != null, _editingDisabled);
            ResetChildConfirmation(_childDelete, _editingDisabled);
            _childConfirmPanel.Visible = _childDelete.Open;
            _childAddButton.Disabled = !editing;
            _childAddInput.Editable = editing;

            _childGrid.Columns = childColumns.Count + 1;
            AddHeaderCell(_childGrid, "identity");
            foreach (var column in childColumns)
                AddHeaderCell(_childGrid, column.Name);

            if (list == null)
            {
                _childGrid.AddChild(BuildPlainLabel("(null child list)"));
                return;
            }

            object? canonParent = null;
            if (context.Tab.CanonKnown)
                context.Tab.CanonByKey.TryGetValue(context.ParentKey, out canonParent);
            var canonList = canonParent != null ? Children.ListOf(context.Column.Property, canonParent) : null;

            var diff = context.Tab.Diff != null
                && context.Tab.Diff.ByIndex.TryGetValue(IndexOfRow(context.Tab, context.ParentReference), out var parentDiff)
                ? parentDiff
                : null;
            ChildListDiff? childDiff = null;
            if (diff != null && diff.ChildDiffs.TryGetValue(context.Column.Name, out var found))
                childDiff = found;

            var rowCount = 0;
            for (var i = 0; i < list.Count; i++)
            {
                var child = list[i];
                var label = Children.Label(identity, child, i);
                var rowDiff = childDiff?.Rows.Find(r => r.Index == i && !r.Added) ?? null;
                if (_changesOnlyMode && childDiff != null && rowDiff == null)
                    continue;

                if (rowCount >= RowCap)
                    break;
                rowCount++;
                BuildChildRow(context, childColumns, identity, child, label, i, editing, rowDiff);
            }

            if (!_changesOnlyMode && list.Count > RowCap)
                _childIdentityNote.Text +=
                    $"\nShowing {RowCap} of {list.Count} child rows (truncated); all rows still participate in diff/export/transactions.";

            // Deleted canon children are inspectable tombstones, never editable.
            if (_changesOnlyMode && childDiff != null)
            {
                foreach (var deleted in childDiff.Deleted)
                {
                    BuildChildRow(context, childColumns, identity,
                        Lookup(canonList, deleted.Index), deleted.Identity, deleted.Index, editing: false, rowDiff: deleted);
                }
            }
        }

        private void BuildChildRow(
            ChildContext context,
            IReadOnlyList<ColumnInfo> childColumns,
            ChildIdentity identity,
            object? child,
            string label,
            int index,
            bool editing,
            ChildRowDiff? rowDiff)
        {
            var deleted = rowDiff?.Deleted == true;
            var cellRow = new HBoxContainer();
            cellRow.AddThemeConstantOverride("separation", 4);

            var identityLabel = new Label
            {
                Text = label,
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                CustomMinimumSize = new Vector2(CellWidth, 0),
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            identityLabel.AddThemeFontSizeOverride("font_size", ViewerFontSize);
            cellRow.AddChild(identityLabel);

            var delete = new Button
            {
                Text = "✕",
                Flat = true,
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(48f, 0),
                Disabled = !editing || deleted
            };
            delete.AddThemeFontSizeOverride("font_size", ViewerFontSize);
            delete.Pressed += () => OpenChildDeleteConfirm(context, label, index);
            cellRow.AddChild(delete);

            _childGrid.AddChild(cellRow);

            if (child == null)
            {
                foreach (var _ in childColumns)
                    _childGrid.AddChild(BuildPlainLabel("<missing>"));
                return;
            }

            foreach (var column in childColumns)
            {
                object? value;
                try
                {
                    value = column.Property.GetValue(child);
                }
                catch (Exception ex)
                {
                    value = $"<error: {ex.GetType().Name}>";
                }

                var changed = rowDiff != null && rowDiff.ChangedColumns.Contains(column.Name);
                var wrapper = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                wrapper.AddThemeConstantOverride("separation", 0);

                Control editor;
                if (editing && !deleted && column.CanEdit)
                    editor = BuildEditor(column, value, ChildEditorHost(context, label, index));
                else
                    editor = BuildPlainLabel(value);

                wrapper.AddChild(editor);
                if (changed)
                    editor.Modulate = new Color(1f, 0.75f, 0.45f, 1f);
                _childGrid.AddChild(wrapper);
            }
        }

        private static object? Lookup(IList? list, int index)
            => list != null && index >= 0 && index < list.Count ? list[index] : null;

        private static int IndexOfRow(TabState tab, object row)
        {
            for (var i = 0; i < tab.Rows.Count; i++)
            {
                if (ReferenceEquals(tab.Rows[i], row))
                    return i;
            }

            return -1;
        }

        private EditorHost ChildEditorHost(ChildContext context, string identity, int index) => new()
        {
            CommitValue = (column, value) => CommitChildCell(context, identity, index, column, value),
            ShowError = ShowCellError
        };

        /// <summary>Re-resolves the displayed published parent reference and requires it to match.</summary>
        private void CommitChildCell(ChildContext context, string label, int index, ColumnInfo column, object? value)
        {
            var tab = context.Tab;
            if (tab.Binding == null)
                return;

            if (_editingDisabled)
            {
                FlashStatus(Editing.DisabledMessage);
                return;
            }

            string? oldText = null;
            var result = RunCommitCurrent(tab, (candidate, current) =>
            {
                var error = ChildEdit.SetCell(
                    tab, context, label, index, column, value, candidate, current, out oldText);
                return error;
            });

            if (result?.Success == true && oldText != null)
                Log.LogInfo(
                    $"{tab.InfoType.Name}.{context.ParentKey}.{context.Column.Name}[{label}].{column.Name}: " +
                    $"{oldText} → {FormatCellValue(value)}");
        }

        private void CommitChildAdd()
        {
            var context = _childContext;
            if (context == null)
                return;

            var tab = context.Tab;
            if (tab.Binding == null)
            {
                ShowChildAddError("Editing is unavailable for this tab.");
                return;
            }

            if (_editingDisabled)
            {
                ShowChildAddError(Editing.DisabledMessage);
                return;
            }

            var identity = context.Identity;
            object? identityValue = null;
            string? identityText = null;
            if (!identity.Positional)
            {
                if (!Codec.TryParse(identity.Property!.PropertyType, _childAddInput.Text, null, out identityValue, out var error))
                {
                    ShowChildAddError(error ?? "invalid identity");
                    return;
                }

                identityText = Keying.FormatKeyValue(identityValue);
            }

            var instance = Keying.DefaultInstance(context.ElementType);
            if (instance == null)
            {
                ShowChildAddError("A new child row could not be created (no public parameterless constructor).");
                return;
            }

            var result = RunCommitCurrent(tab, (candidate, current) =>
                ChildEdit.Add(tab, context, identityValue, instance, candidate, current));

            if (result?.Success == true)
            {
                Log.LogInfo(
                    $"{tab.InfoType.Name}.{context.ParentKey}.{context.Column.Name}: " +
                    $"added child {identityText ?? "(positional)"}");
                _childAddInput.Text = string.Empty;
            }
        }

        private void OpenChildDeleteConfirm(ChildContext context, string label, int index)
        {
            if (_editingDisabled)
            {
                FlashStatus(Editing.DisabledMessage);
                return;
            }

            _childDelete.OpenFor(label, index);
            _childConfirmPanel.Visible = true;
            _childAddError.Visible = false;

            var lines = new List<string> { $"Delete child '{label}' from {context.Column.Name}?" };

            // A natural string identity is the only case where a ref scan is meaningful.
            var identityProperty = context.Identity.Property;
            if (identityProperty != null && identityProperty.PropertyType == typeof(string))
            {
                var scan = RefScanFor(context.Tab, label);
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
            }
            else
            {
                lines.Add("No reference scan for a non-string/positional child identity; candidates may exist outside the scanned scalar refs.");
            }

            _childConfirmText.Text = string.Join("\n", lines);
        }

        private void CommitChildDelete()
        {
            var context = _childContext;
            if (context == null || context.Tab.Binding == null || _childDelete.PendingIdentity == null)
                return;

            // Explicit early return in addition to the central lock in the commit path: an open
            // confirmation must never write after a recovery failure engaged the lock.
            if (_editingDisabled)
            {
                CloseChildConfirm();
                FlashStatus(Editing.DisabledMessage);
                return;
            }

            var label = _childDelete.PendingIdentity;
            var index = _childDelete.PendingIndex;
            var result = RunCommitCurrent(context.Tab, (candidate, current) =>
                ChildEdit.Remove(context.Tab, context, label, index, candidate, current));

            if (result?.Success == true)
            {
                Log.LogInfo($"{context.Tab.InfoType.Name}.{context.ParentKey}.{context.Column.Name}: removed child {label}");
                _childDelete.Reset();
                _childConfirmPanel.Visible = false;
            }
        }

        private void CloseChildConfirm()
        {
            _childDelete.Reset();
            _childConfirmPanel.Visible = false;
        }

        private void ShowChildAddError(string message)
        {
            _childAddError.Text = message;
            _childAddError.Visible = true;
        }

        /// <summary>
        /// Revalidates the open child context after a rebuild: keeps it when the parent row and the
        /// child property still exist (re-anchoring to the freshly published parent), closes it
        /// cleanly otherwise. The commit guard uses the re-anchored reference, never a stale one.
        /// </summary>
        private void RevalidateChildContext()
        {
            if (_childContext == null)
                return;

            var tab = SelectedTabState();
            if (tab == null || !ReferenceEquals(tab, _childContext.Tab))
            {
                CloseChildDetail();
                return;
            }

            var index = Keying.IndexOfKey(tab.InfoType, tab.Rows, _childContext.ParentKey);
            if (index < 0)
            {
                CloseChildDetail();
                return;
            }

            var column = tab.Columns.Find(c => c.Kind == EditorKind.ChildList && c.Name == _childContext.Column.Name);
            if (column == null)
            {
                CloseChildDetail();
                return;
            }

            _childContext.Column = column;
            _childContext.ParentReference = tab.Rows[index];
            _childContext.ChildColumns = BuildColumns(_childContext.ElementType, allowChildLists: false);
            _childContext.Identity = Children.Resolve(_childContext.ElementType, tab.ResolveChildIdentity);
            BuildChildUi();
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
            _childContext = null;
            _childDelete.Reset();
            _childConfirmPanel.Visible = false;
            _childPane.Visible = false;
            _detailText.Visible = true;
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

            RebuildTabUiPreservingTab();
            if (_childContext != null)
                BuildChildUi();
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
            // An open child context is a selection in its own right: it must not depend on whether
            // its parent row is still inside a changes-only visible window.
            var hasSelection = _childContext != null || (selected != null && _selectedKey != null
                && selected.Visible.Exists(v => string.Equals(v.Key, _selectedKey, StringComparison.Ordinal)));
            // Aspect decides the axis: a portrait viewport (height > width, equality is landscape)
            // stacks; flip the single body container in place instead of reparenting/rebuilding panes.
            var portrait = size.Y > size.X;
            var wide = size.X >= WideBreakpointWidth;
            _body.Vertical = portrait;

            _childPane.Visible = _childContext != null;
            if (_childContext != null)
                _detailText.Visible = false;

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

    /// <summary>One open child drill-down: the parent row reference and the child list it shows.</summary>
    private sealed class ChildContext
    {
        public TabState Tab = null!;
        public string ParentKey = string.Empty;

        /// <summary>The displayed published parent row; the commit guard re-resolves and matches it.</summary>
        public object ParentReference = null!;

        public ColumnInfo Column = null!;
        public Type ElementType = typeof(object);
        public List<ColumnInfo> ChildColumns = new();
        public ChildIdentity Identity = null!;
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

        /// <summary>The binding's optional child natural-identity resolver, or null.</summary>
        public Func<Type, string?>? ResolveChildIdentity { get; set; }

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
