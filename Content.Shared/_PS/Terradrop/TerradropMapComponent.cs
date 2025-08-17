namespace Content.Shared._PS.Terradrop;

[RegisterComponent]
public sealed partial class TerradropMapComponent : Component
{
    public int ThreatLevel = 1;

    [NonSerialized]
    public EntityUid? StationUid = null;

    [NonSerialized]
    public TerradropMapPrototype? MapPrototype = null;
}
