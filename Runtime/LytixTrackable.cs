using System;

/// <summary>
/// Mark any property with [LytixTrackable] to have it automatically
/// collected and monitored by the LytixTracker global store.
///
/// Usage:
///   [LytixTrackable]
///   public int Coins
///
///   [LytixTrackable("Player Health")]   // optional custom display name
///   public float Health
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public class LytixTrackable : Attribute
{
    /// <summary>
    /// Optional human-readable label shown in the tracker store.
    /// Falls back to "TypeName.PropertyName" when null.
    /// </summary>
    public string Label { get; }

    /// <summary>Attribute with an auto-generated label.</summary>
    public LytixTrackable() { }

    /// <summary>Attribute with a custom display label.</summary>
    public LytixTrackable(string label)
    {
        Label = label;
    }

    // special case for player position
    public sealed class PlayerPosition : LytixTrackable
    {
        public PlayerPosition() : base("playerPosition") { }
    }
}
