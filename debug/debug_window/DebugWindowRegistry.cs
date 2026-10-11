using System;
using System.Collections.Generic;
using Godot;

namespace cfGodotEngine.Debug;

/// <summary>
/// Engine-free static registry of named debug windows. Stores registration metadata only and
/// performs no Godot node construction, viewport access, logging or factory execution, so it can
/// be exercised from a plain NUnit host. Consumers register a factory under a unique ordinal name
/// and later request it by name; the <see cref="DebugWindowHost"/> reacts to
/// <see cref="WindowOpenRequested"/> on the Godot main thread.
/// </summary>
public static class DebugWindowRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Registration> Registrations = new(StringComparer.Ordinal);

    /// <summary>
    /// Raised when <see cref="Open"/> accepts a request for a known name. Carries the ordinal name,
    /// title and factory captured at request time. Invoked outside the registry lock and without
    /// calling the factory; the host, not the registry, owns open-window identity.
    /// </summary>
    public static event Action<string, string, Func<Control>> WindowOpenRequested;

    /// <summary>
    /// Registers (or replaces) the window stored under <paramref name="name"/>. Names are ordinal
    /// and case-sensitive; a subsequent <see cref="Open"/> emits the registration stored last.
    /// </summary>
    public static void Register(string name, string title, Func<Control> factory)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Window name must not be blank.", nameof(name));
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Window title must not be blank.", nameof(title));
        if (factory is null)
            throw new ArgumentNullException(nameof(factory));

        lock (Gate)
        {
            Registrations[name] = new Registration(title, factory);
        }
    }

    /// <summary>
    /// Requests the window registered under <paramref name="name"/>. Returns <c>false</c> for an
    /// unknown name (no event). For a known name returns <c>true</c> and raises
    /// <see cref="WindowOpenRequested"/> even when there are no listeners; a <c>true</c> result
    /// means the request was accepted, not that content was rendered.
    /// </summary>
    public static bool Open(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        Registration registration;
        lock (Gate)
        {
            if (!Registrations.TryGetValue(name, out registration))
                return false;
        }

        WindowOpenRequested?.Invoke(name, registration.Title, registration.Factory);
        return true;
    }

    private readonly struct Registration
    {
        public Registration(string title, Func<Control> factory)
        {
            Title = title;
            Factory = factory;
        }

        public string Title { get; }

        public Func<Control> Factory { get; }
    }
}
