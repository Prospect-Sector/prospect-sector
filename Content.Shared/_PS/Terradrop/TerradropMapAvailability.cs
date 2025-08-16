using Robust.Shared.Serialization;

namespace Content.Shared._PS.Terradrop;

[Serializable, NetSerializable]
public enum TerradropMapAvailability : byte
{
    Unexplored,
    InProgress,
    Explored,
    Unavailable
}
