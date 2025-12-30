using System.Linq;
using Content.Server._PS.CargoStorage.Components;
using Content.Server._PS.CargoStorage.Extensions;
using Content.Server.Storage.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Materials;
using Content.Shared.Power;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Content.Server.Cargo.Systems;
using Content.Shared._PS.CargoStorage.BUI;
using Content.Shared._PS.CargoStorage.Data;
using Content.Shared._PS.CargoStorage.Events;
using Content.Shared._PS.CargoStorage.Systems;
using Content.Shared.Storage.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._PS.CargoStorage.Systems;

public sealed partial class CargoStorageSystem
{
    [Dependency] private readonly IComponentFactory _componentFactory = default!;
    [Dependency] private readonly SharedMaterialStorageSystem _sharedMaterialStorageSystem = default!;

    private void InitializeConsole()
    {
        SubscribeLocalEvent<EntityStoredEvent>(OnEntityStoredEvent);
        SubscribeLocalEvent<CargoStorageConsoleComponent, BoundUIOpenedEvent>(OnConsoleUiOpened);
        SubscribeLocalEvent<CargoStorageConsoleComponent, CargoStorageCartMessage>(OnCartMessage);
        SubscribeLocalEvent<CargoStorageConsoleComponent, PowerChangedEvent>(OnPowerChanged);
    }

    private void OnPowerChanged(EntityUid uid, CargoStorageConsoleComponent component, ref PowerChangedEvent args)
    {
        if (args.Powered)
            return;
        _ui.CloseUi(uid, CargoStorageConsoleUiKey.Default);
    }

    /// <summary>
    /// This event signifies that something has been sold at a cargo pallet.
    /// </summary>
    /// <param name="entityStoredEvent">The details of the event</param>
    private void OnEntityStoredEvent(ref EntityStoredEvent entityStoredEvent)
    {
        var station = _station.GetOwningStation(entityStoredEvent.Station);
        if (station is null ||
            !_entityManager.TryGetComponent<CargoStorageDataComponent>(station, out var dataComponent))
        {
            return;
        }

        foreach (var sold in entityStoredEvent.Stored)
        {
            UpsertEntity(dataComponent, sold);
        }
    }

    /// <summary>
    /// Recursively updates/inserts an entity and everything it contains into the cargo market.
    /// </summary>
    /// <param name="storage">The cargo storage data set that will store these entities.</param>
    /// <param name="sold">The entity to add.</param>
    private void UpsertEntity(CargoStorageDataComponent storage, EntityUid sold)
    {
        // Recurse through other stored/contained entities first.
        if (_entityManager.TryGetComponent<MaterialStorageComponent>(sold, out var materialStorageComponent))
            UpsertMaterialStorage(storage, materialStorageComponent, sold);
        if (_entityManager.TryGetComponent<StorageComponent>(sold, out var storageComponent))
            UpsertStorage(storage, storageComponent);
        if (_entityManager.TryGetComponent<EntityStorageComponent>(sold, out var entityStorageComponent))
            UpsertEntityStorage(storage, entityStorageComponent);
        if (_entityManager.TryGetComponent<ItemSlotsComponent>(sold, out var itemSlotsComponent))
            UpsertItemSlots(storage, itemSlotsComponent);

        // Get our prototype for this entity and insert it.
        if (!_entityManager.TryGetComponent<MetaDataComponent>(sold, out var metaDataComponent))
            return;

        if (metaDataComponent.EntityPrototype == null)
            return;

        var count = 1;
        var entityPrototype = metaDataComponent.EntityPrototype;
        string? stackPrototypeId = null;

        // Get amount of items in the stack if it's a stackable item.
        // If it's a stackable item, get the singular item id instead.
        if (_entityManager.TryGetComponent<StackComponent>(sold, out var stackComponent))
        {
            count = stackComponent.Count;
            stackPrototypeId = stackComponent.StackTypeId;
            var singularId = _prototypeManager.Index<StackPrototype>(stackComponent.StackTypeId).Spawn.Id;
            _prototypeManager.TryIndex(singularId, out entityPrototype);
        }

        // If this is null, probably couldn't find the stack type id.
        if (entityPrototype == null)
            return;

        // Check whitelist/blacklist for particular prototype
        if (_whitelistSystem.IsWhitelistFail(storage.Whitelist, sold) ||
            _whitelistSystem.IsWhitelistPass(storage.Blacklist, sold) &&
            _whitelistSystem.IsWhitelistFailOrNull(storage.WhitelistOverride, sold))
        {
            return;
        }

        // Increase the count in the MarketData for this entity
        // Assuming the quantity to increase is 1 for each sold entity
        storage.CargoStorageDataList.Upsert(entityPrototype.ID, count, stackPrototypeId);
    }

