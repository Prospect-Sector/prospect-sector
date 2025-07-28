using Robust.Shared.Serialization;

namespace Content.Shared._PS.CargoStorage.Systems;

public abstract class SharedCargoStorageSystem : EntitySystem
{
    public const int CartMaxCapacity = 30;
};

[NetSerializable, Serializable]
public enum CargoStorageConsoleUiKey : byte
{
    Default,
}
