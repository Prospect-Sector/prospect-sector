using Content.Shared._PS.Terradrop;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._PS.Terradrop;

public sealed class TerradropConsoleBoundUserInterface : BoundUserInterface
{
    private TerradropConsoleMenu? _menu;

    public TerradropConsoleBoundUserInterface(EntityUid owner, Enum uiKey)
        : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        if (_menu != null)
            return;

        _menu = this.CreateWindow<TerradropConsoleMenu>();
        _menu.OnStartButtonPressed += SendStartTerradropMessage;
    }

    private void SendStartTerradropMessage(BaseButton.ButtonEventArgs args)
    {
        SendMessage(new StartTerradropMessage());
        Close();
    }
}
