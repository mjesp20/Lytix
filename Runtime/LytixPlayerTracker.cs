using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

using TMPro;
using Newtonsoft.Json;
using LytixInternal;

public class LytixPlayerTracker : MonoBehaviour
{
    // If true, send via socket to server, otherwise save locally
    bool server;
    bool trackPosition;
    // File output
    string filePath;
    StreamWriter writer;

    // Timing
    private float playSessionDuration;

    private float sampleTimer;
    private float flushTimer;

    private float sampleInterval;
    private float batchFrequency;
    private float dataPointsPerSecond;

    // Batch storage
    private List<string> batch = new List<string>();



    private PlayerInputActions inputActions;

    private static LytixPlayerTracker _instance;
    public static LytixPlayerTracker Instance
    {
        get
        {
            if (_instance == null)
            {
                GameObject trackerObject = new GameObject("LytixPlayerTracker");
                _instance = trackerObject.AddComponent<LytixPlayerTracker>();
                DontDestroyOnLoad(trackerObject);
            }
            return _instance;
        }
    }

    private void Awake()
    {
        inputActions = new PlayerInputActions();
    }

    private void OnEnable()
    {
        inputActions.Player.Enable();
        inputActions.Player.LytixFeedbackNote.performed += ctx => CreateFeedbackNotesWindow();
    }

    private void OnDisable()
    {
        inputActions.Player.Disable();
    }

    void Start()
    {
        _instance = this;

        server = LytixSettings.Get<bool>("Lytix.ServerTracking", false);
        trackPosition = LytixSettings.Get<bool>("Lytix.TrackPosition", true);
        batchFrequency = LytixSettings.Get<float>("Lytix.BatchFrequency", 1f);
        dataPointsPerSecond = LytixSettings.Get<float>("Lytix.DataPointsPerSecond", 10f);

        sampleInterval = 1f / Mathf.Max(0.0001f, dataPointsPerSecond);

        LytixTracker.Instance.CacheTrackables();

        string header = "{ \"session_start\": \"" + DateTime.UtcNow.ToString("o") + "\" }\n";

        if (server)
        {
            throw new NotImplementedException("Server tracking is not implemented yet. Please set 'server' to false.");
        }
        else
        {
            if (!Directory.Exists(LytixGlobals.folderPath))
            {
                Directory.CreateDirectory(LytixGlobals.folderPath);
            }

            int fileNameIndex = 0;

            foreach (string file in Directory.GetFiles(LytixGlobals.folderPath, "*.jsonl"))
            {
                string filename = Path.GetFileNameWithoutExtension(file);

                if (int.TryParse(filename, out int num) && num > fileNameIndex)
                    fileNameIndex = num;
            }

            fileNameIndex++;
            filePath = Path.Combine(LytixGlobals.folderPath, $"{fileNameIndex}.jsonl");

            File.AppendAllText(filePath, header);

            writer = new StreamWriter(filePath, true);
        }
    }

    void Update()
    {
        float delta = Time.deltaTime;

        playSessionDuration += delta;

        sampleTimer += delta;
        flushTimer += delta;

        while (sampleTimer >= sampleInterval)
        {
            sampleTimer -= sampleInterval;

            WriteData(LytixJSONTypes.Movement, null);
        }

        // dump batch (every x seconds)
        if (flushTimer >= batchFrequency)
        {
            flushTimer -= batchFrequency;
            FlushBatch();
        }
    }

