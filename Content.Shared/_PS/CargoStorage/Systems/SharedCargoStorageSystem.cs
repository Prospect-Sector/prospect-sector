using Robust.Shared.Serialization;

namespace Content.Shared._PS.CargoStorage.Systems;

public abstract class SharedCargoStorageSystem : EntitySystem
{
};

[NetSerializable, Serializable]
public enum CargoStorageConsoleUiKey : byte
{
    Default,
}
