using Robust.Shared.Serialization;

namespace Content.Shared._PS.Terradrop;

[Serializable, NetSerializable]
public sealed class ReconnectPortalMessage : BoundUserInterfaceMessage
{
    public string TerradropMapId;
    public int InstanceIndex;

    public ReconnectPortalMessage(string terradropMapId, int instanceIndex)
    {
        TerradropMapId = terradropMapId;
        InstanceIndex = instanceIndex;
    }
}
