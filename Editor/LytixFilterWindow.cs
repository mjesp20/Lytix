using System;
using System.Collections.Generic;
using System.Linq;
using LytixInternal;
using UnityEditor;
using UnityEngine;

// ---------------------------------------------------------------------------
//  LytixFilterWindow
//  Displays all unique ScriptName.VariableName args found in cached entries,
//  grouped into collapsible script sections. Each variable can be assigned
//  a FlagFilter (operator + value) that is persisted via EditorPrefs.
// ---------------------------------------------------------------------------
public class LytixFilterWindow : EditorWindow
{
    // -----------------------------------------------------------------------
    //  Types  (mirrors the old QATool types – keep in sync or share via a
    //          common static class)
    // -----------------------------------------------------------------------
    public enum FilterOperator
    {
        Ignore, Equal, NotEqual,
        GreaterThan, GreaterThanOrEqual,
        LessThan, LessThanOrEqual
    }

    public class FlagFilter
    {
        public bool           enabled;
        public FilterOperator op;
        public object         value;
    }

    // Operator labels shown in the UI
    private static readonly Dictionary<FilterOperator, string> OpLabel =
        new Dictionary<FilterOperator, string>
        {
            { FilterOperator.Ignore,             "—"  },
            { FilterOperator.Equal,              "="  },
            { FilterOperator.NotEqual,           "≠"  },
            { FilterOperator.GreaterThan,        ">"  },
            { FilterOperator.GreaterThanOrEqual, "≥"  },
            { FilterOperator.LessThan,           "<"  },
            { FilterOperator.LessThanOrEqual,    "≤"  },
        };

    // Operators that only make sense for numeric types
    private static readonly FilterOperator[] NumericOps =
    {
        FilterOperator.Ignore,
        FilterOperator.Equal, FilterOperator.NotEqual,
        FilterOperator.GreaterThan, FilterOperator.GreaterThanOrEqual,
        FilterOperator.LessThan,   FilterOperator.LessThanOrEqual,
    };

    // Operators that make sense for bool / string
    private static readonly FilterOperator[] EqualityOps =
    {
        FilterOperator.Ignore,
        FilterOperator.Equal, FilterOperator.NotEqual,
    };

    private const string PrefKey = "LytixFlagFilters";

    // -----------------------------------------------------------------------
    //  Internal state
    // -----------------------------------------------------------------------

    // key  : "ScriptName.VarName"
    // value: inferred System.Type (int, float, bool, string …)
    private Dictionary<string, Type> _argTypes = new Dictionary<string, Type>();

    // key  : "ScriptName.VarName"
    private Dictionary<string, FlagFilter> _filters = new Dictionary<string, FlagFilter>();

    // key  : "ScriptName"  →  foldout open?
    private Dictionary<string, bool> _foldouts = new Dictionary<string, bool>();

    // Grouped view: script → list of varNames
    private Dictionary<string, List<string>> _grouped = new Dictionary<string, List<string>>();

    private Vector2 _scroll;

    // Temp string buffers so the value field survives repaint
    // key: "ScriptName.VarName"
    private Dictionary<string, string> _valueBuffer = new Dictionary<string, string>();

    // Styles (lazy init)
    private GUIStyle _scriptHeaderStyle;
    private GUIStyle _rowEvenStyle;
    private GUIStyle _rowOddStyle;

    // -----------------------------------------------------------------------
    //  Static entry points
    // -----------------------------------------------------------------------

    [MenuItem("Lytix/Filter Window")]
    public static LytixFilterWindow Open()
    {
        var win = GetWindow<LytixFilterWindow>("Lytix Filters");
        win.minSize = new Vector2(440, 300);
        return win;
    }

    // Call this from your main tool whenever the cached entry list changes.
    public void Refresh(IEnumerable<LytixEntry.Entry> entries)
    {
        // 1. Collect every unique "Script.Var" key and infer its type from the
        //    first non-null value found across all entries.
        var typeMap = new Dictionary<string, Type>();

        foreach (var entry in entries)
        {
            if (entry.args == null) continue;
            foreach (var kvp in entry.args)
            {
                if (!typeMap.ContainsKey(kvp.Key) && kvp.Value != null)
                    typeMap[kvp.Key] = NormalizeType(kvp.Value).GetType();
            }
        }

        _argTypes = typeMap;

        // 2. Re-group by script name
        _grouped = typeMap.Keys
            .GroupBy(k => ScriptOf(k))
            .OrderBy(g => g.Key)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(v => VarOf(v)).ToList()
            );

