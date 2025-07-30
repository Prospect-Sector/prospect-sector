using Robust.Shared.Serialization;

namespace Content.Shared._PS.Expadition;

public abstract class SharedExpaditionSystem: EntitySystem
{
    protected const int MissionLimit = 3;
}

[NetSerializable, Serializable]
public enum ExpaditionConsoleUiKey : byte
{
    Default,
}
