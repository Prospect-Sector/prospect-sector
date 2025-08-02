using Content.Shared._PS.Expadition;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._PS.Expadition;

public sealed class ExpaditionConsoleBoundUserInterface : BoundUserInterface
{
    private ExpaditionMenu? _menu;

    public ExpaditionConsoleBoundUserInterface(EntityUid owner, Enum uiKey)
        : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        if (_menu != null)
            return;

        _menu = this.CreateWindow<ExpaditionMenu>();
        _menu.OnStartExpadition += StartExpadition;
    }

    private void StartExpadition(BaseButton.ButtonEventArgs args)
    {
        SendMessage(new StartExpaditionMessage());
        Close();
    }
}
