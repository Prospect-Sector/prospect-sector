using System.Linq;
using Content.Shared._PS.CargoStorage.Data;
using Content.Shared._PS.CargoStorage.Systems;
using Robust.Shared.Serialization;

namespace Content.Shared._PS.CargoStorage.BUI;

[NetSerializable, Serializable]
public sealed class CargoStorageConsoleInterfaceState : BoundUserInterfaceState
{
    /// <summary>
    /// Data to display
    /// </summary>
    public List<CargoStorageData> CargoStorageDataList;

    /// <summary>
    /// The currently stored cart data
    /// </summary>
    public List<CargoStorageData> CartDataList;

    public bool CanRequestBoxedCart;

    /// <summary>
    /// are the buttons enabled
    /// </summary>
    public bool Enabled;

    /// <summary>
    /// The total amount of entities in the cart.
    /// </summary>
    public int CartEntities;

    public CargoStorageConsoleInterfaceState(List<CargoStorageData> cargoStorageDataList,
        List<CargoStorageData> cartDataList,
        bool enabled,
        int cartEntities)
    {
        CargoStorageDataList = cargoStorageDataList;
        CartDataList = cartDataList;
        CanRequestBoxedCart = cargoStorageDataList.Any(data =>
            data.Prototype.Id == SharedCargoStorageSystem.CartBoxProtoIdString &&
            data.Quantity >= SharedCargoStorageSystem.CartBoxAmountRequired);
        Enabled = enabled;
        CartEntities = cartEntities;
    }
}
