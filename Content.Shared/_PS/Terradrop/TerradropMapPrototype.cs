using Content.Shared.Damage.Prototypes;
using Content.Shared.Procedural;
using Content.Shared.Salvage.Expeditions;
using Content.Shared.Salvage.Expeditions.Modifiers;
using Robust.Shared.Prototypes;

namespace Content.Shared._PS.Terradrop;

[Prototype]
public sealed class TerradropMapPrototype: IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Player-facing name.
    /// Supports locale strings.
    /// </summary>
    [DataField("name", required: true)]
    public string Name = string.Empty;

    /// <summary>
    /// An icon used to visually represent the map in UI.
    /// </summary>
    [DataField("icon", required: true)]
    public EntProtoId Icon;

    /// <summary>
    /// A list of <see cref="TerradropMapPrototype"/>s that need to be explored in order to unlock this map.
    /// </summary>
    [DataField]
    public List<ProtoId<TerradropMapPrototype>> MapPrerequisites = new();

    [DataField]
    public List<ProtoId<TerradropMapPrototype>> MapUnlocks = new();

    /// <summary>
    /// Position of this tech in console menu
    /// </summary>
    [DataField("position", required: true)]
    public Vector2i Position { get; private set; }

    /// <summary>
    /// The seed to use for generating the map.
    /// Leave null to use a random seed.
    /// Can optionally be used to ensure the same map is generated each time.
    /// </summary>
    [DataField("seed")]
    public int? Seed;

    /// <summary>
    /// The factions that can spawn on this map.
    /// Leave null to use random factions.
    /// </summary>
    [DataField("salvageFaction")]
    public ProtoId<SalvageFactionPrototype>? SalvageFaction;

    /// <summary>
    /// The difficulty of the salvage expedition on this map.
    /// </summary>
    [DataField("salvageDifficulty")]
    public ProtoId<SalvageDifficultyPrototype> SalvageDifficulty = "Moderate";

    /// <summary>
    /// The biome of the map.
    /// </summary>
    [DataField("biome")]
    public ProtoId<SalvageBiomeModPrototype> Biome = "Grasslands";

    [DataField("atmosphere")]
    public ProtoId<SalvageAirMod> Atmosphere = "Breathable";

    [DataField("temperature")]
    public ProtoId<SalvageTemperatureMod> Temperature = "RoomTemp";

    /// <summary>
    /// Must be a prototype with a EntityStorageComponent.
    /// </summary>
    [DataField("returnContainerProto")]
    public EntProtoId ReturnContainerProto = "BodyBag";

    [DataField("returnDamageType")]
    public ProtoId<DamageGroupPrototype> ReturnDamageType = "Brute";

    [DataField("returnDamageAmount")]
    public int ReturnDamageAmount = 200;

    [DataField("unlockedByDefault")]
    public bool UnlockedByDefault = false;

    /// <summary>
    /// The minimum level required to play this map.
    /// Players can select any level at or above this value.
    /// </summary>
    [DataField("minLevel")]
    public int MinLevel = 0;
}

