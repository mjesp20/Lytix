using System.Collections.Generic;
using UnityEngine;
using LytixInternal;
// Lytix.cs — global namespace, your public API surface
public static class Lytix
{
    // Public API for sending events, accepts a dictionary of string keys and arbitrary values
    public static void Event(Dictionary<string, object> args) {
        LytixPlayerTracker.Instance.Event(args);
    }
    // Convenience overload for simple string events
    public static void Event(string label)
    {
        LytixPlayerTracker.Instance.Event(new Dictionary<string, object>
        {
            { "event", label }
        });
    }
}

