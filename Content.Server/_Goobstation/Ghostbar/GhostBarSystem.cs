using Robust.Server.GameObjects;
using Content.Server.Clothing.Systems; // Einstein Engines
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Server.Players.PlayTimeTracking; // Einstein Engines
using Content.Server.Station.Systems;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Content.Shared.Ghost;
using Content.Server._Goobstation.Ghostbar.Components;
using Content.Server.Mind;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Roles;
using Content.Server.Antag.Components;
using Content.Server.Traits; // Einstein Engines
using Content.Shared.Mindshield.Components;
using Content.Shared.Players;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Utility;


namespace Content.Server._Goobstation.Ghostbar;

public sealed class GhostBarSystem : EntitySystem
{
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly MapLoaderSystem _mapLoader = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly StationSpawningSystem _spawningSystem = default!;
    [Dependency] private readonly MindSystem _mindSystem = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;
    // Einstein Engines start
    [Dependency] private readonly PlayTimeTrackingManager _playTimeTracking = default!;
    [Dependency] private readonly LoadoutSystem _loadout = default!;
    [Dependency] private readonly TraitSystem _trait = default!;
    // Einstein Engines end

    [ValidatePrototypeId<JobPrototype>] // Einstein Engines - validate job prototypes
    private static readonly List<ProtoId<JobPrototype>> _jobComponents = new()
    {
        "Passenger", "Bartender", "Botanist", "Chef", "Janitor"
    };

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStart);
        SubscribeNetworkEvent<GhostBarSpawnEvent>(SpawnPlayer);
        SubscribeLocalEvent<GhostBarPlayerComponent, MindRemovedMessage>(OnPlayerGhosted);
    }

    private static readonly ResPath MapPath = new ResPath("Maps/_Goobstation/Nonstations/ghostbar.yml");

    private void OnRoundStart(RoundStartingEvent ev)
    {
        var options = new DeserializationOptions { InitializeMaps = true, };
        _mapLoader.TryLoadMap(MapPath, out _, out _, options);
    }

    public void SpawnPlayer(GhostBarSpawnEvent msg, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;

        if (!_mindSystem.TryGetMind(player, out var mindId, out var mind))
        {
            Log.Warning($"Failed to find mind for player {player.Name}.");
            return;
        }

        if (!_entityManager.HasComponent<GhostComponent>(player.AttachedEntity))
        {
            Log.Warning($"User {player.Name} tried to spawn at ghost bar without being a ghost.");
            return;
        }

        var spawnPoints = new List<EntityCoordinates>();
        var query = EntityQueryEnumerator<GhostBarSpawnComponent>();
        while (query.MoveNext(out var ent, out _))
        {
            spawnPoints.Add(_entityManager.GetComponent<TransformComponent>(ent).Coordinates);
        }

        if (spawnPoints.Count == 0)
        {
            Log.Warning("No spawn points found for ghost bar.");
            return;
        }

        var data = player.ContentData();

        if (data == null)
        {
            Log.Warning($"ContentData was null when trying to spawn {player.Name} in ghost bar.");
            return;
        }

        var randomSpawnPoint = _random.Pick(spawnPoints);
        var randomJob = _random.Pick(_jobComponents);
        var profile = _ticker.GetPlayerProfile(args.SenderSession);
        var mobUid = _spawningSystem.SpawnPlayerMob(randomSpawnPoint, randomJob, profile, null);

        // Einstein Engines start - apply loadouts and traits
        var playTimes = _playTimeTracking.GetTrackerTimes(player);
        var whitelisted = player.ContentData()?.Whitelisted ?? false;

        _loadout.ApplyCharacterLoadout(
            mobUid,
            randomJob,
            profile,
            playTimes,
            whitelisted
        );
        _trait.ApplyTraits(
            mobUid,
            randomJob,
            profile,
            playTimes,
            whitelisted,
            punishCheater: false
        );
        // Einstein Engines end - apply loadouts and traits

        _entityManager.EnsureComponent<GhostBarPlayerComponent>(mobUid);
        _entityManager.EnsureComponent<MindShieldComponent>(mobUid);
        _entityManager.EnsureComponent<AntagImmuneComponent>(mobUid);
        _entityManager.EnsureComponent<IsDeadICComponent>(mobUid);

        // wwdp edit start
        if(player.AttachedEntity != null)
            _entityManager.GetComponent<GhostBarPlayerComponent>(mobUid).TimeOfDeath = _entityManager.GetComponent<GhostComponent>((Robust.Shared.GameObjects.EntityUid) player.AttachedEntity).TimeOfDeath;
        if(mind.TimeOfDeath.HasValue)
            _entityManager.GetComponent<GhostBarPlayerComponent>(mobUid).TimeOfDeath = mind.TimeOfDeath.Value;
        // wwdp edit end

        if (mind.Objectives.Count == 0)
            _mindSystem.WipeMind(player);
        mindId = _mindSystem.CreateMind(data.UserId, profile.Name).Owner;
        _mindSystem.TransferTo(mindId, mobUid, true);
    }

    private void OnPlayerGhosted(EntityUid uid, GhostBarPlayerComponent component, MindRemovedMessage args)
    {
        // wwdp start
        _entityManager.GetComponent<MindComponent>(args.Mind).TimeOfDeath = _entityManager.GetComponent<GhostBarPlayerComponent>(uid).TimeOfDeath;
        // wwdp end
        _entityManager.DeleteEntity(uid);
    }
}

