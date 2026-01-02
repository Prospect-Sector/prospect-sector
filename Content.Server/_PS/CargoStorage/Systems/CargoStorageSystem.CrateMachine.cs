using System.Linq;
using Content.Server._PS.CargoStorage.Components;
using Content.Server._PS.CrateMachine;
using Content.Shared._PS.CargoStorage.Components;
using Content.Shared._PS.CargoStorage.Data;
using Content.Shared._PS.CargoStorage.Events;
using Content.Shared._PS.CargoStorage.Systems;
using Content.Shared._PS.CrateMachine.Components;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server._PS.CargoStorage.Systems;

public sealed partial class CargoStorageSystem
{
    [Dependency] private readonly CrateMachineSystem _crateMachine = default!;
    [Dependency] private readonly ILogManager _logMan = default!;
    private ISawmill _sawmill = default!;

    private void InitializeCrateMachine()
    {
        _sawmill = Logger.GetSawmill("cargo-storage");
        SubscribeLocalEvent<CargoStorageConsoleComponent, CargoStoragePurchaseMessage>(OnCargoStorageConsolePurchaseCrateMessage);
        SubscribeLocalEvent<CrateMachineComponent, CrateMachineOpenedEvent>(OnCrateMachineOpened);
    }

    private void OnCargoStorageConsolePurchaseCrateMessage(EntityUid consoleUid,
        CargoStorageConsoleComponent component,
        ref CargoStoragePurchaseMessage args)
    {
        if (!_crateMachine.FindNearestUnoccupied(consoleUid, component.MaxCrateMachineDistance, out var machineUid) || !_entityManager.TryGetComponent<CrateMachineComponent> (machineUid, out var comp))
        {
            _popup.PopupEntity(Loc.GetString("cargo-storage-no-crate-machine-available"), consoleUid, Filter.PvsExcept(consoleUid), true);
            _audio.PlayPredicted(component.ErrorSound, consoleUid, null, AudioParams.Default.WithMaxDistance(5f));

            return;
        }
        OnPurchaseMessage(machineUid.Value, consoleUid, comp, component, args);
    }

    private void OnPurchaseMessage(EntityUid crateMachineUid,
        EntityUid consoleUid,
        CrateMachineComponent component,
        CargoStorageConsoleComponent consoleComponent,
        CargoStoragePurchaseMessage args)
    {
        if (args.Actor is not { Valid: true })
            return;

        if (!TryComp<CargoStorageItemSpawnerComponent>(crateMachineUid, out var itemSpawner))
            return;

        // Check if we can make a crate if one is requested.
        if (!args.NoCrate)
        {
            var stationUid = _station.GetOwningStation(consoleUid);
            if (!TryComp<CargoStorageDataComponent>(stationUid, out var market))
                return;

            var matchingData = FindCargoStorageDataByPrototype(market.CargoStorageDataList, CartBoxProtoIdString);
            if (matchingData != null && matchingData.Quantity >= CartBoxAmountRequired)
            {
                matchingData.Quantity -= CartBoxAmountRequired;
            }
            else
            {
                var message = Loc.GetString("cargo-storage-no-steel", ("number", CartBoxAmountRequired), ("name", CartBoxProtoIdString));
                _popup.PopupEntity(message, consoleUid, Filter.PvsExcept(consoleUid), true);
                _audio.PlayPredicted(consoleComponent.ErrorSound, consoleUid, null, AudioParams.Default.WithMaxDistance(5f));
                return;
            }
        }

        _audio.PlayPredicted(consoleComponent.SuccessSound, consoleUid, null, AudioParams.Default.WithMaxDistance(5f));

        itemSpawner.ItemsToSpawn = consoleComponent.CartDataList;
        itemSpawner.NoCrate = args.NoCrate;
        consoleComponent.CartDataList = [];

        _crateMachine.OpenFor(crateMachineUid, component, noCrate: args.NoCrate);
    }

    /// <summary>
    /// Spawn items either into a crate or at the specified coordinates.
    /// </summary>
    /// <param name="spawnList"></param>
    /// <param name="targetCrate"></param>
    /// <param name="targetCoordinates"></param>
    private void SpawnItems(
        List<CargoStorageData> spawnList,
        EntityUid? targetCrate,
        EntityCoordinates? targetCoordinates)
    {
        if (targetCrate == null && targetCoordinates == null)
        {
            _sawmill.Error("CargoStorageSystem: SpawnItems called with neither targetCrate nor targetCoordinates specified.");
            return;
        }
        var coordinates = targetCoordinates ?? Transform(targetCrate!.Value).Coordinates;
        foreach (var data in spawnList)
        {
            if (data.StackPrototype != null && _prototypeManager.TryIndex(data.StackPrototype, out var stackPrototype))
            {
                var entityList = _stackSystem.SpawnMultipleAtPosition(stackPrototype.Spawn, data.Quantity, coordinates);
                foreach (var entity in entityList)
                {
                    if (targetCrate != null)
                    {
                        _crateMachine.InsertIntoCrate(entity, targetCrate.Value);
                    }
                }
            }
            else
            {
                for (var i = 0; i < data.Quantity; i++)
                {
                    var spawn = Spawn(data.Prototype, coordinates);
                    if (targetCrate != null)
                    {
                        _crateMachine.InsertIntoCrate(spawn, targetCrate.Value);
                    }
                }
            }
        }
    }

    private void OnCrateMachineOpened(EntityUid uid, CrateMachineComponent component, CrateMachineOpenedEvent args)
    {
        if (!TryComp<CargoStorageItemSpawnerComponent>(uid, out var itemSpawner))
            return;

        if (itemSpawner.NoCrate)
        {
            SpawnItems(itemSpawner.ItemsToSpawn, null, Transform(args.EntityUid).Coordinates);
        }
        else
        {
            var targetCrate = _crateMachine.SpawnCrate(uid, component);
            SpawnItems(itemSpawner.ItemsToSpawn, targetCrate, null);
        }
        itemSpawner.ItemsToSpawn = [];
    }
}
