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
    /// A color used for UI
    /// </summary>
    [DataField("color", required: true)]
    public Color Color;

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

}

