using System.Linq;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.Administration.Logs;
using Content.Shared.Body.Systems;
using Content.Shared.Database;
using Content.Shared.Gravity;
using Content.Shared.Physics;
using Content.Shared.Movement.Pulling.Events;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Shared.Throwing
{
    /// <summary>
    ///     Handles throwing landing and collisions.
    /// </summary>
    public sealed class ThrownItemSystem : EntitySystem
    {
        [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
        [Dependency] private readonly IGameTiming _gameTiming = default!;
        [Dependency] private readonly SharedBroadphaseSystem _broadphase = default!;
        [Dependency] private readonly FixtureSystem _fixtures = default!;
        [Dependency] private readonly SharedPhysicsSystem _physics = default!;
        [Dependency] private readonly SharedGravitySystem _gravity = default!;
        [Dependency] private readonly SharedBodySystem _body = default!;

        private const string ThrowingFixture = "throw-fixture";

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<ThrownItemComponent, MapInitEvent>(OnMapInit);
            SubscribeLocalEvent<ThrownItemComponent, PhysicsSleepEvent>(OnSleep);
            SubscribeLocalEvent<ThrownItemComponent, StartCollideEvent>(HandleCollision);
            SubscribeLocalEvent<ThrownItemComponent, PreventCollideEvent>(PreventCollision);
            SubscribeLocalEvent<ThrownItemComponent, ThrownEvent>(ThrowItem);

            SubscribeLocalEvent<PullStartedMessage>(HandlePullStarted);
        }

        private void OnMapInit(EntityUid uid, ThrownItemComponent component, MapInitEvent args)
        {
            component.ThrownTime ??= _gameTiming.CurTime;
        }

        private void ThrowItem(EntityUid uid, ThrownItemComponent component, ref ThrownEvent @event)
        {
            if (!EntityManager.TryGetComponent(uid, out FixturesComponent? fixturesComponent) ||
                fixturesComponent.Fixtures.Count != 1 ||
                !TryComp<PhysicsComponent>(uid, out var body))
            {
                return;
            }

            var fixture = fixturesComponent.Fixtures.Values.First();
            var shape = fixture.Shape;
            _fixtures.TryCreateFixture(uid, shape, ThrowingFixture, hard: false, collisionMask: (int) CollisionGroup.ThrownItem, manager: fixturesComponent, body: body);
        }

        private void HandleCollision(EntityUid uid, ThrownItemComponent component, ref StartCollideEvent args)
        {
            if (!args.OtherFixture.Hard)
                return;

            if (args.OtherEntity == component.Thrower)
                return;

            // WD EDIT START
            if (component.Processed.Contains(args.OtherEntity))
                return;
            // WD EDIT END

            ThrowCollideInteraction(component, args.OurEntity, args.OtherEntity);
            component.Processed.Add(args.OtherEntity); // WD EDIT
        }

        private void PreventCollision(EntityUid uid, ThrownItemComponent component, ref PreventCollideEvent args)
        {
            if (args.OtherEntity == component.Thrower)
            {
                args.Cancelled = true;
            }
        }

        private void OnSleep(EntityUid uid, ThrownItemComponent thrownItem, ref PhysicsSleepEvent @event)
        {
            StopThrow(uid, thrownItem);
        }

        private void HandlePullStarted(PullStartedMessage message)
        {
            // TODO: this isn't directed so things have to be done the bad way
            if (EntityManager.TryGetComponent(message.PulledUid, out ThrownItemComponent? thrownItemComponent))
                StopThrow(message.PulledUid, thrownItemComponent);
        }

        public void StopThrow(EntityUid uid, ThrownItemComponent thrownItemComponent)
        {
            if (TryComp<PhysicsComponent>(uid, out var physics))
            {
                _physics.SetBodyStatus(uid, physics, BodyStatus.OnGround);

                if (physics.Awake)
                    _broadphase.RegenerateContacts((uid, physics));
            }

            if (EntityManager.TryGetComponent(uid, out FixturesComponent? manager))
            {
                var fixture = _fixtures.GetFixtureOrNull(uid, ThrowingFixture, manager: manager);

                if (fixture != null)
                {
                    _fixtures.DestroyFixture(uid, ThrowingFixture, fixture, manager: manager);
                }
            }

            EntityManager.EventBus.RaiseLocalEvent(uid, new StopThrowEvent { User = thrownItemComponent.Thrower }, true);
            EntityManager.RemoveComponent<ThrownItemComponent>(uid);
            thrownItemComponent.Processed.Clear(); // WD EDIT
        }

        public void LandComponent(EntityUid uid, ThrownItemComponent thrownItem, PhysicsComponent physics, bool playSound, bool forceLand = false)
        {
            if (thrownItem.Landed || thrownItem.Deleted || _gravity.IsWeightless(uid) && !forceLand || Deleted(uid))
                return;

            thrownItem.Landed = true;

            // Assume it's uninteresting if it has no thrower. For now anyway.
            if (thrownItem.Thrower is not null)
                _adminLogger.Add(LogType.Landed, LogImpact.Low, $"{ToPrettyString(uid):entity} thrown by {ToPrettyString(thrownItem.Thrower.Value):thrower} landed.");

            _broadphase.RegenerateContacts((uid, physics));
            var landEvent = new LandEvent(thrownItem.Thrower, playSound);
            RaiseLocalEvent(uid, ref landEvent);
        }

        /// <summary>
        ///     Raises collision events on the thrown and target entities.
        /// </summary>
        public void ThrowCollideInteraction(ThrownItemComponent component, EntityUid thrown, EntityUid target)
        {
            if (HasComp<ThrownItemImmuneComponent>(target))
                return;

            if (component.Thrower is not null)
                _adminLogger.Add(LogType.ThrowHit, LogImpact.Low,
                    $"{ToPrettyString(thrown):thrown} thrown by {ToPrettyString(component.Thrower.Value):thrower} hit {ToPrettyString(target):target}.");

            //WWDP EDIT START
            TryComp<TargetingComponent>(component.Thrower, out var targetingComponent);

            var targetPart =  targetingComponent?.Target ?? _body.GetRandomBodyPart(target);
            //WWDP EDIT END

            if (component.Thrower is not null)// Nyano - Summary: Gotta check if there was a thrower.
                RaiseLocalEvent(target, new ThrowHitByEvent(component.Thrower.Value, thrown, target, component, targetPart), true); // Nyano - Summary: Gotta update for who threw it.
            else
                RaiseLocalEvent(target, new ThrowHitByEvent(null, thrown, target, component, targetPart), true); // Nyano - Summary: No thrower.
            RaiseLocalEvent(thrown, new ThrowDoHitEvent(thrown, target, component, targetPart), true);
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);

            var query = EntityQueryEnumerator<ThrownItemComponent, PhysicsComponent>();
            while (query.MoveNext(out var uid, out var thrown, out var physics))
            {
                if (thrown.LandTime <= _gameTiming.CurTime)
                {
                    LandComponent(uid, thrown, physics, thrown.PlayLandSound);
                }

                var stopThrowTime = thrown.LandTime ?? thrown.ThrownTime;
                if (stopThrowTime <= _gameTiming.CurTime)
                {
                    StopThrow(uid, thrown);
                }
            }
        }
    }
}
