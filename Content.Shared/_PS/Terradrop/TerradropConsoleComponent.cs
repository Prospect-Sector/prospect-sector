using Robust.Shared.Audio;

namespace Content.Shared._PS.Terradrop;

[RegisterComponent]
[Access(typeof(SharedTerradropSystem))]
public sealed partial class TerradropConsoleComponent : Component
{
    [DataField]
    public string LandingPadTileName = "FloorSteel";

    [DataField]
    public SoundSpecifier ErrorSound = new SoundPathSpecifier("/Audio/Effects/Cargo/buzz_sigh.ogg");

    [DataField]
    public SoundSpecifier SuccessSound = new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg");
}
