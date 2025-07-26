using Content.Server._PS.GameRule.Components;
using Content.Server.GameTicking.Rules;
using Content.Shared.GameTicking.Components;
using Robust.Shared.Timing;

namespace DefaultNamespace;

public sealed class ProspectGameRuleSystem : GameRuleSystem<ProspectGameRuleComponent>
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;

    public override void Initialize()
    {
        base.Initialize();
    }

    protected override void Added(EntityUid uid, ProspectGameRuleComponent component, GameRuleComponent gameRule, GameRuleAddedEvent args)
    {
        base.Added(uid, component, gameRule, args);
    }
}
