using LytixInternal;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class LytixWindow : EditorWindow
{
    // ──────────────────────────────────────────────
    //  Scene data
    // ──────────────────────────────────────────────

    private static List<List<LytixEntry.Entry>> entriesByFile = new List<List<LytixEntry.Entry>>();
    private static List<List<Vector3>>          trailsByFile  = new List<List<Vector3>>();
    private static List<LytixEntry.Entry>        cachedEntries = new List<LytixEntry.Entry>();

    private static readonly Color[] playerPalette = { Color.red, Color.cyan, Color.green, Color.yellow, Color.magenta };

    // ──────────────────────────────────────────────
    //  Candidate caches
    //  Rebuilt on data load, trail change, or
    //  feedbackPreviewLength change — not every frame
    // ──────────────────────────────────────────────

    private static List<(LytixEntry.Entry entry, int fileIndex)> _eventCandidates
        = new List<(LytixEntry.Entry, int)>();

    private static List<(LytixEntry.Entry entry, string preview, string fullText)> _feedbackCandidates
        = new List<(LytixEntry.Entry, string, string)>();

    // ──────────────────────────────────────────────
    //  Heatmap state
    // ──────────────────────────────────────────────

    private static Dictionary<Vector3Int, int>           _heatmap         = new Dictionary<Vector3Int, int>();
    private static float                                  _lastHeatmapCellSize = -1f;

    // Percentile thresholds — computed once, not every frame
    private static int   _cachedMinThreshold;
    private static int   _cachedMaxThreshold;
    private static float _cachedLogMin;
    private static float _cachedLogMax;

    // Back-to-front sorted cell list — only rebuilds when camera moves far enough
    private static List<KeyValuePair<Vector3Int, int>> _sortedCells    = new List<KeyValuePair<Vector3Int, int>>();
    private static Vector3                              _lastSortCamPos = Vector3.positiveInfinity;
    private const  float                               SortRebuildThreshold = 1f;

    // ──────────────────────────────────────────────
    //  Line texture
    // ──────────────────────────────────────────────

    private static Texture2D _lineTex;
    private static Texture2D LineTex
    {
        get
        {
            if (_lineTex == null)
            {
                _lineTex = new Texture2D(1, 1);
                _lineTex.SetPixel(0, 0, Color.white);
                _lineTex.Apply();
            }
            return _lineTex;
        }
    }

    // ──────────────────────────────────────────────
    //  Cached GUI styles
    // ──────────────────────────────────────────────

    private static GUIStyle _feedbackLabelStyle;
    private static GUIStyle _feedbackOutlineStyle;
    private static GUIStyle _eventLabelStyle;
    private static GUIStyle _eventLabelOutlineStyle;
    private static GUIStyle _eventAbbrStyle;
    private static GUIStyle _eventAbbrOutlineStyle;

    // ──────────────────────────────────────────────
    //  Temporal trail state
    // ──────────────────────────────────────────────

    private static List<Vector3> temporalTrail  = new List<Vector3>();
    public  static int           activeFileIndex = 0;
    private static int           scrubIndex      = 0;
    private static bool          isPreview       = true;

    // ──────────────────────────────────────────────
    //  Editor state
    // ──────────────────────────────────────────────

    private LytixFilterWindow _filterWindow;
    private int               _lastHotControl;

    // ──────────────────────────────────────────────
    //  Foldout state
    // ──────────────────────────────────────────────

    private bool _foldVisualisation  = true;
    private bool _foldHeatmap        = true;
    private bool _foldTracking       = true;
    private bool _foldTemporalTrail  = true;

    // ──────────────────────────────────────────────
    //  Lifecycle
    // ──────────────────────────────────────────────

    [MenuItem("Window/Lytix")]
    public static void OpenWindow() => GetWindow<LytixWindow>("Lytix Window");

    void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        RepaintScene();
    }

    void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    // ──────────────────────────────────────────────
    //  GUI
    // ──────────────────────────────────────────────

    private void OnGUI()
    {
        GUILayout.Space(8);
        GUILayout.Label("Lytix", EditorStyles.boldLabel);
        DrawHorizontalLine();

        DrawReloadButton();
        GUILayout.Space(4);
        
        if (GUILayout.Button("Filters")) OpenFilterWindow();


        DrawHorizontalLine();

        // ── Visualisation ──────────────────────────
        GUILayout.Space(4);
        DrawSection("Visualisation", ref _foldVisualisation, DrawVisualisationControls);
        DrawHorizontalLine();

        // ── Heatmap ────────────────────────────────
        GUILayout.Space(4);
        DrawSection("Heatmap", ref _foldHeatmap, DrawHeatmapControls);
        DrawHorizontalLine();

        // ── Tracking Settings ──────────────────────
        GUILayout.Space(4);
        DrawSection("Tracking Settings", ref _foldTracking, DrawTrackingControls);
        DrawHorizontalLine();

        // ── Temporal Trail ─────────────────────────
        GUILayout.Space(4);
        DrawTemporalTrailSection();
    }

    // ──────────────────────────────────────────────
    //  GUI sections
    // ──────────────────────────────────────────────

    private void DrawVisualisationControls()
    {
        EditorGUI.BeginChangeCheck();

        GUILayout.Label("Toggles", EditorStyles.miniBoldLabel);
        LytixSettings.Set("Lytix.ShowGhostTrails",
            EditorGUILayout.Toggle(new GUIContent("Show Ghost Trails",   "Draws a trail for each player file loaded."),
                LytixSettings.Get<bool>("Lytix.ShowGhostTrails")));
        LytixSettings.Set("Lytix.ShowHeatMap",
            EditorGUILayout.Toggle(new GUIContent("Show Heat Map",       "Overlays a heatmap showing where players spent the most time."),
                LytixSettings.Get<bool>("Lytix.ShowHeatMap")));
        LytixSettings.Set("Lytix.ShowFeedbackNotes",
            EditorGUILayout.Toggle(new GUIContent("Show Feedback Notes", "Displays in-world labels for any feedback notes recorded."),
                LytixSettings.Get<bool>("Lytix.ShowFeedbackNotes")));
        LytixSettings.Set("Lytix.ShowEvents",
            EditorGUILayout.Toggle(new GUIContent("Show Events",         "Renders clickable events, each showing individual event data."),
                LytixSettings.Get<bool>("Lytix.ShowEvents")));

        GUILayout.Space(4);
        GUILayout.Label("Display", EditorStyles.miniBoldLabel);

/*
        LytixSettings.Set("Lytix.FeedbackKeyCode",
            EditorGUILayout.TextField(
                new GUIContent("Feedback Key", "The key players press in-game to submit a feedback note."),
                LytixSettings.Get<string>("Lytix.FeedbackKeyCode")));
*/

        LytixSettings.Set("Lytix.FeedbackPreviewLength",
            EditorGUILayout.IntSlider(
                new GUIContent("Feedback Preview Chars", "How many characters of a feedback note are shown in the scene view before truncating."),
                LytixSettings.Get<int>("Lytix.FeedbackPreviewLength", 10), 1, 20));

        LytixSettings.Set("Lytix.RenderRadius",
            EditorGUILayout.Slider(
                new GUIContent("Render Radius", "Only show events and feedback within this distance from the Scene camera."),
                LytixSettings.Get<float>("Lytix.RenderRadius", 100f), 1f, 500f));

        LytixSettings.Set("Lytix.GhostTrailThickness",
            EditorGUILayout.Slider(
                new GUIContent("Trail Thickness", "Controls the thickness of all ghost trail lines in the scene view."),
                LytixSettings.Get<float>("Lytix.GhostTrailThickness", 1f), 1f, 10f));

        if (EditorGUI.EndChangeCheck())
        {
            RebuildCandidateCaches();
            RepaintScene();
        }
    }

    private void DrawHeatmapControls()
    {
        EditorGUI.BeginChangeCheck();

        LytixSettings.Set("Lytix.HeatmapCellSize",
            EditorGUILayout.Slider(
                new GUIContent("Cell Size", "The size of each heatmap grid cell. Larger cells are broader but less precise."),
                LytixSettings.Get<float>("Lytix.HeatmapCellSize", 1f), 0.2f, 5f));

        LytixSettings.Set("Lytix.HeatmapOpacity",
            EditorGUILayout.Slider(
                new GUIContent("Opacity", "Overall transparency of the heatmap overlay."),
                LytixSettings.Get<float>("Lytix.HeatmapOpacity", 0.6f), 0f, 1f));

        LytixSettings.Set("Lytix.HeatmapContrast",
            EditorGUILayout.Slider(
                new GUIContent("Contrast", "Increases the difference between low and high density areas."),
                LytixSettings.Get<float>("Lytix.HeatmapContrast", 1f), 0f, 3f));

        GUILayout.Space(2);
        GUILayout.Label("Percentile Range", EditorStyles.miniLabel);

        float min = LytixSettings.Get<float>("Lytix.HeatmapMinPercentile", 0f);
        float max = LytixSettings.Get<float>("Lytix.HeatmapMaxPercentile", 1f);

        EditorGUILayout.MinMaxSlider(
            new GUIContent("", "Clamps which density percentile range is shown. Use to filter out extreme outliers."),
            ref min, ref max, 0f, 1f);

        EditorGUILayout.BeginHorizontal();
        min = EditorGUILayout.FloatField(min, GUILayout.MaxWidth(50));
        max = EditorGUILayout.FloatField(max, GUILayout.MaxWidth(50));
        EditorGUILayout.EndHorizontal();

        LytixSettings.Set("Lytix.HeatmapMinPercentile", Mathf.Clamp01(min));
        LytixSettings.Set("Lytix.HeatmapMaxPercentile", Mathf.Clamp01(max));

        bool anyHeatmapChanged = EditorGUI.EndChangeCheck();

        // ── Slider-release detection ────────────────
        // Only rebuild the grid (expensive) when the user releases the cell-size
        // slider, not on every dragged tick.
        bool controlJustReleased = _lastHotControl != 0 && GUIUtility.hotControl == 0;

        if (controlJustReleased)
        {
            float currentCellSize = LytixSettings.Get<float>("Lytix.HeatmapCellSize", 1f);

            if (!Mathf.Approximately(currentCellSize, _lastHeatmapCellSize))
                LoadHeatmap();       // full grid + threshold rebuild
            else
                ComputeHeatmapThresholds(); // percentile sliders only

            RepaintScene();
        }
        else if (anyHeatmapChanged)
        {
            // Opacity / contrast — just repaint, no rebuild needed
            RepaintScene();
        }

        _lastHotControl = GUIUtility.hotControl;
    }

    private void DrawTrackingControls()
    {
        // Changes here are read by the runtime tracker — no reload needed,
        // just persist them so the next session picks them up.
        LytixSettings.Set("Lytix.ServerTracking",
            EditorGUILayout.Toggle(
                new GUIContent("Server Tracking", "Send telemetry to a remote server in addition to local files."),
                LytixSettings.Get<bool>("Lytix.ServerTracking", false)));

        LytixSettings.Set("Lytix.TrackPosition",
            EditorGUILayout.Toggle(
                new GUIContent("Track Position", "Record player position over time."),
                LytixSettings.Get<bool>("Lytix.TrackPosition", true)));

        LytixSettings.Set("Lytix.BatchFrequency",
            EditorGUILayout.Slider(
                new GUIContent("Batch Frequency", "How often (in seconds) batched data is flushed to disk or the server."),
                LytixSettings.Get<float>("Lytix.BatchFrequency", 1f), 0.1f, 10f));

        LytixSettings.Set("Lytix.DataPointsPerSecond",
            EditorGUILayout.FloatField(
                new GUIContent("Data Points / Sec", "How many position data points are recorded per second during a session."),
                LytixSettings.Get<float>("Lytix.DataPointsPerSecond", 10f)));
    }

    private void DrawTemporalTrailSection()
    {
        DrawSection("Temporal Trail", ref _foldTemporalTrail, () =>
        {
            DrawTemporalTrailLoadButtons();

            if (trailsByFile.Count == 0) return;

            GUILayout.Space(4);
            DrawFilePicker();

            if (temporalTrail.Count == 0) return;

            GUILayout.Space(4);
            DrawScrubber();
        });
    }

    private void DrawTemporalTrailLoadButtons()
    {
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Load Trail"))
            LoadFileAtIndex(0);

        if (GUILayout.Button("Unload Trail"))
            UnloadTemporalTrail();

        EditorGUILayout.EndHorizontal();
    }

    private void DrawFilePicker()
    {
        GUILayout.Label("File Browser", EditorStyles.miniBoldLabel);
        GUILayout.Space(2);
        GUILayout.Label($"Player File: {activeFileIndex + 1} / {trailsByFile.Count}", EditorStyles.miniLabel);

        EditorGUILayout.BeginHorizontal();

        EditorGUI.BeginDisabledGroup(activeFileIndex <= 0);
        if (GUILayout.Button("◀ Prev"))
            LoadFileAtIndex(activeFileIndex - 1);
        EditorGUI.EndDisabledGroup();

        EditorGUI.BeginDisabledGroup(activeFileIndex >= trailsByFile.Count - 1);
        if (GUILayout.Button("Next ▶"))
            LoadFileAtIndex(activeFileIndex + 1);
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.EndHorizontal();
    }

    private void DrawScrubber()
    {
        GUILayout.Label("Scrubber", EditorStyles.miniBoldLabel);
        GUILayout.Label($"Point: {scrubIndex} / {temporalTrail.Count - 1}", EditorStyles.miniLabel);

        int newIndex = (int)EditorGUILayout.Slider(scrubIndex, 0, temporalTrail.Count - 1);
        if (newIndex != scrubIndex)
        {
            scrubIndex = newIndex;
            isPreview  = false;
            RepaintScene();
        }
    }

    // ──────────────────────────────────────────────
    //  Layout helpers
    // ──────────────────────────────────────────────

    private void DrawSection(string title, ref bool foldout, System.Action drawContent)
    {
        foldout = EditorGUILayout.Foldout(foldout, title, true, EditorStyles.foldoutHeader);
        if (!foldout) return;

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Space(2);
        drawContent();
        GUILayout.Space(2);
        EditorGUILayout.EndVertical();
    }

    private void DrawHorizontalLine(float topSpacing = 4f, float bottomSpacing = 4f)
    {
        GUILayout.Space(topSpacing);
        Rect r = EditorGUILayout.GetControlRect(false, 1f);
        EditorGUI.DrawRect(r, new Color(0.35f, 0.35f, 0.35f, 1f));
        GUILayout.Space(bottomSpacing);
    }

    private void DrawReloadButton()
    {
        GUIStyle style = new GUIStyle(GUI.skin.button)
        {
            fontSize    = 13,
            fontStyle   = FontStyle.Bold,
            fixedHeight = 46f,
            alignment   = TextAnchor.MiddleCenter,
        };

        Color prev = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0f, 1f, 0.35f, 1f);
        if (GUILayout.Button("↺  Refresh Data", style))
            ReloadData();
        GUI.backgroundColor = prev;
    }

    // ──────────────────────────────────────────────
    //  Filter window
    // ──────────────────────────────────────────────

    private void OpenFilterWindow()
    {
        _filterWindow = LytixFilterWindow.Open();
        _filterWindow.Refresh(cachedEntries);
    }

    // ──────────────────────────────────────────────
    //  Scene rendering
    // ──────────────────────────────────────────────

    static void OnSceneGUI(SceneView sceneView)
    {
        DrawGhostTrails();
        DrawHeatmap();
        DrawFeedbackNotes();
        DrawEvents();
        DrawTemporalTrail();
    }

    private static bool IsInFrontOfCamera(Vector3 worldPos, Camera cam)
    {
        Vector3 toPoint = worldPos - cam.transform.position;
        return Vector3.Dot(cam.transform.forward, toPoint) > 0.1f;
    }

    private static bool IsWithinRadius(Vector3 worldPos, Camera cam, float radius)
    {
        return Vector3.Distance(cam.transform.position, worldPos) <= radius;
    }

    private static void DrawGhostTrails()
    {
        if (!LytixSettings.Get<bool>("Lytix.ShowGhostTrails") || trailsByFile.Count == 0) return;
        if (Event.current.type != EventType.Repaint) return;

        bool trailActive = temporalTrail.Count > 0;

        for (int i = 0; i < trailsByFile.Count; i++)
        {
            if (trailActive && i != activeFileIndex) continue;

            List<Vector3> trail = trailsByFile[i];
            if (trail == null || trail.Count == 0) continue;

            Handles.color = playerPalette[i % playerPalette.Length];
            Handles.DrawAAPolyLine(LineTex, LytixSettings.Get<float>("Lytix.GhostTrailThickness", 1f), trail.ToArray());
        }
    }

    private static void DrawHeatmap()
    {
        if (!LytixSettings.Get<bool>("Lytix.ShowHeatMap") || _heatmap == null || _heatmap.Count == 0) return;
        if (Event.current.type != EventType.Repaint) return;

        Camera cam = SceneView.currentDrawingSceneView?.camera;
        if (cam == null) return;

        float   cellSize = LytixSettings.Get<float>("Lytix.HeatmapCellSize", 1f);
        float   opacity  = LytixSettings.Get<float>("Lytix.HeatmapOpacity",  0.6f);
        float   contrast = LytixSettings.Get<float>("Lytix.HeatmapContrast", 1f);
        Vector3 camPos   = cam.transform.position;

        if (Vector3.Distance(camPos, _lastSortCamPos) > SortRebuildThreshold)
        {
            _sortedCells = _heatmap
                .OrderByDescending(kvp => Vector3.Distance(camPos, CellCenter(kvp.Key, cellSize)))
                .ToList();
            _lastSortCamPos = camPos;
        }

        foreach (KeyValuePair<Vector3Int, int> kvp in _sortedCells)
        {
            if (kvp.Value < _cachedMinThreshold || kvp.Value > _cachedMaxThreshold) continue;

            float logRange   = Mathf.Log(_cachedLogMax - _cachedLogMin + 2f);
            float normalized = logRange > 0f
                ? Mathf.Log(kvp.Value - _cachedLogMin + 2f) / logRange
                : 0f;

            normalized = Mathf.Pow(Mathf.Clamp01(normalized), contrast);

            Color col = Color.Lerp(Color.green, Color.red, normalized);
            col.a = opacity;

            DrawHeatmapCell(CellCenter(kvp.Key, cellSize), cellSize, col);
        }
    }

    private static void DrawHeatmapCell(Vector3 center, float size, Color color)
    {
        float h = size * 0.5f;
        Handles.color = color;

        Vector3 p000 = center + new Vector3(-h, -h, -h);
        Vector3 p001 = center + new Vector3(-h, -h,  h);
        Vector3 p010 = center + new Vector3(-h,  h, -h);
        Vector3 p011 = center + new Vector3(-h,  h,  h);
        Vector3 p100 = center + new Vector3( h, -h, -h);
        Vector3 p101 = center + new Vector3( h, -h,  h);
        Vector3 p110 = center + new Vector3( h,  h, -h);
        Vector3 p111 = center + new Vector3( h,  h,  h);

        Handles.DrawAAConvexPolygon(p010, p110, p111, p011); // Top
        Handles.DrawAAConvexPolygon(p000, p001, p101, p100); // Bottom
        Handles.DrawAAConvexPolygon(p001, p011, p111, p101); // Front  (+Z)
        Handles.DrawAAConvexPolygon(p000, p100, p110, p010); // Back   (-Z)
        Handles.DrawAAConvexPolygon(p000, p010, p011, p001); // Left   (-X)
        Handles.DrawAAConvexPolygon(p100, p101, p111, p110); // Right  (+X)
    }

    private static Vector3 CellCenter(Vector3Int cell, float cellSize) =>
        new Vector3(
            cell.x * cellSize + cellSize * 0.5f,
            cell.y * cellSize + cellSize * 0.5f,
            cell.z * cellSize + cellSize * 0.5f);

    private static void DrawFeedbackNotes()
    {
        if (!LytixSettings.Get<bool>("Lytix.ShowFeedbackNotes") || _feedbackCandidates == null) return;

        Camera cam = SceneView.currentDrawingSceneView?.camera;
        if (cam == null) return;

        float renderRadius = LytixSettings.Get<float>("Lytix.RenderRadius", 100f);

        EnsureStyles();

        Handles.BeginGUI();

        foreach (var (entry, preview, fullText) in _feedbackCandidates)
        {
            Vector3 worldPos = entry.position.ToVector3();

            if (!IsInFrontOfCamera(worldPos, cam) || !IsWithinRadius(worldPos, cam, renderRadius))
                continue;

            Vector2 screenPos = HandleUtility.WorldToGUIPoint(worldPos + Vector3.up * 0.4f);

            GUIContent content = new GUIContent(preview);
            Vector2 size       = _feedbackLabelStyle.CalcSize(content);

            Rect rect = new Rect(
                screenPos.x - size.x * 0.5f,
                screenPos.y - size.y,
                size.x,
                size.y);

            DrawGUIOutlinedLabel(rect, content, _feedbackLabelStyle, _feedbackOutlineStyle);

            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                LytixInternal.LytixFeedbackInspectorWindow.Show(entry, fullText);
                e.Use();
            }
        }

        Handles.EndGUI();

        // ── 3D wire cubes at feedback positions ──
        if (Event.current.type != EventType.Repaint) return;

        Handles.color = Color.white;

        foreach (var (entry, _, _) in _feedbackCandidates)
        {
            Vector3 worldPos = entry.position.ToVector3();

            if (!IsInFrontOfCamera(worldPos, cam) || !IsWithinRadius(worldPos, cam, renderRadius))
                continue;

            Handles.DrawWireCube(worldPos, Vector3.one * 0.4f);
        }
    }

    private static void DrawEvents()
    {
        if (!LytixSettings.Get<bool>("Lytix.ShowEvents") || _eventCandidates == null) return;

        Camera sceneCamera = SceneView.currentDrawingSceneView?.camera;
        if (sceneCamera == null) return;

        float   renderRadius = LytixSettings.Get<float>("Lytix.RenderRadius", 100f);
        Vector3 camPos       = sceneCamera.transform.position;
        bool    isRepaint    = Event.current.type == EventType.Repaint;

        EnsureStyles();

        // ── 3D geometry ─────────────────────────────
        if (isRepaint)
        {
            var sorted = _eventCandidates
                .Where(t =>
                {
                    Vector3 pos = t.entry.position.ToVector3();
                    return IsInFrontOfCamera(pos, sceneCamera) && IsWithinRadius(pos, sceneCamera, renderRadius);
                })
                .OrderByDescending(t => Vector3.Distance(camPos, t.entry.position.ToVector3()))
                .ToList();

            foreach (var (entry, fileIndex) in sorted)
            {
                Vector3 pos   = entry.position.ToVector3();
                Color   color = playerPalette[fileIndex % playerPalette.Length];

                Handles.color = color;
                Handles.DrawWireCube(pos, Vector3.one * 0.5f);
                Handles.SphereHandleCap(0, pos, Quaternion.identity, 0.5f, EventType.Repaint);
            }
        }

        // ── Labels + click handling ──────────────────
        Handles.BeginGUI();

        foreach (var (entry, fileIndex) in _eventCandidates)
        {
            Vector3 pos = entry.position.ToVector3();

            if (!IsInFrontOfCamera(pos, sceneCamera) || !IsWithinRadius(pos, sceneCamera, renderRadius))
                continue;

            if (!entry.args.TryGetValue("event", out object evt) || evt == null) continue;

            string evtName = evt.ToString();
            string abbr    = evtName.Length > 2 ? evtName.Substring(0, 2).ToUpper() : evtName.ToUpper();
            Color  color   = playerPalette[fileIndex % playerPalette.Length];

            Vector2 screenTop = HandleUtility.WorldToGUIPoint(pos + Vector3.up * 0.5f);
            Vector2 screenMid = HandleUtility.WorldToGUIPoint(pos);

            // Name label
            _eventLabelStyle.normal.textColor        = color;
            _eventLabelOutlineStyle.normal.textColor = Color.black;

            GUIContent nameContent = new GUIContent(evtName);
            Vector2    nameSize    = _eventLabelStyle.CalcSize(nameContent);

            Rect nameRect = new Rect(
                screenTop.x - nameSize.x * 0.5f,
                screenTop.y - nameSize.y,
                nameSize.x, nameSize.y);

            DrawGUIOutlinedLabel(nameRect, nameContent, _eventLabelStyle, _eventLabelOutlineStyle);

            // Abbreviation label
            GUIContent abbrContent = new GUIContent(abbr);
            Vector2    abbrSize    = _eventAbbrStyle.CalcSize(abbrContent);

            Rect abbrRect = new Rect(
                screenMid.x - abbrSize.x * 0.5f,
                screenMid.y - abbrSize.y * 0.5f,
                abbrSize.x, abbrSize.y);

            _eventAbbrOutlineStyle.normal.textColor = color;
            DrawGUIOutlinedLabel(abbrRect, abbrContent, _eventAbbrStyle, _eventAbbrOutlineStyle);

            // Click area spans both labels
            Rect clickRect = Rect.MinMaxRect(
                Mathf.Min(nameRect.xMin, abbrRect.xMin),
                nameRect.yMin,
                Mathf.Max(nameRect.xMax, abbrRect.xMax),
                abbrRect.yMax);

            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && clickRect.Contains(e.mousePosition))
            {
                LytixEventInspectorWindow.Show(entry, fileIndex, color);
                e.Use();
            }
        }

        Handles.EndGUI();
    }

    private static void DrawTemporalTrail()
    {
        if (temporalTrail.Count == 0) return;
        if (Event.current.type != EventType.Repaint) return;

        int   drawUpTo   = isPreview ? temporalTrail.Count - 1 : scrubIndex;
        float thickness  = LytixSettings.Get<float>("Lytix.GhostTrailThickness", 1f) + 3f;

        Handles.color = Color.white;

        if (isPreview)
            Handles.DrawAAPolyLine(thickness, temporalTrail.Take(drawUpTo + 1).ToArray());
        else
            Handles.DrawAAPolyLine(LineTex, thickness, temporalTrail.Take(drawUpTo + 1).ToArray());

        Handles.DrawSolidDisc(temporalTrail[scrubIndex], Vector3.up, 0.2f);
    }

    // ──────────────────────────────────────────────
    //  GUI helpers
    // ──────────────────────────────────────────────

    private static void DrawGUIOutlinedLabel(Rect rect, GUIContent content, GUIStyle style, GUIStyle outlineStyle)
    {
        GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), content, outlineStyle);
        GUI.Label(rect, content, style);
    }

    private static void EnsureStyles()
    {
        if (_feedbackLabelStyle == null)
        {
            _feedbackLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Italic,
                alignment = TextAnchor.MiddleCenter,
                fontSize  = 11
            };
            _feedbackLabelStyle.normal.textColor = Color.white;
            _feedbackLabelStyle.hover.textColor  = Color.white;
        }

        if (_feedbackOutlineStyle == null)
        {
            _feedbackOutlineStyle = new GUIStyle(_feedbackLabelStyle);
            _feedbackOutlineStyle.normal.textColor = Color.black;
        }

        if (_eventLabelStyle == null)
        {
            _eventLabelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize  = 11
            };
        }

        if (_eventLabelOutlineStyle == null)
            _eventLabelOutlineStyle = new GUIStyle(_eventLabelStyle);

        if (_eventAbbrStyle == null)
        {
            _eventAbbrStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize  = 10
            };
            _eventAbbrStyle.normal.textColor = Color.black;
        }

        if (_eventAbbrOutlineStyle == null)
            _eventAbbrOutlineStyle = new GUIStyle(_eventAbbrStyle);
    }

    // ──────────────────────────────────────────────
    //  Candidate cache builder
    // ──────────────────────────────────────────────

    private static void RebuildCandidateCaches()
    {
        bool trailActive = temporalTrail.Count > 0;

        _eventCandidates = entriesByFile
            .SelectMany((file, fileIndex) => file
                .Where(e => e != null
                         && e.type == "Event"
                         && e.args != null
                         && e.args.ContainsKey("event")
                         && (!trailActive || fileIndex == activeFileIndex))
                .Select(e => (entry: e, fileIndex)))
            .ToList();

        int previewLength = LytixSettings.Get<int>("Lytix.FeedbackPreviewLength", 10);

        _feedbackCandidates = cachedEntries
            .Where(e => e?.args != null
                     && e.args.TryGetValue("note", out object n)
                     && n != null)
            .Select(e =>
            {
                string full    = e.args["note"].ToString();
                string preview = full.Length > previewLength
                                  ? full.Substring(0, previewLength) + "…"
                                  : full;
                return (entry: e, preview, fullText: full);
            })
            .ToList();
    }

    // ──────────────────────────────────────────────
    //  Data loading
    // ──────────────────────────────────────────────

    public void ReloadData()
    {
        List<List<LytixEntry.Entry>> data = LytixGlobals.LoadFromFolder();

        cachedEntries = FlattenEntries(data)
            .Where(e => e != null && e.type != null && e.position != null)
            .ToList();

        entriesByFile = data;

        trailsByFile = data
            .Select(file => file
                .Where(e => e != null && e.type == "Movement" && e.position != null)
                .Select(e => e.position.ToVector3())
                .ToList())
            .ToList();

        if (_filterWindow != null)
            _filterWindow.Refresh(cachedEntries);

        // Invalidate cached styles so they rebuild cleanly against the current skin
        _feedbackLabelStyle     = null;
        _feedbackOutlineStyle   = null;
        _eventLabelStyle        = null;
        _eventLabelOutlineStyle = null;
        _eventAbbrStyle         = null;
        _eventAbbrOutlineStyle  = null;

        RebuildCandidateCaches();
        LoadHeatmap();
        RepaintScene();
    }

    private static List<LytixEntry.Entry> FlattenEntries(List<List<LytixEntry.Entry>> data)
        => data.SelectMany(f => f).ToList();

    // ──────────────────────────────────────────────
    //  Heatmap building
    // ──────────────────────────────────────────────

    private static void LoadHeatmap()
    {
        _heatmap.Clear();

        float cellSize = LytixSettings.Get<float>("Lytix.HeatmapCellSize", 1f);
        _lastHeatmapCellSize = cellSize;
        _lastSortCamPos      = Vector3.positiveInfinity;

        foreach (LytixEntry.Entry entry in cachedEntries)
        {
            if (entry?.position == null) continue;

            Vector3    pos  = entry.position.ToVector3();
            Vector3Int cell = new Vector3Int(
                Mathf.FloorToInt(pos.x / cellSize),
                Mathf.FloorToInt(pos.y / cellSize),
                Mathf.FloorToInt(pos.z / cellSize));

            if (!_heatmap.ContainsKey(cell)) _heatmap[cell] = 0;
            _heatmap[cell]++;
        }

        ComputeHeatmapThresholds();
    }

    private static void ComputeHeatmapThresholds()
    {
        if (_heatmap.Count == 0) return;

        List<int> sorted = _heatmap.Values.OrderBy(v => v).ToList();
        int       total  = sorted.Count;

        float rawMin = LytixSettings.Get<float>("Lytix.HeatmapMinPercentile", 0f);
        float rawMax = LytixSettings.Get<float>("Lytix.HeatmapMaxPercentile", 1f);

        int minIdx = Mathf.Clamp(Mathf.FloorToInt(rawMin * (total - 1)), 0, total - 1);
        int maxIdx = Mathf.Clamp(Mathf.FloorToInt(rawMax * (total - 1)), 0, total - 1);

        _cachedMinThreshold = sorted[minIdx];
        _cachedMaxThreshold = sorted[maxIdx];
        _cachedLogMin       = Mathf.Max(1, _cachedMinThreshold);
        _cachedLogMax       = Mathf.Max(_cachedLogMin + 1, _cachedMaxThreshold);
    }

    // ──────────────────────────────────────────────
    //  Temporal trail helpers
    // ──────────────────────────────────────────────

    private static void LoadFileAtIndex(int index)
    {
        if (trailsByFile.Count == 0 || index < 0 || index >= trailsByFile.Count) return;

        activeFileIndex = index;
        temporalTrail   = trailsByFile[index];
        scrubIndex      = 0;
        isPreview       = true;

        RebuildCandidateCaches();
        RepaintScene();
    }

    private static void UnloadTemporalTrail()
    {
        temporalTrail   = new List<Vector3>();
        activeFileIndex = 0;
        scrubIndex      = 0;
        isPreview       = true;

        RebuildCandidateCaches();
        RepaintScene();
    }

    public static void SelectFile(int index) => LoadFileAtIndex(index);

    // ──────────────────────────────────────────────
    //  Utilities
    // ──────────────────────────────────────────────

    private static void RepaintScene() => SceneView.RepaintAll();

    void Test()
    {
        var argnames = new HashSet<string>();
        foreach (var entry in cachedEntries)
            entry.args?.Keys.ToList().ForEach(k => argnames.Add(k));
        foreach (var argname in argnames)
            Debug.Log(argname);
    }
}