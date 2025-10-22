using Robust.Shared.GameStates;

namespace Content.Shared._Friday31.Pennywise;

/// <summary>
/// Компонент для отслеживания состояния фазового прохода Пеннивайза
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class PennywisePhaseComponent : Component
{
    /// <summary>
    /// Включен ли режим прохода сквозь стены
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField]
    public bool IsPhasing = false;

    /// <summary>
    /// Время задержки между переключениями (в секундах)
    /// </summary>
    [DataField]
    public float Cooldown = 3f;

    /// <summary>
    /// Последнее время использования способности
    /// </summary>
    public TimeSpan LastToggleTime = TimeSpan.Zero;
}
