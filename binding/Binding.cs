using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using cfEngine;
using cfEngine.Rx;
using Godot;

namespace cfGodotEngine.Binding
{
    /// <summary>
    /// Co-located binding node. Add as a child of the UI element whose property
    /// should reflect a reactive property from the nearest <see cref="IBindingSource"/>.
    /// The target is always the parent node — no NodePath needed.
    /// </summary>
    [Tool]
    [GlobalClass]
    [CustomInspector]
    public partial class Binding : BindingBase
    {
        [Export(PropertyHint.None, "BindingApplyTo")]
        private StringName _applyTo;

        [Export] private string _format;

        [Export] private BindingConverter[] _converters;

#if DEBUG
        [ExportGroup("Debug")]
        [Export] private string _debugLastValue;
        [Export] private string _debugLastError = "";
        [Export] private bool _debugIsSubscribed;
        [Export] private string _debugSourceType = "";
        [Export] private string _debugTargetPath = "";
#endif

        public StringName ApplyTo => _applyTo;

        public static Binding Create(StringName key, StringName applyTo = null)
        {
            return new Binding { _key = key, _applyTo = applyTo };
        }

        private string XPath => GetPath();

#if DEBUG
        public override void _ValidateProperty(Godot.Collections.Dictionary property)
        {
            base._ValidateProperty(property);
            if (property["name"].AsString().StartsWith("_debug"))
            {
                var usage = (PropertyUsageFlags)property["usage"].AsInt32();
                property["usage"] = (int)(usage & ~PropertyUsageFlags.Storage);
            }
        }
#endif

        protected override void ResolveAndApply()
        {
            if (!TryResolveSource())
            {
#if DEBUG
                Log.LogWarning($"[Binding:{XPath}] No IBindingSource found in parent tree. Binding is inactive.");
                UpdateDebugState(null, "No source", null, false);
#endif
                return;
            }

            if (_key == null || _key.IsEmpty)
            {
#if DEBUG
                Log.LogWarning($"[Binding:{XPath}] Key is empty. Source '{_bindingSource.GetType().Name}' was resolved but no key was assigned.");
                UpdateDebugState(null, "Key empty", _bindingSource.GetType().Name, false);
#endif
                return;
            }

            BindingDebug.LogVerbose($"[Binding:{XPath}] Resolved source '{_bindingSource.GetType().Name}' for key '{_key}'.");

            var target = GetParentOrNull<Node>();
            UpdateDebugTargetPath(target);

            ApplyValue();
            var bindingsMap = _bindingSource.GetBindings;
            if (bindingsMap != null)
                _changeSub = bindingsMap.RegisterPropertyChange(OnKeyChanged);
#if DEBUG
            _debugIsSubscribed = true;
#endif
            _resolved = true;

            BindingDebug.LogVerbose($"[Binding:{XPath}] Subscribed to property change for key '{_key}'.");
        }

        protected override void Unsubscribe()
        {
            base.Unsubscribe();
#if DEBUG
            _debugIsSubscribed = false;
            _debugSourceType = "";
#endif
        }

        private void OnKeyChanged(string changedKey)
        {
            if (changedKey == _key.ToString())
                ApplyValue();
        }

