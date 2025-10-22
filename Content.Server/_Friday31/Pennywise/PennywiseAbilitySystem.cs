using Content.Server.Humanoid;
using Content.Server.Polymorph.Components;
using Content.Server.Polymorph.Systems;
using Content.Shared.Polymorph.Components;
using Content.Shared._Friday31.Pennywise;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Humanoid;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.StatusIcon.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Content.Shared.Physics;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;
using Content.Server.GameTicking;
using Content.Shared.Eye;
using Robust.Server.GameObjects;
using Content.Shared.Revenant;
using System.Linq;

namespace Content.Server._Friday31.Pennywise;

public sealed class PennywiseAbilitySystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly PolymorphSystem _polymorph = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly MobThresholdSystem _mobThreshold = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly HumanoidAppearanceSystem _humanoid = default!;
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly VisibilitySystem _visibility = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PennywiseAbilityComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<PennywiseAbilityComponent, PennywiseChameleonEvent>(OnChameleonDisguise);
        SubscribeLocalEvent<PennywiseAbilityComponent, PennywisePhaseToggleEvent>(OnPhaseToggle);
    }

    private void OnMapInit(EntityUid uid, PennywiseAbilityComponent component, MapInitEvent args)
    {
        _actions.AddAction(uid, ref component.ChameleonActionEntity, component.ChameleonAction);
        _actions.AddAction(uid, ref component.PhaseToggleActionEntity, component.PhaseToggleAction);
        
        // Добавляем компонент фазового состояния
        EnsureComp<PennywisePhaseComponent>(uid);
    }

    private void OnChameleonDisguise(EntityUid uid, PennywiseAbilityComponent component, PennywiseChameleonEvent args)
    {
        if (args.Handled)
            return;

        var target = args.Target;

        // Проверка возможности использования способности
        if (!_actionBlocker.CanInteract(uid, target))
            return;

        if (HasComp<ChameleonDisguiseComponent>(target))
            return;

        if (!_proto.TryIndex<EntityPrototype>("ChameleonProjector", out var projectorProto))
            return;

        if (!projectorProto.TryGetComponent<ChameleonProjectorComponent>(out var projComp))
            return;

        var polymorphConfig = projComp.Polymorph;
        var minHealth = projComp.MinHealth;
        var maxHealth = projComp.MaxHealth;
        var noRotAction = projComp.NoRotAction;
        var anchorAction = projComp.AnchorAction;

        if (_polymorph.PolymorphEntity(uid, polymorphConfig) is not {} disguise)
            return;

        var targetMeta = MetaData(target);
        _meta.SetEntityName(disguise, targetMeta.EntityName);
        _meta.SetEntityDescription(disguise, targetMeta.EntityDescription);

        var comp = EnsureComp<ChameleonDisguiseComponent>(disguise);
        comp.SourceEntity = target;
        comp.SourceProto = Prototype(target)?.ID;
        Dirty(disguise, comp);

        RemComp<StatusIconComponent>(disguise);

        _appearance.CopyData(target, disguise);
        
        // Клонируем внешность гуманоидов
        if (TryComp<HumanoidAppearanceComponent>(target, out var sourceHumanoid))
        {
            // Добавляем компонент гуманоида на маскировку, если его нет
            var targetHumanoid = EnsureComp<HumanoidAppearanceComponent>(disguise);
            _humanoid.CloneAppearance(target, disguise, sourceHumanoid, targetHumanoid);
        }

        var mass = CompOrNull<PhysicsComponent>(target)?.Mass ?? 0f;
        if (TryComp<MobThresholdsComponent>(disguise, out var thresholds))
        {
            var playerMax = _mobThreshold.GetThresholdForState(uid, MobState.Dead).Float();
            var max = playerMax == 0f ? maxHealth : Math.Max(maxHealth, playerMax);
            var health = Math.Clamp(mass, minHealth, maxHealth);
            
            _mobThreshold.SetMobStateThreshold(disguise, health, MobState.Critical, thresholds);
            _mobThreshold.SetMobStateThreshold(disguise, max, MobState.Dead, thresholds);
        }

        _actions.AddAction(disguise, noRotAction);
        _actions.AddAction(disguise, anchorAction);

        args.Handled = true;
    }

    private void OnPhaseToggle(EntityUid uid, PennywiseAbilityComponent component, PennywisePhaseToggleEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<PennywisePhaseComponent>(uid, out var phaseComp))
            return;

        // Проверяем кулдаун
        var currentTime = _timing.CurTime;
        if ((currentTime - phaseComp.LastToggleTime).TotalSeconds < phaseComp.Cooldown)
            return;

        // Проверяем, что у нас есть компонент физики
        if (!TryComp<FixturesComponent>(uid, out var fixtures) || fixtures.FixtureCount < 1)
            return;

        phaseComp.IsPhasing = !phaseComp.IsPhasing;
        phaseComp.LastToggleTime = currentTime;

        var fixture = fixtures.Fixtures.First();

        if (phaseComp.IsPhasing)
        {
            // Включаем проход сквозь стены
            _physics.SetHard(uid, fixture.Value, false, fixtures);
            _physics.SetCollisionMask(uid, fixture.Key, fixture.Value, (int)CollisionGroup.GhostImpassable, fixtures);
            _physics.SetCollisionLayer(uid, fixture.Key, fixture.Value, 0, fixtures);
            
            // Делаем полупрозрачным
            if (TryComp<VisibilityComponent>(uid, out var visibility))
            {
                _visibility.AddLayer((uid, visibility), (int)VisibilityFlags.Ghost, false);
                _visibility.RefreshVisibility(uid, visibility);
            }
            
            // Визуальный эффект
            _appearance.SetData(uid, RevenantVisuals.Corporeal, false);
        }
        else
        {
            // Выключаем проход сквозь стены
            _physics.SetHard(uid, fixture.Value, true, fixtures);
            _physics.SetCollisionMask(uid, fixture.Key, fixture.Value, (int)CollisionGroup.SmallMobMask, fixtures);
            _physics.SetCollisionLayer(uid, fixture.Key, fixture.Value, (int)CollisionGroup.SmallMobLayer, fixtures);
            
            // Делаем полностью видимым
            if (TryComp<VisibilityComponent>(uid, out var visibility))
            {
                _visibility.RemoveLayer((uid, visibility), (int)VisibilityFlags.Ghost, false);
                _visibility.RefreshVisibility(uid, visibility);
            }
            
            // Визуальный эффект
            _appearance.SetData(uid, RevenantVisuals.Corporeal, true);
        }

        args.Handled = true;
    }
}
