using LytixInternal;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using Unity.VisualScripting;
using UnityEngine;

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

        // ----------------------------
        // SAMPLE DATA (X per second)
        // ----------------------------
        while (sampleTimer >= sampleInterval)
        {
            sampleTimer -= sampleInterval;

            WriteData(LytixJSONTypes.Movement, null);
        }

        // ----------------------------
        // FLUSH BATCH (Y seconds)
        // ----------------------------
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
                    entry["position"] = SanitizeValue(kvp.Value); // SanitizeValue should return {x,y,z} dict — see below
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
            // Primitives — Newtonsoft handles these natively, pass through as-is
            int or float or double or bool or string or long => value,
            // Known Unity structs — convert to named object to preserve x/y/z JSON structure
            Vector2 v => new { x = v.x, y = v.y },
            Vector3 v => new { x = v.x, y = v.y, z = v.z },
            Vector4 v => new { x = v.x, y = v.y, z = v.z, w = v.w },
            Quaternion q => new { x = q.x, y = q.y, z = q.z, w = q.w },
            // Fallback — stringify rather than let Newtonsoft choke on it
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
    public void Event(Dictionary<string,object> args)
    {
        WriteData(LytixJSONTypes.Event, args);
    }
}