        private void ApplyValue()
        {
            if (_bindingSource == null)
                return;

            var map = _bindingSource.GetBindings;
            if (map == null)
            {
#if DEBUG
                Log.LogWarning($"[Binding:{XPath}] Source '{_bindingSource.GetType().Name}' returned null PropertyMap.");
                UpdateDebugState(null, "Null PropertyMap", _bindingSource.GetType().Name, _debugIsSubscribed);
#endif
                return;
            }

            if (!map.GetVariant(_key.ToString(), out var value))
            {
#if DEBUG
                Log.LogWarning($"[Binding:{XPath}] Key '{_key}' not exposed by source '{_bindingSource.GetType().Name}'. Available: [{string.Join(", ", map.Keys)}].");
                UpdateDebugState("<missing>", $"Key '{_key}' not in map", _bindingSource.GetType().Name, _debugIsSubscribed);
#endif
                return;
            }

            var target = GetParentOrNull<Node>();
            if (target == null)
            {
#if DEBUG
                Log.LogWarning($"[Binding:{XPath}] Parent (target) is null; cannot apply value.");
#endif
                return;
            }

#if DEBUG
            UpdateDebugState(value.ToString(), "", _bindingSource?.GetType().Name, _debugIsSubscribed);
#endif
            BindingDebug.LogVerbose($"[Binding:{XPath}] Apply key '{_key}' value '{value.VariantType}:{value}' to target '{target.GetPath()}'.");

            try
            {
                if (!string.IsNullOrEmpty(_format))
                {
                    value = string.Format(_format, value.ToString());
                    BindingDebug.LogVerbose($"[Binding:{XPath}] Format '{_format}' -> '{value}'.");
                }

                if (_converters != null)
                {
                    foreach (var converter in _converters)
                    {
                        if (converter == null) continue;
                        value = converter.Convert(value);
                        BindingDebug.LogVerbose($"[Binding:{XPath}] Converter '{converter.GetType().Name}' -> '{value.VariantType}:{value}'.");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogException(ex, $"[Binding:{XPath}] Format/converter threw for key '{_key}'. Subsequent bindings in this source are NOT affected.");
                UpdateDebugState(value.ToString(), "Format/converter threw: " + ex.Message, _bindingSource?.GetType().Name, _debugIsSubscribed);
                return;
            }

            if (_applyTo == null || _applyTo.IsEmpty)
            {
                BindingDebug.LogVerbose($"[Binding:{XPath}] Apply To is empty; value read but no target set.");
                return;
            }
            try
            {
                if (target.HasMethod(_applyTo))
                {
                    target.Call(_applyTo, value);
                    BindingDebug.LogVerbose($"[Binding:{XPath}] Call method '{_applyTo}' on '{target.GetPath()}'.");
                }
                else
                {
                    target.Set(_applyTo, value);
                    BindingDebug.LogVerbose($"[Binding:{XPath}] Set property '{_applyTo}' on '{target.GetPath()}'.");
                }
            }
            catch (Exception ex)
            {
                UpdateDebugState(value.ToString(), $"Apply To '{_applyTo}' threw: {ex.Message}", _bindingSource?.GetType().Name, _debugIsSubscribed);
            }
        }

#if DEBUG
        [Conditional("DEBUG")]
        private void UpdateDebugState(string lastValue, string lastError, string sourceType, bool isSubscribed)
        {
            _debugLastValue = lastValue;
            _debugLastError = lastError ?? "";
            _debugSourceType = sourceType ?? "";
            _debugIsSubscribed = isSubscribed;
        }

        [Conditional("DEBUG")]
        private void UpdateDebugTargetPath(Node target)
        {
            _debugTargetPath = target?.GetPath() ?? "";
        }
#endif

        #region Inspector helpers

        public IReadOnlyList<string> GetAvailableApplyTargets()
        {
            var target = GetParentOrNull<Node>();
            if (target == null)
                return Array.Empty<string>();

            var result = new List<string>();

            var propList = target.GetPropertyList();
            foreach (var dict in propList)
            {
                var name = dict["name"].AsString();
                if (!string.IsNullOrEmpty(name))
                    result.Add(name);
            }

            var methods = target.GetMethodList();
            foreach (var dict in methods)
            {
                var name = dict["name"].AsString();
                if (!string.IsNullOrEmpty(name) && !name.StartsWith("_") && !result.Contains(name))
                    result.Add(name);
            }

            result.Sort(StringComparer.Ordinal);
            return result;
        }

        public override string ValidateBinding()
        {
            if (_key == null || _key.IsEmpty)
                return "Key is not set";

            var source = FindBindingSource();
            if (source == null)
                return "No IBindingSource found in parent tree";

            var keys = source.GetBindings?.Keys;
            if (keys != null && !ContainsKey(keys, _key.ToString()))
                return $"Key '{_key}' is not exposed by source '{source.GetType().Name}'";

            var target = GetParentOrNull<Node>();
            if (target == null)
                return "Binding has no parent node (target)";

            if (_applyTo == null || _applyTo.IsEmpty)
                return "Apply To is not set";

            return string.Empty;
        }

        private static bool ContainsKey(IReadOnlyList<string> keys, string key)
        {
            foreach (var k in keys)
            {
                if (k == key)
                    return true;
            }
            return false;
        }

        #endregion
    }
}
