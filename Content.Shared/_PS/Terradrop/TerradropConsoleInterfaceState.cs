using Robust.Shared.Serialization;

namespace Content.Shared._PS.Terradrop;

[NetSerializable, Serializable]
public sealed class TerradropConsoleInterfaceState: BoundUserInterfaceState
{
    public bool CanOpenPortal;

    public TerradropConsoleInterfaceState(bool canOpenPortal)
    {
        CanOpenPortal = canOpenPortal;
    }
}
