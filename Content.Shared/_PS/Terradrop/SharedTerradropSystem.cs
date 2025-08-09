using Robust.Shared.Serialization;

namespace Content.Shared._PS.Terradrop;

public abstract class SharedTerradropSystem : EntitySystem
{
    protected const int MissionLimit = 3;
}

[NetSerializable] [Serializable]
public enum TerradropConsoleUiKey : byte
{
    Default,
}
