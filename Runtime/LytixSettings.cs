using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using UnityEngine;
using LytixInternal;

public static class LytixSettings
{
    private const string FilePath = "ProjectSettings/LytixSettings.json";

    private static Dictionary<string, object> _data;
    private static bool _loaded;

    // -------------------------
    // Public API
    // -------------------------

    public static void Set(string key, object value)
    {
        EnsureLoaded();
        _data[key] = value;
        Save();
    }

    public static T Get<T>(string key, T defaultValue = default)
    {
        EnsureLoaded();
        if (!_data.TryGetValue(key, out var value) || value == null)
            return defaultValue;
    
        try
        {
            if (value is Newtonsoft.Json.Linq.JToken token)
                return token.ToObject<T>();

            return (T)System.Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return defaultValue;
        }
    }

    public static bool HasKey(string key)
    {
        EnsureLoaded();
        return _data.ContainsKey(key);
    }

    public static void Remove(string key)
    {
        EnsureLoaded();
        if (_data.Remove(key))
            Save();
    }

    // -------------------------
    // Internal load/save
    // -------------------------

    private static void EnsureLoaded()
    {
        if (_loaded) return;

        if (File.Exists(FilePath))
        {
            var json = File.ReadAllText(FilePath);
            _data = JsonConvert.DeserializeObject<Dictionary<string, object>>(json)
                    ?? new Dictionary<string, object>();
        }
        else
        {
            _data = new Dictionary<string, object>();
        }

        _loaded = true;
    }

    private static void Save()
    {
        var json = JsonConvert.SerializeObject(_data);
        File.WriteAllText(FilePath, json);
    }
}