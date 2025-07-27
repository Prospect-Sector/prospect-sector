using Content.Server._PS.CargoStorage.Systems;
using Content.Shared._PS.CargoStorage.Data;
using Robust.Shared.Audio;

namespace Content.Server._PS.CargoStorage.Components;

/// <summary>
/// Component that belongs to the market computer
/// </summary>
[RegisterComponent]
[Access(typeof(CargoStorageSystem))]
public sealed partial class CargoStorageConsoleComponent : Component
{
    [DataField]
    public int MaxCrateMachineDistance = 8;

    public List<CargoStorageData> CartDataList = [];

    [DataField]
    public SoundSpecifier ErrorSound = new SoundPathSpecifier("/Audio/Effects/Cargo/buzz_sigh.ogg");

    [DataField]
    public SoundSpecifier SuccessSound = new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg");
}
