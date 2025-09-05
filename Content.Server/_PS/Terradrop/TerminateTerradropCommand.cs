using Content.Server.Administration;
using Content.Server.Salvage.Expeditions;
using Content.Shared._PS.Terradrop;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Timing;

namespace Content.Server._PS.Terradrop;

[AdminCommand(AdminFlags.Admin)]
public sealed class TerminateTerradropCommand : LocalizedCommands
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;

    private ISawmill _sawmill = default!;
    public override string Command => "terradrop_terminate_all";
    public override string Description => "Terminates all terradrop missions currently in progress.";
    public override string Help => "Usage: literally just run the command without any arguments. " +
                          "This will terminate all active terradrop maps and bodybag return all players in it.";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var query = _entityManager.EntityQueryEnumerator<SalvageExpeditionComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            _entityManager.QueueDeleteEntity(uid);
        }

        var padQuery = _entityManager.EntityQueryEnumerator<TerradropPadComponent>();
        while (padQuery.MoveNext(out var uid, out var comp))
        {
            comp.ActivatedAt = _timing.CurTime - comp.ClearPortalDelay;
        }

        shell.WriteLine("All active terradrop jobs have been terminated.");
    }
}
