using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace LytixInternal
{
    [AddComponentMenu("")]
    public class LytixTracker : MonoBehaviour
    {
        private static LytixTracker _instance;
        public static LytixTracker Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject trackerObject = new GameObject("LytixTracker");
                    _instance = trackerObject.AddComponent<LytixTracker>();
                    DontDestroyOnLoad(trackerObject);
                }
                return _instance;
            }
        }

        public Dictionary<string, object> TrackableStore { get; private set; } = new();

        private static bool IsSupportedType(Type type)
        {
            return true; //test on all types, see if we have to limit later
            return type == typeof(int) ||
                   type == typeof(float) ||
                   type == typeof(bool) ||
                   type == typeof(string);
        }

        // Internal cache of tracked members
        private class TrackableEntry
        {
            public object Target;
            public MemberInfo Member;
            public string Label;
        }

        private readonly List<TrackableEntry> _entries = new();


        //Initial caching of all trackables, do not call on update.
        public void CacheTrackables()
        {
            _entries.Clear();

            var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);

            foreach (var behaviour in behaviours)
            {
                var type = behaviour.GetType();

                // Fields
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    var attr = field.GetCustomAttribute<LytixTrackable>();
                    if (attr == null) continue;

                    if (!IsSupportedType(field.FieldType))
                    {
                        Debug.LogWarning($"[Lytix] Unsupported type on {type.Name}.{field.Name}");
                        continue;
                    }

                    if (!IsSupportedType(field.FieldType))
                        continue;

                    string label = attr.Label ?? $"{type.Name}.{field.Name}";

                    _entries.Add(new TrackableEntry
                    {
                        Target = behaviour,
                        Member = field,
                        Label = label
                    });
                }

                // Properties
                foreach (var prop in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    var attr = prop.GetCustomAttribute<LytixTrackable>();
                    if (attr == null || !prop.CanRead) continue;


                    if (!IsSupportedType(prop.PropertyType))
                    {
                        Debug.LogWarning($"[Lytix] Unsupported type on {type.Name}.{prop.Name}");
                        continue;
                    }

                    if (!IsSupportedType(prop.PropertyType))
                        continue;

                    string label = attr.Label ?? $"{type.Name}.{prop.Name}";

                    _entries.Add(new TrackableEntry
                    {
                        Target = behaviour,
                        Member = prop,
                        Label = label
                    });
                }
            }
        }

        public void ReadTrackableValues()
        {
            TrackableStore.Clear();

            foreach (var entry in _entries)
            {
                object value = null;

                switch (entry.Member)
                {
                    case FieldInfo field:
                        value = field.GetValue(entry.Target);
                        break;

                    case PropertyInfo prop:
                        value = prop.GetValue(entry.Target);
                        break;
                }

                TrackableStore[entry.Label] = value;
            }
        }
    }
}