        // 3. Ensure every known key has a filter entry and a value buffer
        foreach (var key in typeMap.Keys)
        {
            if (!_filters.ContainsKey(key))
                _filters[key] = new FlagFilter { enabled = false, op = FilterOperator.Ignore };

            if (!_valueBuffer.ContainsKey(key))
                _valueBuffer[key] = _filters[key].value?.ToString() ?? DefaultValueString(typeMap[key]);
        }

        Repaint();
    }

    // Returns a snapshot of all currently active (enabled) filters.
    public Dictionary<string, FlagFilter> GetActiveFilters() =>
        _filters.Where(kvp => kvp.Value.enabled)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

    // -----------------------------------------------------------------------
    //  EditorWindow lifecycle
    // -----------------------------------------------------------------------

    private void OnEnable()
    {
        LoadFilters();
    }

    private void OnDisable()
    {
        SaveFilters();
    }

    private void OnGUI()
    {
        InitStyles();

        DrawToolbar();

        if (_grouped.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "No entries loaded yet. Call Refresh() from your main tool, " +
                "or open this window after loading a session.",
                MessageType.Info);
            return;
        }

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        DrawScriptList();
        EditorGUILayout.EndScrollView();
    }

    // -----------------------------------------------------------------------
    //  Drawing
    // -----------------------------------------------------------------------

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label("Script Filters", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Enable All",  EditorStyles.toolbarButton)) SetAllEnabled(true);
            if (GUILayout.Button("Disable All", EditorStyles.toolbarButton)) SetAllEnabled(false);
            if (GUILayout.Button("Reset All",   EditorStyles.toolbarButton)) ResetAllFilters();

            GUILayout.Space(4);

            if (GUILayout.Button("Save",   EditorStyles.toolbarButton)) SaveFilters();
            if (GUILayout.Button("Reload", EditorStyles.toolbarButton)) LoadFilters();
        }
    }

    private void DrawScriptList()
    {
        int scriptIndex = 0;
        foreach (var script in _grouped.Keys)
        {
            // ── Script header row ─────────────────────────────────────────
            bool isOpen = _foldouts.TryGetValue(script, out bool fo) && fo;

            var bgColor = scriptIndex % 2 == 0
                ? new Color(0.22f, 0.22f, 0.22f)
                : new Color(0.25f, 0.25f, 0.25f);

            using (new ColorScope(bgColor))
                GUILayout.Box(GUIContent.none, GUIStyle.none,
                    GUILayout.ExpandWidth(true), GUILayout.Height(1)); // subtle divider

            using (new EditorGUILayout.HorizontalScope(_scriptHeaderStyle))
            {
                // Active-filter count badge
                int activeCount = _grouped[script].Count(k => _filters.TryGetValue(k, out var f) && f.enabled);
                string badge = activeCount > 0 ? $"  [{activeCount} active]" : "";

                isOpen = EditorGUILayout.Foldout(isOpen, $"  {script}{badge}", true,
                    EditorStyles.foldoutHeader);
                _foldouts[script] = isOpen;
            }

            if (!isOpen)
            {
                scriptIndex++;
                continue;
            }

            // ── Variable rows ─────────────────────────────────────────────
            int varIndex = 0;
            foreach (var fullKey in _grouped[script])
            {
                string varName = VarOf(fullKey);
                Type   argType = _argTypes.TryGetValue(fullKey, out Type t) ? t : typeof(string);

                GUIStyle rowStyle = varIndex % 2 == 0 ? _rowEvenStyle : _rowOddStyle;
                using (new EditorGUILayout.HorizontalScope(rowStyle))
                {
                    DrawVariableRow(fullKey, varName, argType);
                }
                varIndex++;
            }

            GUILayout.Space(2);
            scriptIndex++;
        }
    }

    private void DrawVariableRow(string fullKey, string varName, Type argType)
    {
        if (!_filters.TryGetValue(fullKey, out FlagFilter filter))
        {
            filter = new FlagFilter { enabled = false, op = FilterOperator.Ignore };
            _filters[fullKey] = filter;
        }

        // Enabled toggle
        bool newEnabled = EditorGUILayout.Toggle(filter.enabled, GUILayout.Width(18));
        if (newEnabled != filter.enabled)
            filter.enabled = newEnabled;

        // Variable name label
        EditorGUI.BeginDisabledGroup(!filter.enabled);

        GUILayout.Label(varName, GUILayout.Width(180));

        // Type label (greyed out)
        GUILayout.Label(FriendlyTypeName(argType), EditorStyles.miniLabel, GUILayout.Width(48));

        // Operator popup
        FilterOperator[] availableOps = IsNumeric(argType) ? NumericOps : EqualityOps;
        string[] opLabels = availableOps.Select(o => OpLabel[o]).ToArray();

        int currentOpIdx = Array.IndexOf(availableOps, filter.op);
        if (currentOpIdx < 0) currentOpIdx = 0;

        int newOpIdx = EditorGUILayout.Popup(currentOpIdx, opLabels, GUILayout.Width(44));
        filter.op = availableOps[newOpIdx];

        // Value field
        if (filter.op != FilterOperator.Ignore)
            DrawValueField(fullKey, argType, filter);
        else
            GUILayout.FlexibleSpace();

        EditorGUI.EndDisabledGroup();

        // Reset button
        if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(20)))
            ResetFilter(fullKey, argType);
    }

    private void DrawValueField(string fullKey, Type argType, FlagFilter filter)
    {
        if (!_valueBuffer.ContainsKey(fullKey))
            _valueBuffer[fullKey] = filter.value?.ToString() ?? DefaultValueString(argType);

        // Bool → Toggle
        if (argType == typeof(bool))
        {
            bool cur = filter.value is bool b && b;
            bool next = EditorGUILayout.Toggle(cur, GUILayout.Width(20));
            if (next != cur)
            {
                filter.value = next;
                _valueBuffer[fullKey] = next.ToString();
            }
            GUILayout.FlexibleSpace();
            return;
        }

        // Int → IntField
        if (argType == typeof(int))
        {
            int cur = filter.value is int i ? i : 0;
            int next = EditorGUILayout.IntField(cur, GUILayout.MinWidth(60), GUILayout.ExpandWidth(true));
            if (next != cur)
            {
                filter.value = next;
                _valueBuffer[fullKey] = next.ToString();
            }
            return;
        }

        // Float → FloatField
        if (argType == typeof(float))
        {
            float cur = filter.value is float f ? f : 0f;
            float next = EditorGUILayout.FloatField(cur, GUILayout.MinWidth(60), GUILayout.ExpandWidth(true));
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (next != cur)
            {
                filter.value = next;
                _valueBuffer[fullKey] = next.ToString("R");
            }
            return;
        }

        // String / fallback → TextField
        string strCur = _valueBuffer[fullKey];
        string strNext = EditorGUILayout.TextField(strCur, GUILayout.MinWidth(60), GUILayout.ExpandWidth(true));
        if (strNext != strCur)
        {
            _valueBuffer[fullKey] = strNext;
            filter.value = strNext;
        }
    }

    // -----------------------------------------------------------------------
    //  Persistence
    // -----------------------------------------------------------------------

    private void SaveFilters()
    {
        var parts = new List<string>();
        foreach (var kvp in _filters)
        {
            string val = kvp.Value.value?.ToString() ?? "null";
            parts.Add($"{kvp.Key}:{kvp.Value.enabled}:{kvp.Value.op}:{val}");
        }
        EditorPrefs.SetString(PrefKey, string.Join("|", parts));
    }

    private void LoadFilters()
    {
        string raw = EditorPrefs.GetString(PrefKey, null);
        if (string.IsNullOrEmpty(raw)) return;

        foreach (string section in raw.Split('|'))
        {
            string[] parts = section.Split(':');
            if (parts.Length != 4) continue;

            string key  = parts[0];
            bool enabled = bool.TryParse(parts[1], out bool e) && e;
            FilterOperator op = Enum.TryParse(parts[2], out FilterOperator o) ? o : FilterOperator.Ignore;
            string rawVal = parts[3];

            object value = null;
            if (rawVal != "null" && _argTypes.TryGetValue(key, out Type t))
            {
                try { value = Convert.ChangeType(rawVal, t); }
                catch { value = rawVal; }
            }

            _filters[key] = new FlagFilter { enabled = enabled, op = op, value = value };
            _valueBuffer[key] = rawVal != "null" ? rawVal : DefaultValueString(
                _argTypes.TryGetValue(key, out Type t2) ? t2 : typeof(string));
        }
    }

    // -----------------------------------------------------------------------
    //  Helpers
    // -----------------------------------------------------------------------

    private static string ScriptOf(string fullKey)
    {
        int dot = fullKey.IndexOf('.');
        return dot >= 0 ? fullKey[..dot] : fullKey;
    }

    private static string VarOf(string fullKey)
    {
        int dot = fullKey.IndexOf('.');
        return dot >= 0 ? fullKey[(dot + 1)..] : fullKey;
    }

    private static object NormalizeType(object input) => input switch
    {
        long   l => (int)l,
        double d => (float)d,
        _        => input
    };

    private static bool IsNumeric(Type t) =>
        t == typeof(int) || t == typeof(float) || t == typeof(double) || t == typeof(long);

    private static string FriendlyTypeName(Type t)
    {
        if (t == typeof(int))    return "int";
        if (t == typeof(float))  return "float";
        if (t == typeof(bool))   return "bool";
        if (t == typeof(string)) return "string";
        return t.Name;
    }

    private static string DefaultValueString(Type t)
    {
        if (t == typeof(bool))   return "False";
        if (t == typeof(int))    return "0";
        if (t == typeof(float))  return "0";
        return "";
    }

    private void SetAllEnabled(bool state)
    {
        foreach (var f in _filters.Values) f.enabled = state;
        Repaint();
    }

    private void ResetAllFilters()
    {
        foreach (var kvp in _filters)
            ResetFilter(kvp.Key, _argTypes.TryGetValue(kvp.Key, out Type t) ? t : typeof(string));
        Repaint();
    }

    private void ResetFilter(string key, Type argType)
    {
        _filters[key] = new FlagFilter { enabled = false, op = FilterOperator.Ignore };
        _valueBuffer[key] = DefaultValueString(argType);
    }

    // -----------------------------------------------------------------------
    //  Style init
    // -----------------------------------------------------------------------

    private void InitStyles()
    {
        if (_scriptHeaderStyle != null) return;

        _scriptHeaderStyle = new GUIStyle
        {
            padding   = new RectOffset(4, 4, 3, 3),
            margin    = new RectOffset(0, 0, 1, 0),
            fixedHeight = 22,
        };
        _scriptHeaderStyle.normal.background =
            MakeTex(1, 1, new Color(0.18f, 0.18f, 0.18f));

        _rowEvenStyle = new GUIStyle
        {
            padding = new RectOffset(24, 4, 2, 2),
            margin  = new RectOffset(0, 0, 0, 0),
        };
        _rowEvenStyle.normal.background =
            MakeTex(1, 1, new Color(0.21f, 0.21f, 0.21f));

        _rowOddStyle = new GUIStyle
        {
            padding = new RectOffset(24, 4, 2, 2),
            margin  = new RectOffset(0, 0, 0, 0),
        };
        _rowOddStyle.normal.background =
            MakeTex(1, 1, new Color(0.23f, 0.23f, 0.23f));
    }

    private static Texture2D MakeTex(int w, int h, Color col)
    {
        var pix = new Color[w * h];
        for (int i = 0; i < pix.Length; i++) pix[i] = col;
        var tex = new Texture2D(w, h);
        tex.SetPixels(pix);
        tex.Apply();
        return tex;
    }

    // -----------------------------------------------------------------------
    //  Tiny scope helper
    // -----------------------------------------------------------------------
    private struct ColorScope : IDisposable
    {
        private readonly Color _prev;
        public ColorScope(Color bg)  { _prev = GUI.backgroundColor; GUI.backgroundColor = bg; }
        public void Dispose()        { GUI.backgroundColor = _prev; }
    }
}