    void WriteData(LytixJSONTypes type, Dictionary<string, object> args = null)
    {
        LytixTracker.Instance.ReadTrackableValues();


        var entry = new Dictionary<string, object>
        {
            { "type", type.ToString() },
            { "sessionTime", playSessionDuration }
        };

        // Warn if position tracking is on but the key is missing
        if (trackPosition && !LytixTracker.Instance.TrackableStore.ContainsKey("playerPosition"))
        {
            Debug.LogWarning("[Lytix] PlayerPosition not found in Store. Make sure [LytixTrackable.PlayerPosition] is applied to a Vector3.");
        }

        Dictionary<string, object> argsDict = new Dictionary<string, object>();

        // Sanitize all tracked values before serialization
        foreach (KeyValuePair<string, object> kvp in LytixTracker.Instance.TrackableStore)
        {
            // Skip position entirely if position tracking is disabled
            if (kvp.Key == "playerPosition")
            {
                if (trackPosition)
                    entry["position"] = SanitizeValue(kvp.Value); // SanitizeValue returns {x,y,z} dict
                continue;
            }
            ;

            argsDict[kvp.Key] = SanitizeValue(kvp.Value);
        }


        if (args != null)
            foreach (var kv in args)
                argsDict[kv.Key] = SanitizeValue(kv.Value);

        entry["args"] = argsDict;

        string json = JsonConvert.SerializeObject(entry);
        batch.Add(json);

        if (batch.Count >= 100)
            FlushBatch();
    }
    private static object SanitizeValue(object value)
    {
        return value switch
        {
            //easy values
            int or float or double or bool or string or long => value,

            //store unity types as dicts
            Vector2 v => new { x = v.x, y = v.y },
            Vector3 v => new { x = v.x, y = v.y, z = v.z },
            Vector4 v => new { x = v.x, y = v.y, z = v.z, w = v.w },
            Quaternion q => new { x = q.x, y = q.y, z = q.z, w = q.w },

            //unknown type invoke tostring (or null as string if its null value)
            _ => value?.ToString() ?? "null"
        };
    }
    void FlushBatch()
    {
        if (batch.Count == 0) return;

        foreach (string s in batch)
            writer.WriteLine(s);

        writer.Flush();
        batch.Clear();
    }

    void OnApplicationQuit()
    {
        FlushBatch();
        writer?.Close();
    }
    public void Event(Dictionary<string, object> args)
    {
        WriteData(LytixJSONTypes.Event, args);
    }

    public void CreateFeedbackNotesWindow(string prompt = null, Color? accentColor = null)
    {
        showingFeedbackNoteWindow = true;

        Color accent = accentColor ?? new Color(0f, 0.8f, 0.4f, 1f);

        EventSystem eventSystem = FindFirstObjectByType<EventSystem>();
        if (eventSystem == null)
        {
            eventSystem = new GameObject("EventSystem").AddComponent<EventSystem>();
            eventSystem.gameObject.AddComponent<StandaloneInputModule>();
        }

        List<Canvas> canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None).ToList();
        Canvas canvas = null;
        foreach (Canvas cv in canvases)
        {
            if (cv.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                canvas = cv;
                break;
            }
        }
        if (canvas == null)
        {
            canvas = new GameObject("Canvas").AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.gameObject.AddComponent<CanvasScaler>();
            canvas.gameObject.AddComponent<GraphicRaycaster>();
        }
        // Dark background panel
        feedbackPanel = new GameObject("FeedbackPanel");
        feedbackPanel.transform.SetParent(canvas.transform, false);
        Image panelImage = feedbackPanel.AddComponent<Image>();
        panelImage.color = new Color(0.1f, 0.1f, 0.1f, 0.95f);
        RectTransform panelRT = feedbackPanel.GetComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax = new Vector2(0.5f, 0.5f);
        panelRT.pivot = new Vector2(0.5f, 0.5f);
        panelRT.anchoredPosition = Vector2.zero;

        // Accent bar at top of panel
        GameObject accentBar = new GameObject("AccentBar");
        accentBar.transform.SetParent(feedbackPanel.transform, false);
        Image accentImage = accentBar.AddComponent<Image>();
        accentImage.color = accent;
        RectTransform accentRT = accentBar.GetComponent<RectTransform>();
        accentRT.sizeDelta = new Vector2(560, 4);
        accentRT.anchorMin = new Vector2(0.5f, 1f);
        accentRT.anchorMax = new Vector2(0.5f, 1f);
        accentRT.anchoredPosition = new Vector2(0, -2);

        // Prompt label
        if (prompt != null)
        {
            TextMeshProUGUI promptLabel = new GameObject("Prompt").AddComponent<TextMeshProUGUI>();
            promptLabel.transform.SetParent(feedbackPanel.transform, false);
            promptLabel.text = prompt;
            promptLabel.color = Color.white;
            promptLabel.fontSize = 18;
            promptLabel.fontStyle = FontStyles.Bold;
            promptLabel.alignment = TextAlignmentOptions.TopLeft;
            RectTransform promptRT = promptLabel.GetComponent<RectTransform>();
            promptRT.sizeDelta = new Vector2(500, 50);
            promptRT.anchoredPosition = new Vector2(0, 110);
        }

