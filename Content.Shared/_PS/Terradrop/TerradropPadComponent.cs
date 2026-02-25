using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Shared._PS.Terradrop;

[RegisterComponent]
[NetworkedComponent, AutoGenerateComponentState(true, true), Access(typeof(SharedTerradropSystem), Other = AccessPermissions.ReadWriteExecute)]
public sealed partial class TerradropPadComponent : Component
{
    [DataField]
    public SoundSpecifier ClearPortalSound = new SoundPathSpecifier("/Audio/Machines/button.ogg");

    [DataField] public SoundSpecifier NewPortalSound =
        new SoundPathSpecifier("/Audio/Machines/high_tech_confirm.ogg");

    [ViewVariables, DataField("Portal")]
    [AutoNetworkedField]
    public EntityUid? Portal;

    [DataField("PortalPrototype", customTypeSerializer: typeof(PrototypeIdSerializer<EntityPrototype>))]
    public string PortalPrototype = "PortalBlue";

    /// <summary>
    /// The destination map ID for the terradrop pad.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), Access(typeof(SharedTerradropSystem), Other = AccessPermissions.ReadExecute)]
    public MapId TeleportMapId { get; set; }
}
