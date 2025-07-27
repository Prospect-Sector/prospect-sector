using Content.Server._PS.CargoStorage.Systems;
using Content.Shared._PS.CargoStorage.Data;

namespace Content.Server._PS.CargoStorage.Components;

using Content.Shared.Whitelist;

/// <summary>
/// Component that is put on the console's grid that will hold all things that are sold at cargo, for that grid.
/// </summary>
[RegisterComponent]
[Access(typeof(CargoStorageSystem))]
public sealed partial class CargoMarketDataComponent : Component
{
    [DataField]
    public List<CargoStorageData> MarketDataList = [];

    /// <summary>
    /// Sold items must match this whitelist to enter into this data set.
    /// </summary>
    [DataField]
    public EntityWhitelist? Whitelist;

    /// <summary>
    /// Sold items not must match this blacklist to enter into this data set.
    /// </summary>
    [DataField]
    public EntityWhitelist? Blacklist;

    /// <summary>
    /// Particular items that may override the blacklist.
    /// </summary>
    [DataField]
    public EntityWhitelist? WhitelistOverride;
}

