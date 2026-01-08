using System.Linq;
using Content.Shared._PS.Terradrop;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;

namespace Content.Client._PS.Terradrop;

public sealed class TerradropConsoleBoundUserInterface : BoundUserInterface
{
    private TerradropConsoleMenu? _consoleMenu;

    [Dependency] private readonly ILogManager _logManager = default!;
    private ISawmill _sawmill = default!;

    public TerradropConsoleBoundUserInterface(EntityUid owner, Enum uiKey)
        : base(owner, uiKey)
    {
        IoCManager.InjectDependencies(this);

        _sawmill = _logManager.GetSawmill("terradrop.console");
        _sawmill.Debug($"TerradropConsoleBoundUserInterface created for {owner} with key {uiKey}");
    }

    protected override void Open()
    {
        base.Open();

        var owner = Owner;
        _sawmill.Debug($"Opening UI for {owner}");

        if (_consoleMenu != null)
            return;

        _consoleMenu = this.CreateWindow<TerradropConsoleMenu>();
        _consoleMenu.SetEntity(owner);
        _consoleMenu.OnClose += () => _consoleMenu = null;

        // Set up terradrop start handler
        _consoleMenu.OnStartTerradropPressed += (id, level) =>
        {
            try
            {
                _sawmill.Debug($"Sending StartTerradropMessage for map ID: {id}, level: {level}");

                // Create and send the message with level
                var message = new StartTerradropMessage(id, level);
                SendMessage(message);
                _sawmill.Info($"Sent start message for terradrop: {id} at level {level}");
            }
            catch (Exception ex)
            {
                _sawmill.Error($"Error sending terradrop start message for {id}: {ex}");
            }
        };
    }

    public override void OnProtoReload(PrototypesReloadedEventArgs args)
    {
        base.OnProtoReload(args);

        if (!args.WasModified<TerradropMapPrototype>())
            return;

        if (State is not TerradropConsoleBoundInterfaceState rState)
            return;

        _sawmill.Debug("Reloading prototypes in UI");
        _consoleMenu?.UpdatePanels(rState.MapNodes);
        _consoleMenu?.UpdateInformationPanel();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not TerradropConsoleBoundInterfaceState castState)
        {
            _sawmill.Warning("Received non-ResearchConsoleBoundInterfaceState state");
            return;
        }

        // Thats for avoiding refresh spam when only points are updated
        if (_consoleMenu == null)
        {
            _sawmill.Warning("Console menu is null during state update");
            return;
        }

        _sawmill.Debug($"Updating UI state with {castState.MapNodes.Count} terradrop map nodes.");

        var availableTechs = castState.MapNodes.Count(t => t.Value == TerradropMapAvailability.Unexplored);
        _sawmill.Debug($"New maps to explore: {availableTechs}");

        if (!_consoleMenu.List.SequenceEqual(castState.MapNodes))
        {
            _sawmill.Debug("Map node list changed, updating panels");
            _consoleMenu.UpdatePanels(castState.MapNodes);
        }

        _consoleMenu.UpdateInformationPanel(); // always update panel
    }
}
