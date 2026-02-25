using System.Numerics;
using Content.Server._PS.Prospectable;  // Prospect
using Content.Server.GameTicking;
using Content.Server.Spawners.Components;
using Content.Shared._PS.Prospectable; // Prospect
using Content.Shared._PS.ScatterOnSpawn; // Prospect
using Content.Shared.EntityTable;
using Content.Shared.GameTicking.Components;
using JetBrains.Annotations;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server.Spawners.EntitySystems
{
    [UsedImplicitly]
    public sealed class ConditionalSpawnerSystem : EntitySystem
    {
        [Dependency] private readonly IRobustRandom _robustRandom = default!;
        [Dependency] private readonly GameTicker _ticker = default!;
        [Dependency] private readonly EntityTableSystem _entityTable = default!;

        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<GameRuleStartedEvent>(OnRuleStarted);
            SubscribeLocalEvent<ConditionalSpawnerComponent, MapInitEvent>(OnCondSpawnMapInit);
            SubscribeLocalEvent<RandomSpawnerComponent, MapInitEvent>(OnRandSpawnMapInit);
            SubscribeLocalEvent<EntityTableSpawnerComponent, MapInitEvent>(OnEntityTableSpawnMapInit);
        }

        private void OnCondSpawnMapInit(EntityUid uid, ConditionalSpawnerComponent component, MapInitEvent args)
        {
            TrySpawn(uid, component);
        }

        private void OnRandSpawnMapInit(EntityUid uid, RandomSpawnerComponent component, MapInitEvent args)
        {
            Spawn(uid, component);
            if (component.DeleteSpawnerAfterSpawn)
                QueueDel(uid);
        }

        private void OnEntityTableSpawnMapInit(Entity<EntityTableSpawnerComponent> ent, ref MapInitEvent args)
        {
            Spawn(ent);
            if (ent.Comp.DeleteSpawnerAfterSpawn && !TerminatingOrDeleted(ent) && Exists(ent))
                QueueDel(ent);
        }

        private void OnRuleStarted(ref GameRuleStartedEvent args)
        {
            var query = EntityQueryEnumerator<ConditionalSpawnerComponent>();
            while (query.MoveNext(out var uid, out var spawner))
            {
                RuleStarted(uid, spawner, args);
            }
        }

        public void RuleStarted(EntityUid uid, ConditionalSpawnerComponent component, GameRuleStartedEvent obj)
        {
            if (component.GameRules.Contains(obj.RuleId))
                Spawn(uid, component);
        }

        private void TrySpawn(EntityUid uid, ConditionalSpawnerComponent component)
        {
            if (component.GameRules.Count == 0)
            {
                Spawn(uid, component);
                return;
            }

            foreach (var rule in component.GameRules)
            {
                if (!_ticker.IsGameRuleActive(rule))
                    continue;
                Spawn(uid, component);
                return;
            }
        }

        private void Spawn(EntityUid uid, ConditionalSpawnerComponent component)
        {
            if (component.Chance != 1.0f && !_robustRandom.Prob(component.Chance))
                return;

            if (component.Prototypes.Count == 0)
            {
                Log.Warning($"Prototype list in ConditionalSpawnComponent is empty! Entity: {ToPrettyString(uid)}");
                return;
            }

            // Prospect: Jack spawns to events to add comps
            if (!Deleted(uid))
            {
                var entity = Spawn(_robustRandom.Pick(component.Prototypes), Transform(uid).Coordinates);
                var randomItemSpawnedEvent = new RandomItemSpawnedEvent(entity);
                RaiseLocalEvent(ref randomItemSpawnedEvent);
            }
            // End Prospect
        }

        private void Spawn(EntityUid uid, RandomSpawnerComponent component)
        {
            if (component.RarePrototypes.Count > 0 && (component.RareChance == 1.0f || _robustRandom.Prob(component.RareChance)))
            {
                // Prospect: apply offset, scatter, and raise event for rare spawns so they get level-based stats
                var rareOffset = component.Offset;
                var rareXOffset = _robustRandom.NextFloat(-rareOffset, rareOffset);
                var rareYOffset = _robustRandom.NextFloat(-rareOffset, rareOffset);
                var rareCoordinates = Transform(uid).Coordinates.Offset(new Vector2(rareXOffset, rareYOffset));
                var rareEntity = Spawn(_robustRandom.Pick(component.RarePrototypes), rareCoordinates);
                if (TryComp<PSScatterOnSpawnComponent>(uid, out var rareScatter))
                    AddComp(rareEntity, new PSScatterOnSpawnComponent { Force = rareScatter.Force });
                var rareItemSpawnedEvent = new RandomItemSpawnedEvent(rareEntity);
                RaiseLocalEvent(ref rareItemSpawnedEvent);
                // Prospect: End
                return;
            }

            if (component.Chance != 1.0f && !_robustRandom.Prob(component.Chance))
                return;

            if (component.Prototypes.Count == 0)
            {
                Log.Warning($"Prototype list in RandomSpawnerComponent is empty! Entity: {ToPrettyString(uid)}");
                return;
            }

            if (Deleted(uid))
                return;

            var offset = component.Offset;
            var xOffset = _robustRandom.NextFloat(-offset, offset);
            var yOffset = _robustRandom.NextFloat(-offset, offset);

            var coordinates = Transform(uid).Coordinates.Offset(new Vector2(xOffset, yOffset));

            // Prospect: Jack spawns to events to add comps and scatter
            var entity = Spawn(_robustRandom.Pick(component.Prototypes), coordinates);
            if (TryComp<PSScatterOnSpawnComponent>(uid, out var scatter))
                AddComp(entity, new PSScatterOnSpawnComponent { Force = scatter.Force });
            var randomItemSpawnedEvent = new RandomItemSpawnedEvent(entity);
            RaiseLocalEvent(ref randomItemSpawnedEvent);
            // End Prospect
        }

        private void Spawn(Entity<EntityTableSpawnerComponent> ent)
        {
            if (TerminatingOrDeleted(ent) || !Exists(ent))
                return;

            var coords = Transform(ent).Coordinates;

            var spawns = _entityTable.GetSpawns(ent.Comp.Table);
            foreach (var proto in spawns)
            {
                var xOffset = _robustRandom.NextFloat(-ent.Comp.Offset, ent.Comp.Offset);
                var yOffset = _robustRandom.NextFloat(-ent.Comp.Offset, ent.Comp.Offset);
                var trueCoords = coords.Offset(new Vector2(xOffset, yOffset));

                // Prospect: Jack spawns to events to add comps
                var entity = SpawnAtPosition(proto, trueCoords);
                var randomItemSpawnedEvent = new RandomItemSpawnedEvent(entity);
                RaiseLocalEvent(ref randomItemSpawnedEvent);
                // End Prospect
            }
        }
    }
}
