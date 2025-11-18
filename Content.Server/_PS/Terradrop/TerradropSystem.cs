using Content.Server.GameTicking;
using Content.Server.Parallax;
using Content.Server.Popups;
using Content.Server.Procedural;
using Content.Server.Station.Systems;
using Content.Server.Storage.EntitySystems;
using Content.Shared._PS.Terradrop;
using Content.Shared.Buckle;
using Content.Shared.Construction.EntitySystems;
using Content.Shared.Damage.Systems;
using Content.Shared.Teleportation.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._PS.Terradrop;

public sealed partial class TerradropSystem : SharedTerradropSystem
{
    [Dependency] private readonly AnchorableSystem _anchorable = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly BiomeSystem _biome = default!;
    [Dependency] private readonly DungeonSystem _dungeon = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly ILogManager _logManager = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly LinkedEntitySystem _link = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDefinitionManager = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly EntityStorageSystem _entityStorageSystem = default!;
    [Dependency] private readonly DamageableSystem _damageableSystem = default!;
    [Dependency] private readonly SharedBuckleSystem _buckle = default!;

    public override void Initialize()
    {
        base.Initialize();

        InitializeConsole();
        InitializeMissionHandling();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        UpdateTerradropJobs();
    }

}
