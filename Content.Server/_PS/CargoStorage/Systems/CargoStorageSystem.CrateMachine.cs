using Content.Server._PS.CargoStorage.Components;
using Content.Server._PS.CrateMachine;
using Content.Shared._PS.CargoStorage.Components;
using Content.Shared._PS.CargoStorage.Data;
using Content.Shared._PS.CargoStorage.Events;
using Content.Shared._PS.CrateMachine.Components;
using Robust.Shared.Audio;
using Robust.Shared.Player;

namespace Content.Server._PS.CargoStorage.Systems;

public sealed partial class CargoStorageSystem
{
    [Dependency] private readonly CrateMachineSystem _crateMachine = default!;

    private void InitializeCrateMachine()
    {
        SubscribeLocalEvent<CargoStorageConsoleComponent, CargoStoragePurchaseMessage>(OnMarketConsolePurchaseCrateMessage);
        SubscribeLocalEvent<CrateMachineComponent, CrateMachineOpenedEvent>(OnCrateMachineOpened);
    }

    private void OnMarketConsolePurchaseCrateMessage(EntityUid consoleUid,
        CargoStorageConsoleComponent component,
        ref CargoStoragePurchaseMessage args)
    {
        if (!_crateMachine.FindNearestUnoccupied(consoleUid, component.MaxCrateMachineDistance, out var machineUid) || !_entityManager.TryGetComponent<CrateMachineComponent> (machineUid, out var comp))
        {
            _popup.PopupEntity(Loc.GetString("market-no-crate-machine-available"), consoleUid, Filter.PvsExcept(consoleUid), true);
            _audio.PlayPredicted(component.ErrorSound, consoleUid, null, AudioParams.Default.WithMaxDistance(5f));

            return;
        }
        OnPurchaseCrateMessage(machineUid.Value, consoleUid, comp, component, args);
    }

    private void OnPurchaseCrateMessage(EntityUid crateMachineUid,
        EntityUid consoleUid,
        CrateMachineComponent component,
        CargoStorageConsoleComponent consoleComponent,
        CargoStoragePurchaseMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        TrySpawnCrate(crateMachineUid, player, consoleUid, component, consoleComponent);
    }

    private void TrySpawnCrate(EntityUid crateMachineUid,
        EntityUid player,
        EntityUid consoleUid,
        CrateMachineComponent component,
        CargoStorageConsoleComponent consoleComponent)
    {
        if (!TryComp<CargoStorageItemSpawnerComponent>(crateMachineUid, out var itemSpawner))
            return;

        _audio.PlayPredicted(consoleComponent.SuccessSound, consoleUid, null, AudioParams.Default.WithMaxDistance(5f));

        itemSpawner.ItemsToSpawn = consoleComponent.CartDataList;
        consoleComponent.CartDataList = [];
        _crateMachine.OpenFor(crateMachineUid, component);
    }

    private void SpawnCrateItems(List<CargoStorageData> spawnList, EntityUid targetCrate)
    {
        var coordinates = Transform(targetCrate).Coordinates;
        foreach (var data in spawnList)
        {
            if (data.StackPrototype != null && _prototypeManager.TryIndex(data.StackPrototype, out var stackPrototype))
            {
                var entityList = _stackSystem.SpawnMultiple(stackPrototype.Spawn, data.Quantity, coordinates);
                foreach (var entity in entityList)
                {
                    _crateMachine.InsertIntoCrate(entity, targetCrate);
                }
            }
            else
            {
                for (var i = 0; i < data.Quantity; i++)
                {
                    var spawn = Spawn(data.Prototype, coordinates);
                    _crateMachine.InsertIntoCrate(spawn, targetCrate);
                }
            }
        }
    }

    private void OnCrateMachineOpened(EntityUid uid, CrateMachineComponent component, CrateMachineOpenedEvent args)
    {
        if (!TryComp<CargoStorageItemSpawnerComponent>(uid, out var itemSpawner))
            return;

        var targetCrate = _crateMachine.SpawnCrate(uid, component);
        SpawnCrateItems(itemSpawner.ItemsToSpawn, targetCrate);
        itemSpawner.ItemsToSpawn = [];
    }
}
