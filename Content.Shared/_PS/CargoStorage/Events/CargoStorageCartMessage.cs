using Robust.Shared.Serialization;

namespace Content.Shared._PS.CargoStorage.Events;

/// <summary>
/// Message to move an item between cart and market
/// </summary>
[Serializable, NetSerializable]
public sealed class CargoStorageCartMessage : BoundUserInterfaceMessage
{
    public int Amount;
    public string? ItemPrototype;
    public bool RemoveFromCart;

    public CargoStorageCartMessage(int amount, string itemPrototype, bool removeFromCart = false)
    {
        Amount = amount;
        ItemPrototype = itemPrototype;
        RemoveFromCart = removeFromCart;
    }
}