    /// <summary>
    /// Recursively updates or inserts cargo storage data for entities contained within an EntityStorageComponent.
    /// </summary>
    /// <param name="storageDataComponent">The MarketDataComponent to update.</param>
    /// <param name="entityStorageComponent">The EntityStorageComponent containing entities to process.</param>
    private void UpsertEntityStorage(CargoStorageDataComponent storageDataComponent,
        EntityStorageComponent entityStorageComponent)
    {
        foreach (var entityUid in entityStorageComponent.Contents.ContainedEntities)
        {
            UpsertEntity(storageDataComponent, entityUid);
        }
    }

    /// <summary>
    /// Recursively updates or inserts cargo storage data for entities contained within an ItemSlotsComponent.
    /// </summary>
    /// <param name="storageDataComponent">The MarketDataComponent to update.</param>
    /// <param name="itemSlotsComponent">The ItemSlotsComponent containing item slots to process.</param>
    private void UpsertItemSlots(CargoStorageDataComponent storageDataComponent, ItemSlotsComponent itemSlotsComponent)
    {
        foreach (var slot in itemSlotsComponent.Slots.Values)
        {
            if (slot.Item is not { Valid: true } entityUid)
                continue;

            UpsertEntity(storageDataComponent, entityUid);
        }
    }

    /// <summary>
    /// Recursively checks the contents of the storage.
    /// </summary>
    /// <param name="storageDataComponent"></param>
    /// <param name="storageComponent"></param>
    private void UpsertStorage(CargoStorageDataComponent storageDataComponent, StorageComponent storageComponent)
    {
        foreach (var entityUid in storageComponent.Container.ContainedEntities.ToArray())
        {
            UpsertEntity(storageDataComponent, entityUid);
        }
    }

    /// <summary>
    /// Inserts cargo storage data for all materials contained within a MaterialStorageComponent.
    /// </summary>
    /// <param name="storageDataComponent">The MarketDataComponent to update.</param>
    /// <param name="materialStorageComponent">The MaterialStorageComponent containing materials to process.</param>
    /// <param name="sold">The entity that was sold, which contains the materials.</param>
    private void UpsertMaterialStorage(CargoStorageDataComponent storageDataComponent,
        MaterialStorageComponent materialStorageComponent,
        EntityUid sold)
    {
        foreach (var (materialProto, amount) in materialStorageComponent.Storage)
        {
            if (!_prototypeManager.TryIndex(materialProto, out var material))
            {
                Log.Error("Failed to index material prototype " + materialProto);
                continue;
            }

            if (amount <= 0
                || material.StackEntity == null
                || !_prototypeManager.TryIndex<EntityPrototype>(material.StackEntity, out var entProto)
                || !entProto.TryGetComponent<PhysicalCompositionComponent>(out var composition, _componentFactory)
                || !entProto.TryGetComponent<StackComponent>(out var stack, _componentFactory))
            {
                continue;
            }

            var materialPerStack = composition.MaterialComposition[material.ID];
            var amountToSpawn = amount / materialPerStack;

            if (amountToSpawn <= 0)
                continue;

            var overflowMaterial = amount - amountToSpawn * materialPerStack;
            _sharedMaterialStorageSystem.TrySetMaterialAmount(sold,
                materialProto,
                overflowMaterial,
                materialStorageComponent);

            // Increase the count in the MarketData for this material
            storageDataComponent.CargoStorageDataList.Upsert(entProto.ID, amountToSpawn, stack.StackTypeId);
        }
    }

    /// <summary>
    /// Calculates the total number of entities in the cargo storage data list, taking into account the maximum stack count for stackable items.
    /// </summary>
    /// <param name="marketDataList">The list of cargo storage data to calculate the total entity count from.</param>
    /// <returns>The total number of entities in the cargo storage data list.</returns>
    public int CalculateEntityAmount(List<CargoStorageData> marketDataList)
    {
        var count = 0;

        foreach (var data in marketDataList)
        {
            if (data.StackPrototype != null && _prototypeManager.TryIndex(data.StackPrototype, out var stackPrototype))
            {
                var maxStackCount = stackPrototype.MaxCount;
                if (maxStackCount != null)
                {
                    count += (int)Math.Ceiling((double)data.Quantity /
                                               int.Max(1, maxStackCount.Value)); // Ensure denominator is positive
                }
                else
                    count += 1;
            }
            else
            {
                count += 1;
            }
        }

        return count;
    }

    /// <summary>
    /// Calculates the amount of items that can fit within an entity's worth of space for a given item type.
    /// </summary>
    /// <param name="data">The item type to calculate.</param>
    /// <returns>The number of items that can fit within an entity's worth of space. Null if infinite.</returns>
    public int? GetAmountPerEntitySpace(CargoStorageData data)
    {
        if (data.StackPrototype != null && _prototypeManager.TryIndex(data.StackPrototype, out var stackPrototype))
        {
            var maxStackCount = stackPrototype.MaxCount;
            if (maxStackCount != null)
                return int.Max(1, maxStackCount.Value); // Ensure amount is positive.
            else
                return null; // Infinity.
        }
        else
        {
            return 1;
        }
    }

