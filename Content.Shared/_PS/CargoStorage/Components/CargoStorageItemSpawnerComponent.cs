using Content.Shared._PS.CargoStorage.Data;
using Robust.Shared.GameStates;

namespace Content.Shared._PS.CargoStorage.Components;

[RegisterComponent]
[NetworkedComponent]
public sealed partial class CargoStorageItemSpawnerComponent : Component
{
    [NonSerialized]
    public List<CargoStorageData> ItemsToSpawn;

    [NonSerialized]
    public bool NoCrate = false;
}
