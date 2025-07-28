using Robust.Shared.Serialization;

namespace Content.Shared._PS.CargoStorage.Systems;

public abstract class SharedCargoStorageSystem : EntitySystem
{
    public const int CartMaxCapacity = 30;
    public const string CartBoxProtoIdString = "SheetSteel1";
    public const int CartBoxAmountRequired = 5;
};

[NetSerializable, Serializable]
public enum CargoStorageConsoleUiKey : byte
{
    Default,
}
