using Content.Shared._PS.CargoStorage.Systems;

namespace Content.Server._PS.CargoStorage.Systems;

using Content.Server.Stack;
using Content.Shared.Station.Systems;
using Content.Shared.Popups;
using Content.Shared.Whitelist;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;

public sealed partial class CargoStorageSystem: SharedCargoStorageSystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private EntityWhitelistSystem _whitelistSystem = default!;
    [Dependency] private StackSystem _stackSystem = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private StationSystem _station = default!;

    public override void Initialize()
    {
        base.Initialize();

        InitializeConsole();
        InitializeCrateMachine();
    }
}
