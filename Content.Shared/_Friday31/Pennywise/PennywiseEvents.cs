using Content.Shared.Actions;

namespace Content.Shared._Friday31.Pennywise;

public sealed partial class PennywiseChameleonEvent : EntityTargetActionEvent;

/// <summary>
/// Событие для переключения способности прохода сквозь стены
/// </summary>
public sealed partial class PennywisePhaseToggleEvent : InstantActionEvent;
