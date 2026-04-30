using LytixInternal;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class LytixWindow : EditorWindow
{
    private static List<List<LytixEntry.Entry>> entriesByFile = new List<List<LytixEntry.Entry>>();
    private static List<List<Vector3>>          trailsByFile  = new List<List<Vector3>>();
    private static List<LytixEntry.Entry>        cachedEntries = new List<LytixEntry.Entry>();

    // ── Filter window reference ────────────────────────────────────────────
    private LytixFilterWindow _filterWindow;

    private static readonly Color[] playerPalette = { Color.red, Color.cyan, Color.green, Color.yellow, Color.magenta };

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

    private static List<LytixEntry.Entry> FlattenEntries(List<List<LytixEntry.Entry>> data)
        => data.SelectMany(f => f).ToList();

    void Test()
    {
        var argnames = new HashSet<string>();
        foreach (var entry in cachedEntries)
            entry.args?.Keys.ToList().ForEach(k => argnames.Add(k));

        foreach (var argname in argnames)
            Debug.Log(argname);
    }

    private void OnGUI()
    {
        GUILayout.Label("Lytix Window", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        EditorGUI.BeginChangeCheck();

        DrawReloadButton();
        DrawFilterWindowButton();   // ← new

        if (GUILayout.Button("test"))
            Test();

        GUILayout.Label("Toggles", EditorStyles.miniBoldLabel);
        LytixSettings.Set("Lytix.ShowGhostTrails",    EditorGUILayout.Toggle(new GUIContent("Show Ghost Trails",    "Draws a trail for each player file loaded."),                LytixSettings.Get<bool>("Lytix.ShowGhostTrails")));
        LytixSettings.Set("Lytix.ShowHeatMap",         EditorGUILayout.Toggle(new GUIContent("Show Heat Map",        "Overlays a heatmap showing where players spent the most time."), LytixSettings.Get<bool>("Lytix.ShowHeatMap")));
        LytixSettings.Set("Lytix.ShowFeedbackNotes",   EditorGUILayout.Toggle(new GUIContent("Show Feedback Notes",  "Displays in-world labels for any feedback notes recorded."),  LytixSettings.Get<bool>("Lytix.ShowFeedbackNotes")));
        LytixSettings.Set("Lytix.ShowEvents",          EditorGUILayout.Toggle(new GUIContent("Show Events",          "Renders clickable events, each showing individual event data."), LytixSettings.Get<bool>("Lytix.ShowEvents")));

        GUILayout.Space(4);
        GUILayout.Label("Settings", EditorStyles.miniBoldLabel);

        LytixSettings.Set("Lytix.FeedbackKeyCode",
            EditorGUILayout.TextField(
                new GUIContent("Feedback Key", "The key players press in-game to submit a feedback note."),
                LytixSettings.Get<string>("Lytix.FeedbackKeyCode")));

        LytixSettings.Set("Lytix.FeedbackPreviewLength",
            EditorGUILayout.IntSlider(
                new GUIContent("Feedback Preview Chars", "How many characters of a feedback note are shown in the scene view before truncating."),
                LytixSettings.Get<int>("Lytix.FeedbackPreviewLength", 60), 1, 20));

        LytixSettings.Set("Lytix.RenderRadius",
            EditorGUILayout.Slider(
                new GUIContent("Render Radius", "Only show events and feedback within this distance from the Scene camera."),
                LytixSettings.Get<float>("Lytix.RenderRadius", 100f), 1f, 500f));

        LytixSettings.Set("Lytix.DataPointsPerSecond",
            EditorGUILayout.FloatField(
                new GUIContent("Data Points / Sec", "How many data points are recorded per second during a session."),
                LytixSettings.Get<float>("Lytix.DataPointsPerSecond", 10f)));

        LytixSettings.Set("Lytix.GhostTrailThickness",
            EditorGUILayout.Slider(
                new GUIContent("Trail Thickness", "Controls the thickness of all ghost trail lines in the scene view."),
                LytixSettings.Get<float>("Lytix.GhostTrailThickness", 1f), 1f, 10f));

        GUILayout.Space(6);

        if (EditorGUI.EndChangeCheck())
        {
            ReloadData();
            RepaintScene();
        }

        EditorGUILayout.Space();
    }

    // ── Buttons ─────────────────────────────────────────────────────────────

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

    private void DrawFilterWindowButton()
    {
        GUIStyle style = new GUIStyle(GUI.skin.button)
        {
            fontSize    = 12,
            fixedHeight = 28f,
            alignment   = TextAnchor.MiddleCenter,
        };

        Color prev = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.4f, 0.7f, 1f, 1f);
        if (GUILayout.Button("⚙  Open Filter Window", style))
            OpenFilterWindow();
        GUI.backgroundColor = prev;
    }

    // ── Filter window ────────────────────────────────────────────────────────

    private void OpenFilterWindow()
    {
        _filterWindow = LytixFilterWindow.Open();
        _filterWindow.Refresh(cachedEntries);
    }

    // ── Data ─────────────────────────────────────────────────────────────────

    public void ReloadData()
    {
        List<List<LytixEntry.Entry>> data = LytixGlobals.LoadFromFolder();

        cachedEntries = FlattenEntries(data);
        entriesByFile = data;

        trailsByFile = data
            .Select(file => file
                .Where(e => e != null && e.type == "Movement")
                .Select(e => e.position.ToVector3())
                .ToList())
            .ToList();

        // Keep the filter window in sync if it is already open
        if (_filterWindow != null)
            _filterWindow.Refresh(cachedEntries);

        RepaintScene();
    }

    // ── Scene drawing ─────────────────────────────────────────────────────────

    private static void RepaintScene() => SceneView.RepaintAll();
    private static List<Vector3> temporalTrail = new List<Vector3>();
    public static int activeFileIndex = 0;

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

    static void OnSceneGUI(SceneView sceneView) => DrawGhostTrails();
}