        // Input field
        TMP_InputField inputField = new GameObject("InputField").AddComponent<TMP_InputField>();
        inputField.transform.SetParent(feedbackPanel.transform, false);
        Image inputImage = inputField.gameObject.AddComponent<Image>();
        inputImage.color = new Color(0.2f, 0.2f, 0.2f, 1f);
        RectTransform inputRT = inputField.GetComponent<RectTransform>();
        inputRT.sizeDelta = new Vector2(500, 120);
        inputRT.anchoredPosition = new Vector2(0, 20);
        inputField.lineType = TMP_InputField.LineType.MultiLineSubmit;

        RectTransform textArea = new GameObject("Text Area").AddComponent<RectTransform>();
        textArea.transform.SetParent(inputField.transform, false);
        textArea.sizeDelta = new Vector2(480, 110);
        textArea.gameObject.AddComponent<RectMask2D>();

        TextMeshProUGUI placeholder = new GameObject("Placeholder").AddComponent<TextMeshProUGUI>();
        placeholder.transform.SetParent(textArea.transform, false);
        placeholder.text = "Type your response here...";
        placeholder.color = new Color(0.5f, 0.5f, 0.5f, 1f);
        placeholder.fontSize = 14;
        placeholder.GetComponent<RectTransform>().sizeDelta = new Vector2(480, 110);

        TextMeshProUGUI text = new GameObject("Text").AddComponent<TextMeshProUGUI>();
        text.transform.SetParent(textArea.transform, false);
        text.color = Color.white;
        text.fontSize = 14;
        text.GetComponent<RectTransform>().sizeDelta = new Vector2(480, 110);

        inputField.textViewport = textArea.GetComponent<RectTransform>();
        inputField.textComponent = text;
        inputField.placeholder = placeholder;

        StartCoroutine(FocusInputField(inputField));

        // Submit button
        submitButton = new GameObject("SubmitButton").AddComponent<Button>();
        submitButton.transform.SetParent(feedbackPanel.transform, false);
        Image buttonImage = submitButton.gameObject.AddComponent<Image>();
        buttonImage.color = accent;
        RectTransform buttonRT = submitButton.GetComponent<RectTransform>();
        buttonRT.sizeDelta = new Vector2(500, 40);
        buttonRT.anchoredPosition = new Vector2(0, -110);

        TextMeshProUGUI buttonText = new GameObject("ButtonText").AddComponent<TextMeshProUGUI>();
        buttonText.transform.SetParent(submitButton.transform, false);
        buttonText.text = "SUBMIT";
        buttonText.color = Color.white;
        buttonText.fontSize = 16;
        buttonText.fontStyle = FontStyles.Bold;
        buttonText.alignment = TextAlignmentOptions.Center;
        buttonText.GetComponent<RectTransform>().sizeDelta = new Vector2(500, 40);

        ColorBlock colors = submitButton.colors;
        colors.highlightedColor = new Color(accent.r + 0.1f, accent.g + 0.1f, accent.b + 0.1f, 1f);
        colors.pressedColor = new Color(accent.r - 0.2f, accent.g - 0.2f, accent.b - 0.2f, 1f);
        submitButton.colors = colors;

        submitButton.onClick.AddListener(() => { SubmitNote(inputField, prompt); });
    }
    private GameObject feedbackPanel;
    private Button submitButton;
    private bool showingFeedbackNoteWindow;
    float timeScale;
    bool cursorInitiallyVisible;
    CursorLockMode cursorMode;
    private IEnumerator FocusInputField(TMP_InputField inputField)
    {
        timeScale = Time.timeScale;
        Time.timeScale = 0;
        cursorInitiallyVisible = Cursor.visible;
        cursorMode = Cursor.lockState;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        yield return null;
        inputField.ActivateInputField();
        inputField.Select();
    }

    public void SubmitNote(TMP_InputField inputField, string prompt)
    {
        Dictionary<string, object> dict = new Dictionary<string, object> { { "note", inputField.text } };
        if (prompt != null)
        {
            dict["prompt"] = prompt;
        }

        WriteData(LytixJSONTypes.FeedbackNote, dict);
        Destroy(feedbackPanel);
        showingFeedbackNoteWindow = false;
        Cursor.visible = cursorInitiallyVisible;
        Cursor.lockState = cursorMode;
        Time.timeScale = timeScale;
    }


}