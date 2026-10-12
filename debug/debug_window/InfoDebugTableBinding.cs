using System;
using System.Collections.Generic;
using cfEngine.Serialize;

namespace cfGodotEngine.Debug;

/// <summary>
/// Small data-only capability descriptor for one Info table's file/clipboard editing support.
/// It carries the exact serializer the table's loader reads with, a callback that returns the
/// table's current canonical bytes, a callback that atomically writes a candidate document to
/// the table's dedicated override destination, and a callback that projects full runtime rows
/// into the table's local-order sheet matrix (including its header row).
///
/// The descriptor owns no resources and performs no work in its constructor; every capability is
/// supplied by the composition root that created the table's loader. The four original arguments
/// are mandatory: a missing capability is not inferred and never guessed. The callback identities
/// are retained unchanged — the window compares rows against whatever <c>readCanonical</c> returns
/// on every refresh, and never assumes a cached snapshot.
///
/// The projected matrix may be flat or <em>sectioned</em>: a scalar key/value block, a blank
/// separator row, a child header row and the child rows. The window treats it as opaque raw cells
/// (it owns TSV escaping only), so a sectioned table exports without a second writer or format
/// enum. An optional fifth capability names a child element type's natural identity property; when
/// it is absent the four-argument constructor is used and the window falls back to explicit
/// positional child identity. Identity is never guessed from currently-unique values.
/// </summary>
public sealed class InfoDebugTableBinding
{
    internal readonly ISerializer serializer;
    internal readonly Func<byte[]> readCanonical;
    internal readonly Action<string> writeOverride;
    internal readonly Func<IReadOnlyList<object>, IReadOnlyList<IReadOnlyList<string>>> projectFlatSheet;
    internal readonly Func<Type, string> resolveChildIdentity;

    /// <summary>
    /// Creates a binding from the table's four editing capabilities, with no child natural-identity
    /// capability. The window then uses explicit positional child identity. Delegates to the
    /// five-argument overload. Each argument must be non-null; nulls are rejected rather than
    /// silently degrading to a read-only binding.
    /// </summary>
    /// <param name="serializer">
    /// The same <see cref="ISerializer"/> the table's loader deserializes with, so a row cloned
    /// through this window round-trips exactly the shape the manager reads.
    /// </param>
    /// <param name="readCanonical">
    /// Returns the table's current canonical document bytes. Invoked on every refresh/diff/export;
    /// callers must not assume the bytes are stable between calls.
    /// </param>
    /// <param name="writeOverride">
    /// Atomically replaces the table's override document with the serialized candidate text.
    /// Throw to report a failed write. The window never captures success unless this returns.
    /// </param>
    /// <param name="projectFlatSheet">
    /// Projects the full (uncapped) runtime row list into the table's local-order sheet matrix,
    /// including its header row and any repeated header occurrences. The matrix may be sectioned.
    /// May throw to explicitly refuse an unsupported layout — the window never falls back to lossy
    /// output.
    /// </param>
    public InfoDebugTableBinding(
        ISerializer serializer,
        Func<byte[]> readCanonical,
        Action<string> writeOverride,
        Func<IReadOnlyList<object>, IReadOnlyList<IReadOnlyList<string>>> projectFlatSheet)
        : this(serializer, readCanonical, writeOverride, projectFlatSheet, resolveChildIdentity: null)
    {
    }

    /// <summary>
    /// Creates a binding from the table's four editing capabilities plus an optional child
    /// natural-identity resolver. The first four arguments must be non-null; the resolver is
    /// optional capability, so it may be null (equivalently, use the four-argument constructor).
    /// </summary>
    /// <param name="serializer">
    /// The same <see cref="ISerializer"/> the table's loader deserializes with.
    /// </param>
    /// <param name="readCanonical">
    /// Returns the table's current canonical document bytes; re-read on every refresh/diff/export.
    /// </param>
    /// <param name="writeOverride">
    /// Atomically replaces the table's override document with the serialized candidate text.
    /// </param>
    /// <param name="projectFlatSheet">
    /// Projects the full (uncapped) runtime row list into the table's local-order sheet matrix,
    /// which may be sectioned.
    /// </param>
    /// <param name="resolveChildIdentity">
    /// Returns the public scalar property name that naturally identifies one child row of the given
    /// element type, or null when that type has no natural identity. Invoked only for a one-level
    /// <c>List&lt;TChild&gt;</c> column whose element type carries no single <c>[InfoKey]</c>.
    /// </param>
    public InfoDebugTableBinding(
        ISerializer serializer,
        Func<byte[]> readCanonical,
        Action<string> writeOverride,
        Func<IReadOnlyList<object>, IReadOnlyList<IReadOnlyList<string>>> projectFlatSheet,
        Func<Type, string> resolveChildIdentity)
    {
        this.serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        this.readCanonical = readCanonical ?? throw new ArgumentNullException(nameof(readCanonical));
        this.writeOverride = writeOverride ?? throw new ArgumentNullException(nameof(writeOverride));
        this.projectFlatSheet = projectFlatSheet ?? throw new ArgumentNullException(nameof(projectFlatSheet));
        this.resolveChildIdentity = resolveChildIdentity;
    }
}
