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
/// supplied by the composition root that created the table's loader. All four arguments are
/// mandatory: a missing capability is not inferred and never guessed. The callback identities are
/// retained unchanged — the window compares rows against whatever <c>readCanonical</c> returns on
/// every refresh, and never assumes a cached snapshot.
/// </summary>
public sealed class InfoDebugTableBinding
{
    internal readonly ISerializer serializer;
    internal readonly Func<byte[]> readCanonical;
    internal readonly Action<string> writeOverride;
    internal readonly Func<IReadOnlyList<object>, IReadOnlyList<IReadOnlyList<string>>> projectFlatSheet;

    /// <summary>
    /// Creates a binding from the table's four editing capabilities. Each argument must be
    /// non-null; nulls are rejected rather than silently degrading to a read-only binding.
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
    /// including its header row and any repeated header occurrences. May throw to explicitly
    /// refuse a sectioned/unsupported layout — the window never falls back to lossy output.
    /// </param>
    public InfoDebugTableBinding(
        ISerializer serializer,
        Func<byte[]> readCanonical,
        Action<string> writeOverride,
        Func<IReadOnlyList<object>, IReadOnlyList<IReadOnlyList<string>>> projectFlatSheet)
    {
        this.serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        this.readCanonical = readCanonical ?? throw new ArgumentNullException(nameof(readCanonical));
        this.writeOverride = writeOverride ?? throw new ArgumentNullException(nameof(writeOverride));
        this.projectFlatSheet = projectFlatSheet ?? throw new ArgumentNullException(nameof(projectFlatSheet));
    }
}
