#if TOOLS

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using cfEngine;
using cfEngine.Command;
using Godot;

namespace cfGodotEngine.Debug;

/// <summary>
/// Editor-only NDJSON-over-TCP console bridge. One JSON object per line:
/// request <c>{"id","command"}</c> → response
/// <c>{"id","success","logs":[...],"events":[...]}</c>, executed through the shared
/// <see cref="CommandConsoleCore.ExecuteAndCapture"/> core on the main thread (no threads).
/// Advertises itself via a per-pid discovery file, and is hosted by both the
/// command_console editor plugin (<c>Context="editor"</c>) and the game autoload
/// (<c>Context="runtime"</c>). Whole-file <c>#if TOOLS</c>: export builds ship nothing.
/// </summary>
public partial class ConsoleBridgeServer : Node
{
    public const int BasePort = 9970;
    public const int PortScanCount = 16;
    public const string BindAddress = "127.0.0.1";
    public const string ProtocolId = "ndjson-console-v1";
    public const string DiscoverySubdir = "catsweeper-console/bridges"; // under Path.GetTempPath()

    /// <summary>"editor" | "runtime"; injected by the host.</summary>
    public string Context { get; set; } = "editor";

    private TcpServer _server;
    private StreamPeerTcp _peer;
    private readonly List<byte> _rx = new();
    private readonly Queue<byte[]> _txQueue = new();  // complete replies awaiting flush, in order
    private int _txOffset;                            // bytes of the queue head already written
    private int _port;
    private string _discoveryPath;

    public override void _Ready()
    {
        if (!TryBind())
        {
            GD.PushError(
                $"[ConsoleBridgeServer] no free port in {BasePort}..{BasePort + PortScanCount - 1}; bridge disabled");
            return;
        }

        WriteDiscovery();
        GD.Print($"[ConsoleBridgeServer] listening on {BindAddress}:{_port} (context={Context})");
    }

    public override void _Process(double delta)
    {
        if (_server == null)
            return;

        if (_server.IsConnectionAvailable())
        {
            DropPeer();
            _peer = _server.TakeConnection();
        }

        if (_peer == null)
            return;

        _peer.Poll();
        if (_peer.GetStatus() != StreamPeerTcp.Status.Connected)
        {
            DropPeer();
            return;
        }

        // Drain any reply bytes left over from a previous frame before reading more.
        FlushTx();

        int available = _peer.GetAvailableBytes();
        if (available > 0)
        {
            // Godot 4.7 binds StreamPeer.GetPartialData to Godot.Collections.Array
            // shaped [error, PackedByteArray]; add the trailing byte payload.
            AppendBytes(_peer.GetPartialData(available));
        }

        while (true)
        {
            int newline = _rx.IndexOf((byte)'\n');
            if (newline < 0)
                break;

            var frameBytes = _rx.GetRange(0, newline).ToArray();
            _rx.RemoveRange(0, newline + 1);
            HandleLine(Encoding.UTF8.GetString(frameBytes));
        }
    }

    public override void _ExitTree()
    {
        Shutdown();
    }

    /// <summary>Deletes the discovery file and releases the listener + peer. Idempotent.</summary>
    public void Shutdown()
    {
        if (_discoveryPath != null && File.Exists(_discoveryPath))
        {
            try
            {
                File.Delete(_discoveryPath);
            }
            catch (Exception e)
            {
                GD.PushWarning($"[ConsoleBridgeServer] could not delete discovery file: {e.Message}");
            }
        }

        _discoveryPath = null;

        DropPeer();

        _server?.Stop();
        _server = null;
    }

    /// <summary>Disposes the current peer and clears all per-connection buffers.</summary>
    private void DropPeer()
    {
        _peer?.Dispose();
        _peer = null;
        _rx.Clear();
        _txQueue.Clear();
        _txOffset = 0;
    }

    private bool TryBind()
    {
        for (int p = BasePort; p < BasePort + PortScanCount; p++)
        {
            var server = new TcpServer();
            if (server.Listen((ushort)p, BindAddress) == Error.Ok)
            {
                _server = server;
                _port = p;
                return true;
            }
        }

        return false;
    }

    private void WriteDiscovery()
    {
        string dir = Path.Combine(Path.GetTempPath(), "catsweeper-console", "bridges");
        Directory.CreateDirectory(dir);
        _discoveryPath = Path.Combine(dir, $"{OS.GetProcessId()}.json");

        string projectRoot = ProjectSettings.GlobalizePath("res://").TrimEnd(Path.DirectorySeparatorChar);

        var payload = new Dictionary<string, object>
        {
            ["pid"] = OS.GetProcessId(),
            ["port"] = _port,
            ["context"] = Context,
            ["projectRoot"] = projectRoot,
            ["startedAt"] = DateTime.UtcNow.ToString("o"),
            ["protocol"] = ProtocolId,
        };

        File.WriteAllText(_discoveryPath, JsonSerializer.Serialize(payload));
    }