    /// <summary>
    /// Occurs whenever something is added to the cart.
    /// If args.Amount is too high it will automatically be clamped to the maximum amount possible.
    /// This also prevents de-sync if there are two different consoles.
    /// </summary>
    /// <param name="consoleUid">The uuid of the console where it was added.</param>
    /// <param name="consoleComponent">The console component</param>
    /// <param name="args">The arguments for the cart event</param>
    private void OnCartMessage(
        EntityUid consoleUid,
        CargoStorageConsoleComponent consoleComponent,
        ref CargoStorageCartMessage args
    )
    {
        if (args.Actor is not { Valid: true })
            return;

        // Try to get the EntityPrototype that matches marketData.Prototype
        if (args.ItemPrototype == null ||
            !_prototypeManager.TryIndex<EntityPrototype>(args.ItemPrototype, out var prototype))
            return; // Skip this iteration if the prototype was not found

        // No data set for cargo storage data, can't update cart, no data.
        var stationUid = _station.GetOwningStation(consoleUid);
        if (!TryComp<CargoStorageDataComponent>(stationUid, out var market))
            return;

        var cargoStorageData = market.CargoStorageDataList;
        if (args.RemoveFromCart)
        {
            consoleComponent.CartDataList.Move(cargoStorageData, prototype.ID);
        }
        else
        {
            var maxQuantityToWithdraw = cargoStorageData.GetMaxQuantityToWithdraw(prototype);
            var toWithdraw = Math.Max(args.Amount, 0);
            toWithdraw = Math.Min(toWithdraw, maxQuantityToWithdraw);

            var existing = FindCargoStorageDataByPrototype(cargoStorageData, args.ItemPrototype!);
            if (existing == null)
                return;

            // Calculate maximum we can fit.
            var entityAmount = CalculateEntityAmount(consoleComponent.CartDataList);
            var amountPerEntity = GetAmountPerEntitySpace(existing);

            // Finite stack size, limit our withdrawal.
            if (amountPerEntity != null)
            {
                var amountLeft = (CartMaxCapacity - entityAmount) * amountPerEntity.Value;

                var existingCart = FindCargoStorageDataByPrototype(consoleComponent.CartDataList, args.ItemPrototype!);
                if (existingCart != null)
                {
                    // Find if there's a partially filled entity in the cart.
                    var quantityMod = existingCart.Quantity % amountPerEntity.Value;
                    if (quantityMod != 0)
                        amountLeft += amountPerEntity.Value - quantityMod;
                }

                amountLeft = int.Max(0, amountLeft); // If we're over the limit as-is, don't move anything.
                toWithdraw = int.Min(toWithdraw, amountLeft);
            }

            cargoStorageData.Upsert(existing.Prototype, -toWithdraw, existing.StackPrototype);
            consoleComponent.CartDataList.Upsert(existing.Prototype, toWithdraw, existing.StackPrototype);
        }

        // FIXME: this should update the state of other console UI in the same station.
        RefreshState(
            consoleUid,
            consoleComponent
        );
    }

    /// <summary>
    /// Finds a MarketData item in the list that has the same prototype.
    /// </summary>
    /// <param name="marketDataList">The list of cargo storage data to search in.</param>
    /// <param name="prototypeId">The prototype ID to search for.</param>
    /// <returns>The MarketData item with the matching prototype, or null if not found.</returns>
    public CargoStorageData? FindCargoStorageDataByPrototype(List<CargoStorageData> marketDataList, string prototypeId)
    {
        foreach (var marketData in marketDataList)
        {
            if (marketData.Prototype == prototypeId)
                return marketData;
        }

        return null;
    }

    private void OnConsoleUiOpened(EntityUid uid, CargoStorageConsoleComponent component, BoundUIOpenedEvent args)
    {
        if (args.Actor is not { Valid: true })
            return;
        RefreshState(uid, component);
    }

    private void RefreshState(
        EntityUid consoleUid,
        CargoStorageConsoleComponent? component
    )
    {
        if (!Resolve(consoleUid, ref component))
            return;

        // Ensures that when this console is no longer attached to a grid and is powered somehow, it won't work.
        if (Transform(consoleUid).GridUid == null)
            return;

        // Get the cargo storage data for this grid.
        var cartData = component.CartDataList;
        var marketData = new List<CargoStorageData>();

        // Get station and the cargo storage data attached to it.
        var consoleStationUid = _station.GetOwningStation(consoleUid);
        if (TryComp<CargoStorageDataComponent>(consoleStationUid, out var market))
        {
            marketData = market.CargoStorageDataList;
        }

        var newState = new CargoStorageConsoleInterfaceState(
            marketData,
            cartData,
            true, // TODO add enable/disable functionality
            CalculateEntityAmount(cartData)
        );
        _ui.SetUiState(consoleUid, CargoStorageConsoleUiKey.Default, newState);
    }
}
