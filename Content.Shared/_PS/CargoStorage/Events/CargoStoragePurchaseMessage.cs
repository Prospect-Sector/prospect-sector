using Robust.Shared.Serialization;

namespace Content.Shared._PS.CargoStorage.Events;

/// <summary>
///     When the player purchases an item from the market, this message is sent.
/// </summary>
[Serializable, NetSerializable]
public sealed class CargoStoragePurchaseMessage : BoundUserInterfaceMessage
{
};