    private void AppendBytes(Godot.Collections.Array chunk)
    {
        // Godot 4.7 binds StreamPeer.GetPartialData to a 2-element Array
        // [error, PackedByteArray]; append the trailing byte payload.
        foreach (var element in chunk)
        {
            if (element.VariantType == Variant.Type.PackedByteArray)
                _rx.AddRange(element.As<byte[]>());
        }
    }

    private void HandleLine(string line)
    {
        string id = "";
        string command = null;

        if (!string.IsNullOrWhiteSpace(line))
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (doc.RootElement.TryGetProperty("id", out var idEl))
                        id = idEl.ValueKind == JsonValueKind.String ? idEl.GetString() ?? "" : idEl.ToString();
                    if (doc.RootElement.TryGetProperty("command", out var cmdEl) && cmdEl.ValueKind == JsonValueKind.String)
                        command = cmdEl.GetString();
                }
            }
            catch (JsonException)
            {
                id = "";
                command = null;
            }
        }

        if (string.IsNullOrWhiteSpace(command))
        {
            WriteReply(id, false, new List<Dictionary<string, object>>(), new List<Dictionary<string, object>>());
            return;
        }

        var (success, logs, events) = RunCommand(command);
        WriteReply(id, success, logs, events);
    }

    private void WriteReply(
        string id,
        bool success,
        List<Dictionary<string, object>> logs,
        List<Dictionary<string, object>> events)
    {
        if (_peer == null || _peer.GetStatus() != StreamPeerTcp.Status.Connected)
            return;

        string json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["id"] = id,
            ["success"] = success,
            ["logs"] = logs,
            ["events"] = events,
        });

        // Queue the complete reply; FlushTx() writes it (and any earlier queued replies)
        // in order, so a backpressured reply is never clobbered by a later request.
        _txQueue.Enqueue(Encoding.UTF8.GetBytes(json + "\n"));
        FlushTx();
    }

    /// <summary>
    /// Writes queued replies in order. <c>PutPartialData</c> may accept only a prefix, so the
    /// unsent remainder of the current head is kept and retried on later frames; a write error
    /// drops the peer. The queue preserves one complete line per request under pipelining.
    /// </summary>
    private void FlushTx()
    {
        while (_txQueue.Count > 0)
        {
            if (_peer == null || _peer.GetStatus() != StreamPeerTcp.Status.Connected)
            {
                DropPeer();
                return;
            }

            byte[] head = _txQueue.Peek();
            if (_txOffset >= head.Length)
            {
                _txQueue.Dequeue();
                _txOffset = 0;
                continue;
            }

            var remaining = new byte[head.Length - _txOffset];
            Buffer.BlockCopy(head, _txOffset, remaining, 0, remaining.Length);

            // Godot returns [Error, bytesWritten] as a Godot.Collections.Array.
            var result = _peer.PutPartialData(remaining);
            Error error = Error.Ok;
            int written = 0;
            if (result.Count >= 1)
                error = (Error)result[0].AsInt64();
            if (result.Count >= 2)
                written = (int)result[1].AsInt64();

            if (error != Error.Ok)
            {
                GD.PushWarning($"[ConsoleBridgeServer] reply write failed: {error}");
                DropPeer();
                return;
            }

            if (written <= 0)
            {
                // Socket buffer full; keep the reply queued and retry on the next frame.
                return;
            }

            _txOffset += written;
        }
    }

    private static (bool success, List<Dictionary<string, object>> logs, List<Dictionary<string, object>> events)
        RunCommand(string command)
    {
        var logs = new List<Dictionary<string, object>>();
        var events = new List<Dictionary<string, object>>();
        bool success;
        try
        {
            success = CommandConsoleCore.ExecuteAndCapture(
                CommandService.instance,
                command,
                onLog: (message, level) => logs.Add(new Dictionary<string, object>
                {
                    ["message"] = message,
                    ["level"] = level.ToString(),
                }),
                onEvent: evt => events.Add(new Dictionary<string, object>
                {
                    ["success"] = evt.Success,
                    ["error"] = evt.Message,
                }));
        }
        catch (Exception e)
        {
            logs.Add(new Dictionary<string, object>
            {
                ["message"] = $"exception: {e.Message}",
                ["level"] = "Error",
            });
            success = false;
        }

        return (success, logs, events);
    }
}

#endif
