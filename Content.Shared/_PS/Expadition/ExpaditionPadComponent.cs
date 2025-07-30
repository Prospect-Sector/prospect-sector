using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Shared._PS.Expadition;

[RegisterComponent]
[NetworkedComponent, AutoGenerateComponentState(true, fieldDeltas: true), Access(typeof(SharedExpaditionSystem))]
public sealed partial class ExpaditionPadComponent: Component
{
    [DataField("PortalPrototype", customTypeSerializer: typeof(PrototypeIdSerializer<EntityPrototype>))]
    public string PortalPrototype = "PortalBlue";

    [ViewVariables, DataField("Portal")]
    public EntityUid? Portal;

    [DataField] public SoundSpecifier NewPortalSound =
        new SoundPathSpecifier("/Audio/Machines/high_tech_confirm.ogg");

    [DataField]
    public SoundSpecifier ClearPortalSound = new SoundPathSpecifier("/Audio/Machines/button.ogg");

    /// <summary>
    /// The destination map ID for the expadition pad.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), Access(typeof(SharedExpaditionSystem), Other = AccessPermissions.ReadExecute)]
    public MapId TeleportMapId { get; set; }

    /// <summary>
    /// The time the pad was activated. This is set after the map is loaded.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoNetworkedField]
    public TimeSpan? ActivatedAt;

    /// <summary>
    /// The time it takes from ActivatedAt to the pad being active.
    /// </summary>
    [DataField]
    public TimeSpan ClearPortalDelay = TimeSpan.FromSeconds(30);
}

public class EntityUidSerializer
{
}
