using Robust.Shared.Serialization;

namespace Content.Shared._PS.Terradrop;

[Serializable] [NetSerializable]
public sealed class StartTerradropMessage : BoundUserInterfaceMessage
{
    public string TerradropMapId;

    public StartTerradropMessage(string terradropMapId)
    {
        TerradropMapId = terradropMapId;
    }
};
