using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace cfGodotEngine.Debug;

/// <summary>
/// Engine-free tailer for the newest file in a log directory.
/// <para>
/// Polls the directory, selects the newest file by <see cref="File.GetLastWriteTimeUtc(string)"/>
/// (rotation-name-agnostic) and returns only the complete lines appended since the previous
/// <see cref="Poll"/> call. It uses <see cref="System.IO"/> only — never Godot's
/// <c>FileAccess</c>/<c>DirAccess</c> — so it unit-tests under NUnit without a Godot runtime.
/// </para>
/// <para>
/// Line splitting is done at the byte level and the trailing partial segment is buffered until a
/// newline arrives, so a multi-byte UTF-8 character spanning two polls is not corrupted.
/// <see cref="FileShare.ReadWrite"/> is required because the engine holds the log file open.
/// </para>
/// </summary>
public sealed class LogFileTailer
{
    private readonly List<byte> _pendingBytes = new();
    private string _currentFile;
    private long _offset;

    public LogFileTailer(string directoryPath)
    {
        DirectoryPath = directoryPath;
    }

    /// <summary>Directory scanned for the newest log file.</summary>
    public string DirectoryPath { get; }

    /// <summary>Absolute path of the last-selected file, or <c>null</c> before the first successful poll.</summary>
    public string CurrentFile => _currentFile;

    /// <summary>
    /// Returns the new complete lines appended to the newest file since the previous call.
    /// Never throws: a missing directory or any transient <see cref="IOException"/> /
    /// <see cref="UnauthorizedAccessException"/> (enumeration, metadata lookup or read) yields
    /// an empty result, and no buffered state is mutated by a failed poll.
    /// </summary>
    public IReadOnlyList<string> Poll()
    {
        // 1. Missing/blank directory → no-op.
        if (string.IsNullOrEmpty(DirectoryPath) || !Directory.Exists(DirectoryPath))
            return Array.Empty<string>();

        // Discovery, metadata lookup and reading share one I/O boundary: the directory can vanish
        // after the Exists check, and enumeration/stat access can fail, so all of it must be caught.
        try
        {
            // 2. Newest file by mtime (rotation-name-agnostic); ordinal tie-break for determinism.
            var newest = Directory.EnumerateFiles(DirectoryPath)
                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                .ThenByDescending(f => f, StringComparer.Ordinal)
                .FirstOrDefault();

            if (newest == null)
                return Array.Empty<string>();

            // 3. Normalize so CurrentFile is always absolute, even for a relative DirectoryPath.
            newest = Path.GetFullPath(newest);

            // 4. File changed or truncated → reset the read position and drop the partial buffer.
            if (newest != _currentFile || new FileInfo(newest).Length < _offset)
            {
                _currentFile = newest;
                _offset = 0;
                _pendingBytes.Clear();
            }

            // 5. Read new bytes into temporary storage (share read/write: the engine keeps the log
            //    open). Nothing is committed until the whole read succeeds, so a mid-read failure
            //    cannot leave already-buffered bytes that the next poll would read again.
            using var stream = new FileStream(_currentFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Seek(_offset, SeekOrigin.Begin);

            var readBytes = new List<byte>();
            var buffer = new byte[8192];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (var i = 0; i < read; i++)
                    readBytes.Add(buffer[i]);
            }

            // 6. Commit bytes and offset together, only after a fully successful read.
            var newOffset = stream.Position;
            _pendingBytes.AddRange(readBytes);
            _offset = newOffset;
        }
        catch (IOException)
        {
            // Engine holding the file / transient IO failure → no lines this poll, never throw.
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            // Directory/file permissions changed underneath us → no lines this poll, never throw.
            return Array.Empty<string>();
        }

        // 7. Byte-level split on '\n'; strip one trailing '\r'; keep the partial tail buffered.
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < _pendingBytes.Count; i++)
        {
            if (_pendingBytes[i] != (byte)'\n')
                continue;

            var length = i - start;
            if (length > 0 && _pendingBytes[i - 1] == (byte)'\r')
                length--;

            if (length > 0)
                lines.Add(Encoding.UTF8.GetString(CollectionsMarshal.AsSpan(_pendingBytes).Slice(start, length)));
            else
                lines.Add(string.Empty);

            start = i + 1;
        }

        if (start > 0)
            _pendingBytes.RemoveRange(0, start);

        return lines;
    }
}
