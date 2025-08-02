using Robust.Shared.Audio;

namespace Content.Shared._PS.Expadition;

[RegisterComponent]
[Access(typeof(SharedExpaditionSystem))]
public sealed partial class ExpaditionConsoleComponent : Component
{
    [DataField]
    public SoundSpecifier ErrorSound = new SoundPathSpecifier("/Audio/Effects/Cargo/buzz_sigh.ogg");

    [DataField]
    public SoundSpecifier SuccessSound = new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg");
}
