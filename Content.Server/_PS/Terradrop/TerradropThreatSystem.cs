using System.Linq;
using System.Numerics;
using Content.Server.Chat.Managers;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.Doors.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs;
using Content.Shared.Tag;
using Content.Shared.Weapons.Melee.Events;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Content.Server.Ghost.Roles.Components;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared.CombatMode;
using Content.Shared.NPC;
using Content.Shared.NPC.Components;
using Robust.Shared.Map;
using Content.Server.Parallax;
using Content.Server.Procedural;
using Content.Shared._PS.Terradrop;
using Content.Shared.Chat;
using Content.Shared.Construction.EntitySystems;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Salvage.Expeditions;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._PS.Terradrop;

/// <summary>
/// Drops hostile-mob spawners into the biome around a terradrop dungeon at a
/// pace that ramps up over the first ~15 minutes of the mission. Mobs produced
/// by these spawners are put into terradrop hunt mode (NavSmash / NavPry /
/// NavClimb, widened vision) so their A* pathfinder will route through walls
/// and airlocks, smashing obstacles, until it reaches a player.
/// </summary>
public sealed class TerradropThreatSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly BiomeSystem _biome = default!;
    [Dependency] private readonly AnchorableSystem _anchorable = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly NPCSteeringSystem _steering = default!;
    [Dependency] private readonly SharedCombatModeSystem _combatMode = default!;
    [Dependency] private readonly TagSystem _tag = default!;

    /// <summary>Flat damage multiplier applied when a threat mob hits a wall / door.</summary>
    private const float WallDamageMultiplier = 5f;

    /// <summary>
    /// Global multiplier sourced from <c>terradrop.threat_multiplier</c>. 1.0 = baseline,
    /// higher values shorten intervals, grow bursts, and raise the spawner cap.
    /// </summary>
    private float _threatMultiplier = 5f;

    private const string ThreatSpawnerProto = "TerradropThreatSpawner";
    // Spawn annulus measured from the nearest player, not from origin. BiomeSystem
    // only materialises tiles within ~16 tiles of a player, and PathfindingSystem
    // only builds nav polys for chunks that have had TileChangedEvent fire — so
    // anything further out has no nav data and the mob's path request returns
    // NoPath, which collapses the HTN plan to idle. Keeping spawns within the
    // biome load range guarantees the intermediate chunks are navigable.
    // BiomeSystem only materialises tiles — and PathfindingSystem only builds
    // nav polys — within ~16 tiles of each player. Spawning beyond that puts
    // the mob in an un-navigable chunk, so MoveTo returns NoPath and the mob
    // idles forever even with HuntEngaged=true. Keep the outer radius safely
    // inside that window.
    private const int PlayerInnerRadius = 9;
    private const int PlayerOuterRadius = 14;
    // Small exclusion around map origin — just the landing pad disc. The
    // real arrival room is protected by PortalRoomSafeRadius (portal position
    // is inside the dungeon prefab, not at origin).
    private const int SafeZoneRadius = 8;
    private const int Clearance = 4;

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_cfg, CCVars.TerradropThreatMultiplier,
            value => _threatMultiplier = MathF.Max(0.01f, value / 100f), true);
        SubscribeLocalEvent<TerradropHunterComponent, MeleeHitEvent>(OnHunterMeleeHit);
    }

    /// <summary>
    /// Amplify damage against walls / airlocks so threat mobs can actually
    /// breach the dungeon and the player's outposts instead of chipping at them
    /// for minutes. Base wall HP is ~300 which takes a xeno ~50 hits at 6 dmg;
    /// 5x brings it down to a manageable ~10 hits.
    /// </summary>
    private void OnHunterMeleeHit(EntityUid uid, TerradropHunterComponent comp, MeleeHitEvent args)
    {
        if (!args.IsHit || args.HitEntities.Count == 0)
            return;

        var hitsWall = false;
        foreach (var target in args.HitEntities)
        {
            if (_tag.HasTag(target, "Wall") || HasComp<DoorComponent>(target))
            {
                hitsWall = true;
                break;
            }
        }
        if (!hitsWall)
            return;

        // Add BonusDamage = BaseDamage * (multiplier - 1) so final = BaseDamage * multiplier.
        // Using BonusDamage (additive before modifier sets) keeps it clean and visible.
        args.BonusDamage += args.BaseDamage * (WallDamageMultiplier - 1f);
    }

    private TimeSpan _nextHuntDiag = TimeSpan.Zero;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        UpdateThreatEscalation();
        UpdateThreatSpawners();
        UpdateHuntTargeting();
        UpdateHuntDiagnostic();
    }

    /// <summary>
    /// Periodic state dump for terradrop mobs — one line per mob every 5s so we
    /// can see from the log whether each mob is Active, has a Target in its
    /// blackboard, has an NPCSteeringComponent, is in combat mode, and roughly
    /// where it is vs the nearest player. Lets us narrow "mobs just stand there"
    /// down to: no target? no steering? no path? combat off?
    /// </summary>
    private void UpdateHuntDiagnostic()
    {
        var now = _timing.CurTime;
        if (now < _nextHuntDiag)
            return;
        _nextHuntDiag = now + TimeSpan.FromSeconds(5);

        var mobQuery = EntityQueryEnumerator<TerradropHunterComponent, HTNComponent, TransformComponent>();
        while (mobQuery.MoveNext(out var uid, out _, out var htn, out var xform))
        {
            if (xform.MapUid is not { } mapUid || !HasComp<TerradropMapComponent>(mapUid))
                continue;
            var hasTarget = htn.Blackboard.TryGetValue<EntityUid>("Target", out _, EntityManager);
            var engaged = htn.Blackboard.GetValueOrDefault<bool>(HuntEngagedKey, EntityManager);
            var hasSteering = TryComp<Content.Server.NPC.Components.NPCSteeringComponent>(uid, out var steerComp);
            var steerStatus = steerComp?.Status.ToString() ?? "-";
            var pathLen = steerComp?.CurrentPath.Count ?? 0;
            var hasActive = HasComp<ActiveNPCComponent>(uid);
            var inCombat = TryComp<CombatModeComponent>(uid, out var cm) && cm.IsInCombatMode;
            var vision = htn.Blackboard.GetValueOrDefault<float>("VisionRadius", EntityManager);
            var planOp = htn.Plan?.CurrentOperator?.GetType().Name ?? "<no-plan>";
            Log.Info($"Terradrop hunt diag {ToPrettyString(uid)}: active={hasActive} engaged={engaged} target={hasTarget} steer={hasSteering}({steerStatus},path={pathLen}) combat={inCombat} vision={vision:F0} op={planOp}");
        }
    }

    /// <summary>
    /// Force-assigns the nearest living player on each terradrop map as the blackboard
    /// <c>Target</c> for every threat mob, bypassing HTN's own target-acquisition path.
    /// <see cref="Content.Server.NPC.Queries.Queries.NearbyHostilesQuery"/> only returns
    /// entities with <c>NpcFactionMemberComponent</c> within vision radius, and something
    /// in that chain (faction membership on players, lookup timing, vision-radius default
    /// resolution) was leaving our mobs without a target — so instead we set it directly
    /// once a second. Once <c>Target</c> exists, <c>MeleeAttackTargetCompound</c> picks up
    /// and runs its <c>MoveToOperator</c> + <c>MeleeOperator</c> chain.
    /// </summary>
    /// <summary>Per-mob interval between engagement coin-flips.</summary>
    private static readonly TimeSpan HuntDecisionInterval = TimeSpan.FromSeconds(15);
    /// <summary>Probability a mob picks up the chase at each decision tick.</summary>
    private const float HuntEngageProbability = 0.6f;
    private const string HuntNextDecisionKey = "TerradropHuntNextDecision";
    private const string HuntEngagedKey = "TerradropHuntEngaged";

    private void UpdateHuntTargeting()
    {
        var now = _timing.CurTime;
        // Runs every frame (no throttle) so Target / TargetCoordinates are
        // restored on the real blackboard immediately after any HTN plan
        // shutdown that happens to wipe them. Cheap — one EnsureComp per
        // hunter per tick plus a single SetValue.

        // Bucket candidate player targets by the terradrop map they're on.
        var playersByMap = new Dictionary<EntityUid, List<(EntityUid Uid, Vector2 Pos)>>();
        var playerQuery = EntityQueryEnumerator<ActorComponent, MobStateComponent, TransformComponent>();
        while (playerQuery.MoveNext(out var playerUid, out _, out var mobState, out var xform))
        {
            if (mobState.CurrentState != MobState.Alive)
                continue;
            if (xform.MapUid is not { } mapUid || !HasComp<TerradropMapComponent>(mapUid))
                continue;
            if (!playersByMap.TryGetValue(mapUid, out var list))
                playersByMap[mapUid] = list = new();
            list.Add((playerUid, xform.LocalPosition));
        }

        if (playersByMap.Count == 0)
            return;

        var mobQuery = EntityQueryEnumerator<TerradropHunterComponent, HTNComponent, TransformComponent>();
        while (mobQuery.MoveNext(out var mobUid, out _, out var htn, out var xform))
        {
            if (xform.MapUid is not { } mapUid || !playersByMap.TryGetValue(mapUid, out var candidates))
                continue;

            var mobPos = xform.LocalPosition;
            EntityUid? nearest = null;
            var bestSq = float.MaxValue;
            foreach (var (uid, pos) in candidates)
            {
                var d = (pos - mobPos).LengthSquared();
                if (d < bestSq)
                {
                    bestSq = d;
                    nearest = uid;
                }
            }

            if (nearest is not { } targetUid)
                continue;

            htn.Blackboard.SetValue(NPCBlackboard.Owner, mobUid);

            // Every HuntDecisionInterval the mob flips a coin to decide whether
            // it engages. Staggers pack behaviour so a crowd doesn't all converge
            // the instant a player re-enters the map.
            var nextDecision = htn.Blackboard.GetValueOrDefault<TimeSpan>(HuntNextDecisionKey, EntityManager);
            var engaged = htn.Blackboard.GetValueOrDefault<bool>(HuntEngagedKey, EntityManager);
            if (now >= nextDecision)
            {
                engaged = _random.Prob(HuntEngageProbability);
                htn.Blackboard.SetValue(HuntEngagedKey, engaged);
                htn.Blackboard.SetValue(HuntNextDecisionKey, now + HuntDecisionInterval);
            }

            // Always keep Target / TargetCoordinates populated — MoveToOperator.Startup
            // throws if TargetCoordinates disappears between plan and startup. The
            // TerradropHuntEngaged flag (flipped above) is what gates MeleeAttackTargetCompound
            // vs IdleCompound at plan time, so removing keys here would also race.
            htn.Blackboard.SetValue("Target", targetUid);
            // UtilityOperator writes both Target AND TargetCoordinates; MoveToOperator
            // reads TargetCoordinates so the pair must stay in sync or the MoveTo step
            // silently fails and the plan falls to IdleCompound.
            var targetCoords = new EntityCoordinates(targetUid, Vector2.Zero);
            htn.Blackboard.SetValue("TargetCoordinates", targetCoords);

            // Belt-and-braces: drive the steering system directly so the mob starts
            // walking toward the player this tick even if HTN's MeleeCombatCompound
            // branch failed during planning (e.g. because NearbyHostilesQuery came
            // back empty for some reason we haven't pinned down yet). HTN's own
            // MoveToOperator ends up calling the same Register() anyway.
            _steering.TryRegister(mobUid, targetCoords);

            // Ensure combat mode is on so MeleeOperator actually swings when the
            // mob arrives in melee range; some mob prototypes start with it off.
            if (TryComp<CombatModeComponent>(mobUid, out var combat) && !combat.IsInCombatMode)
                _combatMode.SetInCombatMode(mobUid, true, combat);

            // Make absolutely sure the mob is flagged hostile to the player's
            // faction. If it lacks NpcFactionMember entirely, NearbyHostilesQuery
            // will never return it to anyone either.
            EnsureComp<NpcFactionMemberComponent>(mobUid);
        }
    }

    private void UpdateThreatEscalation()
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<TerradropThreatComponent, MapGridComponent>();
        while (query.MoveNext(out var mapUid, out var threat, out var grid))
        {
            if (now < threat.NextPlacement)
                continue;

            var progress = ComputeProgress(threat, now);
            var interval = MathHelper.Lerp(
                threat.StartPlacementIntervalSeconds,
                threat.PeakPlacementIntervalSeconds,
                progress) / _threatMultiplier;
            threat.NextPlacement = now + TimeSpan.FromSeconds(Math.Max(0.5f, interval));

            var scaledCap = (int)MathF.Ceiling(threat.MaxSpawners * _threatMultiplier);
            if (threat.SpawnerCount >= scaledCap)
                continue;

            if (TryPlaceSpawner((mapUid, grid), threat, progress, mapUid))
            {
                threat.SpawnerCount++;
                Log.Info($"Terradrop threat: placed spawner #{threat.SpawnerCount} on {ToPrettyString(mapUid)} (progress={progress:F2}, next in {interval:F0}s, mult={_threatMultiplier:F1}x)");
                AnnounceToMap(mapUid, threat.SpawnerCount == 1
                    ? "An incursion has begun in the surrounding terrain."
                    : $"Another incursion beacon has been detected. ({threat.SpawnerCount} active)");
            }
            else
            {
                var anchor = FindRandomPlayerAnchor(mapUid);
                Log.Warning($"Terradrop threat: failed to place spawner on {ToPrettyString(mapUid)} (faction={threat.FactionId}, center={threat.DungeonCenter}, bounds={threat.DungeonBounds}, anchor={anchor?.ToString() ?? "<none>"}, portal={threat.PortalRoomPosition}). Rejection sampling found no valid outside tile.");
            }
        }
    }

    private void UpdateThreatSpawners()
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<TerradropThreatSpawnerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var spawner, out var xform))
        {
            if (spawner.Prototypes.Count == 0)
                continue;
            if (now < spawner.NextFire)
                continue;

            spawner.NextFire = now + spawner.Interval;

            var count = _random.Next(spawner.MinBurst, Math.Max(spawner.MinBurst, spawner.MaxBurst) + 1);

            // Enforce per-map live-hunter cap = map level. Old mobs get culled
            // oldest-first so the incursion feels alive without ballooning
            // toward the entity limit on long missions.
            if (xform.MapUid is { } mapUid)
                EnforceHunterCap(mapUid, count);

            for (var i = 0; i < count; i++)
            {
                var proto = _random.Pick(spawner.Prototypes);
                var mob = SpawnAtPosition(proto, xform.Coordinates);
                OnThreatMobSpawned(mob);
            }
            Log.Info($"Terradrop threat: spawner {ToPrettyString(uid)} fired {count} mob(s), next in {spawner.Interval.TotalSeconds:F0}s");
        }
    }

    /// <summary>
    /// Applies the terradrop hunt-mode overrides to a freshly spawned mob. Done here
    /// (rather than via a <c>ComponentStartup</c> subscription on <c>HTNComponent</c>,
    /// which <c>HTNSystem</c> already owns) so we keep the single subscribe/one post-spawn
    /// path clean.
    /// </summary>
    private void OnThreatMobSpawned(EntityUid mob)
    {
        // Strip ghost-takeover so the mob is controlled by its HTN, not a waiting player.
        RemComp<GhostRoleComponent>(mob);
        RemComp<GhostTakeoverAvailableComponent>(mob);

        // Tag for mission-level scaling so these threat mobs get the same health/damage
        // curve as interior mobs on this dungeon level.
        EnsureComp<TerradropMobComponent>(mob);
        // Counts toward the mission's ObjectiveRequired kill target — without
        // this, players could never complete the mission by farming incursions.
        EnsureComp<TerradropObjectiveTargetComponent>(mob);
        // Scope for UpdateHuntTargeting / diag — must not touch dungeon-interior
        // mobs using vanilla XenoCompound.
        var hunter = EnsureComp<TerradropHunterComponent>(mob);
        hunter.SpawnedAt = _timing.CurTime;

        if (!TryComp<HTNComponent>(mob, out var htn))
            return;

        // Swap to a root task that plans MeleeAttackTargetCompound directly off
        // the Target key we force-inject, skipping MeleeCombatCompound's utility
        // query (which was returning empty for most mobs, collapsing their plan
        // to IdleCompound). Keeps the standard Melee / MoveTo / Juke operators.
        htn.RootTask = new HTNCompoundTask { Task = "TerradropHuntCompound" };
        htn.PlanAccumulator = 0f;

        // Start engaged with the standard probability so a fresh wave doesn't
        // have to wait the full decision window before any of them charge.
        // The staggered offset still prevents synchronised pulsing on re-roll.
        var offset = _random.NextFloat() * (float)HuntDecisionInterval.TotalSeconds;
        htn.Blackboard.SetValue(HuntNextDecisionKey, _timing.CurTime + TimeSpan.FromSeconds(offset));
        htn.Blackboard.SetValue(HuntEngagedKey, _random.Prob(HuntEngageProbability));

        htn.Blackboard.SetValue(NPCBlackboard.NavSmash, true);
        htn.Blackboard.SetValue(NPCBlackboard.NavPry, true);
        htn.Blackboard.SetValue(NPCBlackboard.NavClimb, true);
        htn.Blackboard.SetValue(NPCBlackboard.NavInteract, true);

        // Vision wide enough to cover the whole map. NearbyHostilesQuery is a pure
        // radius test (no line-of-sight), so the mob will always find the nearest
        // player regardless of walls between them; pathfinding with NavSmash/Pry
        // handles actually reaching that target through obstacles.
        htn.Blackboard.SetValue("VisionRadius", 1000f);
        htn.Blackboard.SetValue("AggroVisionRadius", 1000f);

        // Force an immediate replan so the newly-hostile blackboard takes effect
        // this tick instead of waiting out the default PlanCooldown.
        htn.PlanAccumulator = 0f;

        Log.Info($"Terradrop threat: armed hunt-mode on {ToPrettyString(mob)} (faction={GetFaction(mob)})");
    }

    private string GetFaction(EntityUid mob)
    {
        if (!TryComp<Content.Shared.NPC.Components.NpcFactionMemberComponent>(mob, out var fac))
            return "<none>";
        return string.Join(",", fac.Factions);
    }

    /// <summary>
    /// Minimum hunter cap regardless of map level. A level-0 map would otherwise
    /// spawn nothing; keep a baseline so incursions still function on low-level
    /// maps during playtesting.
    /// </summary>
    private const int MinHunterCap = 6;

    private void EnforceHunterCap(EntityUid mapUid, int incomingBurst)
    {
        if (!TryComp<TerradropMapComponent>(mapUid, out var mapComp))
            return;

        var cap = Math.Max(MinHunterCap, mapComp.Level);
        // After the burst fires we'll have `currentCount + incomingBurst` alive.
        // Work out how many we need to cull now so we land at (or below) cap.
        var currentCount = 0;
        var live = new List<(EntityUid Uid, TimeSpan SpawnedAt)>();
        var query = EntityQueryEnumerator<TerradropHunterComponent, TransformComponent>();
        while (query.MoveNext(out var mobUid, out var hunter, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;
            currentCount++;
            live.Add((mobUid, hunter.SpawnedAt));
        }

        var overflow = currentCount + incomingBurst - cap;
        if (overflow <= 0)
            return;

        // Cull oldest first — those are the mobs the player is furthest from
        // anyway (they spawned when the player was somewhere else), so deleting
        // them is the least noticeable.
        live.Sort((a, b) => a.SpawnedAt.CompareTo(b.SpawnedAt));
        var toRemove = Math.Min(overflow, live.Count);
        for (var i = 0; i < toRemove; i++)
        {
            QueueDel(live[i].Uid);
        }
    }

    private void AnnounceToMap(EntityUid mapUid, string text)
    {
        if (!TryComp<MapComponent>(mapUid, out var mapComp))
            return;
        _chat.ChatMessageToManyFiltered(
            Filter.BroadcastMap(mapComp.MapId),
            ChatChannel.Radio,
            text,
            text,
            mapUid,
            false,
            true,
            null);
    }

    private static float ComputeProgress(TerradropThreatComponent threat, TimeSpan now)
    {
        var duration = (float)threat.Duration.TotalSeconds;
        if (duration <= 0f)
            return 1f;
        var elapsed = (float)(now - threat.StartTime).TotalSeconds;
        return Math.Clamp(elapsed / duration, 0f, 1f);
    }

    private bool TryPlaceSpawner(Entity<MapGridComponent> grid, TerradropThreatComponent threat, float progress, EntityUid mapUid)
    {
        if (!_proto.TryIndex<SalvageFactionPrototype>(threat.FactionId, out var faction) || faction.MobGroups.Count == 0)
            return false;

        var tile = FindOutsideTile(grid, threat, mapUid);
        if (tile == null)
            return false;

        var coords = _map.GridTileToLocal(grid.Owner, grid, tile.Value);
        var spawner = Spawn(ThreatSpawnerProto, coords);

        var comp = EnsureComp<TerradropThreatSpawnerComponent>(spawner);
        comp.Prototypes = faction.MobGroups.Select(m => (EntProtoId)m.Proto).ToList();

        var fireInterval = MathHelper.Lerp(
            threat.StartSpawnerIntervalSeconds,
            threat.PeakSpawnerIntervalSeconds,
            progress) / _threatMultiplier;
        comp.Interval = TimeSpan.FromSeconds(Math.Max(0.5f, fireInterval));
        var baseMin = 1 + (int)(progress * 2f);
        var baseMax = 2 + (int)(progress * 3f) + threat.Level / 5;
        comp.MinBurst = (int)MathF.Ceiling(baseMin * _threatMultiplier);
        comp.MaxBurst = (int)MathF.Ceiling(baseMax * _threatMultiplier);
        comp.NextFire = _timing.CurTime + comp.Interval;
        return true;
    }

    private Vector2i? FindOutsideTile(Entity<MapGridComponent> grid, TerradropThreatComponent threat, EntityUid mapUid)
    {
        if (!TryComp<BiomeComponent>(grid.Owner, out var biome))
            return null;

        // Pick a random living player on this map as the spawn anchor. No players
        // on the map → nothing to hunt, skip this tick.
        var anchor = FindRandomPlayerAnchor(mapUid);
        if (anchor is not { } anchorPos)
            return null;

        var halfX = threat.DungeonBounds.X / 2 + Clearance;
        var halfY = threat.DungeonBounds.Y / 2 + Clearance;
        var originSafeRSq = SafeZoneRadius * SafeZoneRadius;
        var portalSafeRSq = threat.PortalRoomSafeRadius * threat.PortalRoomSafeRadius;

        // Bias spawn direction away from the dungeon centre so the rejection
        // sampling doesn't have to dodge a huge AABB that typically covers one
        // full half of the player's spawn annulus. If the player is roughly at
        // the dungeon centre (vector length ~0), fall back to a uniform angle.
        var away = new Vector2(anchorPos.X - threat.DungeonCenter.X, anchorPos.Y - threat.DungeonCenter.Y);
        var awayMag = away.Length();
        var useDirectionalBias = awayMag > 1f;
        var awayAngle = useDirectionalBias ? MathF.Atan2(away.Y, away.X) : 0f;

        for (var attempt = 0; attempt < 64; attempt++)
        {
            // Annulus centred on the chosen player so the biome chunks & nav polys
            // around the spawn tile are already loaded — pathfinder can then route
            // back to the player.
            float angle;
            if (useDirectionalBias)
            {
                // Cone of ±110° around the outward direction from the dungeon —
                // covers the full outside half-plane plus some tolerance, so mobs
                // can still appear flanking the player from behind.
                var offset = (_random.NextFloat() - 0.5f) * MathF.PI * 1.22f;
                angle = awayAngle + offset;
            }
            else
            {
                angle = _random.NextFloat() * MathF.PI * 2f;
            }

            var radius = PlayerInnerRadius + _random.NextFloat() * (PlayerOuterRadius - PlayerInnerRadius);
            var tile = new Vector2i(
                anchorPos.X + (int)(MathF.Cos(angle) * radius),
                anchorPos.Y + (int)(MathF.Sin(angle) * radius));

            // Belt-and-braces safe zone around the map origin in case the landing
            // prefab failed to place and the return portal defaulted to (0,0).
            if (tile.X * tile.X + tile.Y * tile.Y <= originSafeRSq)
                continue;

            // Hard exclusion around the actual portal room (the guaranteed
            // landing prefab placed inside the BSP dungeon). This is what
            // players actually arrive in — must never get a spawner on top.
            var dx = tile.X - threat.PortalRoomPosition.X;
            var dy = tile.Y - threat.PortalRoomPosition.Y;
            if (dx * dx + dy * dy <= portalSafeRSq)
                continue;

            // Reject the dungeon's axis-aligned footprint (plus clearance) so the
            // spawner doesn't drop into a sealed room.
            if (Math.Abs(tile.X - threat.DungeonCenter.X) <= halfX &&
                Math.Abs(tile.Y - threat.DungeonCenter.Y) <= halfY)
                continue;

            if (!_biome.TryGetBiomeTile(grid.Owner, grid, tile, out _))
                continue;

            // Biome entities (rock walls, trees, etc.) are generated lazily when a
            // chunk loads near a player — so TileFree returns true now but a wall
            // will pop in on top of us later. Ask the biome what it *would* place
            // here and reject the tile if that slot is claimed.
            if (_biome.TryGetEntity(tile, biome, grid, out _))
                continue;

            if (!_anchorable.TileFree(grid, tile, DungeonSystem.CollisionLayer, DungeonSystem.CollisionMask))
                continue;

            return tile;
        }

        return null;
    }

    private Vector2i? FindRandomPlayerAnchor(EntityUid mapUid)
    {
        var candidates = new List<Vector2i>();
        var query = EntityQueryEnumerator<ActorComponent, MobStateComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var mobState, out var xform))
        {
            if (mobState.CurrentState != MobState.Alive)
                continue;
            if (xform.MapUid != mapUid)
                continue;
            var pos = xform.LocalPosition;
            candidates.Add(new Vector2i((int)MathF.Round(pos.X), (int)MathF.Round(pos.Y)));
        }
        if (candidates.Count == 0)
            return null;
        return candidates[_random.Next(candidates.Count)];
    }
}
