using Content.Shared.Stacks;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._PS.CargoStorage.Data;

[Virtual, NetSerializable, Serializable]
public sealed class CargoStorageData
{
    [ViewVariables]
    public EntProtoId Prototype { get; set; }

    [ViewVariables]
    public ProtoId<StackPrototype>? StackPrototype { get; set; }

    [ViewVariables]
    public int Quantity { get; set; }

    public CargoStorageData(EntProtoId prototype, ProtoId<StackPrototype>? stackPrototype, int quantity)
    {
        Prototype = prototype;
        StackPrototype = stackPrototype;
        Quantity = quantity;
    }
}


