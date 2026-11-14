using Content.Server.GameTicking;
using Content.Server.Parallax;
using Content.Server.Popups;
using Content.Server.Procedural;
using Content.Shared.Station.Systems;
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
    [Dependency] private AnchorableSystem _anchorable = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private BiomeSystem _biome = default!;
    [Dependency] private DungeonSystem _dungeon = default!;
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private ILogManager _logManager = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private LinkedEntitySystem _link = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ITileDefinitionManager _tileDefinitionManager = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private UserInterfaceSystem _uiSystem = default!;

    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private EntityStorageSystem _entityStorageSystem = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private SharedBuckleSystem _buckle = default!;

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
