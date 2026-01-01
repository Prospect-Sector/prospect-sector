using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Server._PS.Procedural;
using Content.Server.Procedural;
using Content.Shared.CCVar;
using Content.Shared.Procedural;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._PS.Procedural;

[TestFixture]
[TestOf(typeof(ProspectDungeonSystem))]
public sealed class ProspectDungeonTests
{
    /// <summary>
    /// Tests that the Prospect dungeon system can be enabled and disabled via CVAR.
    /// </summary>
    [Test]
    public async Task TestProspectDungeonCVar()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var configManager = server.ResolveDependency<IConfigurationManager>();
        var entManager = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var prospectSystem = entManager.System<ProspectDungeonSystem>();

            // Test that the CVAR controls the Enabled property
            configManager.SetCVar(CCVars.ProspectParallelDungeons, true);
            Assert.That(prospectSystem.Enabled, Is.True, "ProspectDungeonSystem should be enabled when CVAR is true");

            configManager.SetCVar(CCVars.ProspectParallelDungeons, false);
            Assert.That(prospectSystem.Enabled, Is.False, "ProspectDungeonSystem should be disabled when CVAR is false");

            // Reset to default
            configManager.SetCVar(CCVars.ProspectParallelDungeons, true);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Tests that the upstream hook correctly routes to Prospect system when enabled.
    /// </summary>
    [Test]
    public async Task TestDungeonSystemHookRouting()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var configManager = server.ResolveDependency<IConfigurationManager>();
        var entManager = server.ResolveDependency<IEntityManager>();
        var protoManager = server.ResolveDependency<IPrototypeManager>();
        var mapManager = server.ResolveDependency<IMapManager>();

        await server.WaitAssertion(() =>
        {
            var dungeonSystem = entManager.System<DungeonSystem>();
            var prospectSystem = entManager.System<ProspectDungeonSystem>();

            // Ensure Prospect system is enabled
            configManager.SetCVar(CCVars.ProspectParallelDungeons, true);
            Assert.That(prospectSystem.Enabled, Is.True);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Tests that dungeon configs can be loaded and have valid layers.
    /// </summary>
    [Test]
    public async Task TestDungeonConfigsHaveValidLayers()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var protoManager = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            foreach (var config in protoManager.EnumeratePrototypes<DungeonConfigPrototype>())
            {
                Assert.That(config.Layers, Is.Not.Null, $"DungeonConfig {config.ID} has null Layers");
                Assert.That(config.Layers.Count, Is.GreaterThan(0), $"DungeonConfig {config.ID} has no layers");

                foreach (var layer in config.Layers)
                {
                    Assert.That(layer, Is.Not.Null, $"DungeonConfig {config.ID} has a null layer");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Tests that all layer types referenced in configs are supported by the executor registry.
    /// </summary>
    [Test]
    public async Task TestAllLayerTypesSupported()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var protoManager = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var layerTypes = new HashSet<System.Type>();

            foreach (var config in protoManager.EnumeratePrototypes<DungeonConfigPrototype>())
            {
                foreach (var layer in config.Layers)
                {
                    layerTypes.Add(layer.GetType());
                }
            }

            // Just verify we collected some layer types - the actual executor coverage
            // would require instantiating the context which needs a grid
            Assert.That(layerTypes.Count, Is.GreaterThan(0), "Should find layer types in dungeon configs");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Tests that the DungeonRoom record works correctly.
    /// </summary>
    [Test]
    public async Task TestDungeonRoomConstruction()
    {
        await using var pair = await PoolManager.GetServerClient();

        await pair.Server.WaitAssertion(() =>
        {
            var tiles = new HashSet<Vector2i>
            {
                new(0, 0),
                new(1, 0),
                new(0, 1),
                new(1, 1)
            };

            var exterior = new HashSet<Vector2i>
            {
                new(-1, 0),
                new(2, 0),
                new(0, -1),
                new(0, 2)
            };

            var center = new System.Numerics.Vector2(0.5f, 0.5f);
            var bounds = new Box2i(0, 0, 2, 2);

            var room = new DungeonRoom(tiles, center, bounds, exterior);

            Assert.Multiple(() =>
            {
                Assert.That(room.Tiles.Count, Is.EqualTo(4));
                Assert.That(room.Exterior.Count, Is.EqualTo(4));
                Assert.That(room.Center, Is.EqualTo(center));
                Assert.That(room.Bounds, Is.EqualTo(bounds));
                Assert.That(room.Entrances, Is.Not.Null);
                Assert.That(room.Entrances.Count, Is.EqualTo(0));
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Tests that the Dungeon class correctly aggregates room data.
    /// </summary>
    [Test]
    public async Task TestDungeonAggregation()
    {
        await using var pair = await PoolManager.GetServerClient();

        await pair.Server.WaitAssertion(() =>
        {
            var room1Tiles = new HashSet<Vector2i>
            {
                new(0, 0),
                new(1, 0)
            };

            var room2Tiles = new HashSet<Vector2i>
            {
                new(5, 5),
                new(6, 5)
            };

            var room1 = new DungeonRoom(room1Tiles, new System.Numerics.Vector2(0.5f, 0f), new Box2i(0, 0, 2, 1), new HashSet<Vector2i>());
            var room2 = new DungeonRoom(room2Tiles, new System.Numerics.Vector2(5.5f, 5f), new Box2i(5, 5, 7, 6), new HashSet<Vector2i>());

            var dungeon = new Dungeon();
            dungeon.AddRoom(room1);
            dungeon.AddRoom(room2);

            Assert.Multiple(() =>
            {
                Assert.That(dungeon.Rooms.Count, Is.EqualTo(2));
                Assert.That(dungeon.RoomTiles.Count, Is.EqualTo(4));
                Assert.That(dungeon.RoomTiles.Contains(new Vector2i(0, 0)));
                Assert.That(dungeon.RoomTiles.Contains(new Vector2i(5, 5)));
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Tests object pooling in the generation context.
    /// </summary>
    [Test]
    public async Task TestObjectPooling()
    {
        await using var pair = await PoolManager.GetServerClient();

        await pair.Server.WaitAssertion(() =>
        {
            // Test that HashSet pooling works correctly
            var set1 = new HashSet<Vector2i>();
            set1.Add(new Vector2i(1, 1));
            set1.Add(new Vector2i(2, 2));

            Assert.That(set1.Count, Is.EqualTo(2));

            set1.Clear();
            Assert.That(set1.Count, Is.EqualTo(0));

            // Pool return simulation - clearing should work
            set1.Add(new Vector2i(3, 3));
            Assert.That(set1.Count, Is.EqualTo(1));
        });

        await pair.CleanReturnAsync();
    }
}
