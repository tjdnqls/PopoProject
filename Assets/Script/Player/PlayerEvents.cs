using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 플레이어 관련 이벤트들을 관리하는 중앙 이벤트 시스템
/// </summary>
public static class PlayerEvents
{
    // === Movement Events ===
    public static event Action<Vector2> OnMovementChanged;
    public static event Action<bool> OnGroundedChanged;
    public static event Action OnJumped;
    public static event Action OnLanded;
    public static event Action<Vector2> OnVelocityChanged;

    // === Carry System Events ===
    public static event Action<bool> OnCarryStateChanged;
    public static event Action OnCarryStarted;
    public static event Action OnCarryEnded;
    public static event Action OnThrowStarted;
    public static event Action OnThrowEnded;

    // === Combat Events ===
    public static event Action OnAttackStarted;
    public static event Action OnAttackEnded;
    public static event Action<int> OnHealthChanged;
    public static event Action OnPlayerDied;
    public static event Action OnPlayerRevived;

    // === Animation Events ===
    public static event Action<string, bool> OnAnimationBoolChanged;
    public static event Action<string> OnAnimationTriggered;

    // === Audio Events ===
    public static event Action<string> OnSoundRequested;
    public static event Action OnFootstepRequested;

    // === Wall Interaction Events ===
    public static event Action<bool> OnWallContactChanged;
    public static event Action OnWallJumped;
    public static event Action<bool> OnCeilingStickChanged;

    // === Input Events ===
    public static event Action<bool> OnInputLocked;

    // === Movement Event Triggers ===
    public static void TriggerMovementChanged(Vector2 movement) => OnMovementChanged?.Invoke(movement);
    public static void TriggerGroundedChanged(bool grounded) => OnGroundedChanged?.Invoke(grounded);
    public static void TriggerJumped() => OnJumped?.Invoke();
    public static void TriggerLanded() => OnLanded?.Invoke();
    public static void TriggerVelocityChanged(Vector2 velocity) => OnVelocityChanged?.Invoke(velocity);

    // === Carry System Event Triggers ===
    public static void TriggerCarryStateChanged(bool isCarrying) => OnCarryStateChanged?.Invoke(isCarrying);
    public static void TriggerCarryStarted() => OnCarryStarted?.Invoke();
    public static void TriggerCarryEnded() => OnCarryEnded?.Invoke();
    public static void TriggerThrowStarted() => OnThrowStarted?.Invoke();
    public static void TriggerThrowEnded() => OnThrowEnded?.Invoke();

    // === Combat Event Triggers ===
    public static void TriggerAttackStarted() => OnAttackStarted?.Invoke();
    public static void TriggerAttackEnded() => OnAttackEnded?.Invoke();
    public static void TriggerHealthChanged(int newHealth) => OnHealthChanged?.Invoke(newHealth);
    public static void TriggerPlayerDied() => OnPlayerDied?.Invoke();
    public static void TriggerPlayerRevived() => OnPlayerRevived?.Invoke();

    // === Animation Event Triggers ===
    public static void TriggerAnimationBoolChanged(string paramName, bool value) => OnAnimationBoolChanged?.Invoke(paramName, value);
    public static void TriggerAnimationTriggered(string triggerName) => OnAnimationTriggered?.Invoke(triggerName);

    // === Audio Event Triggers ===
    public static void TriggerSoundRequested(string soundName) => OnSoundRequested?.Invoke(soundName);
    public static void TriggerFootstepRequested() => OnFootstepRequested?.Invoke();

    // === Wall Interaction Event Triggers ===
    public static void TriggerWallContactChanged(bool touching) => OnWallContactChanged?.Invoke(touching);
    public static void TriggerWallJumped() => OnWallJumped?.Invoke();
    public static void TriggerCeilingStickChanged(bool sticking) => OnCeilingStickChanged?.Invoke(sticking);

    // === Input Event Triggers ===
    public static void TriggerInputLocked(bool locked) => OnInputLocked?.Invoke(locked);

    // === Cleanup Method ===
    public static void ClearAllEvents()
    {
        OnMovementChanged = null;
        OnGroundedChanged = null;
        OnJumped = null;
        OnLanded = null;
        OnVelocityChanged = null;
        OnCarryStateChanged = null;
        OnCarryStarted = null;
        OnCarryEnded = null;
        OnThrowStarted = null;
        OnThrowEnded = null;
        OnAttackStarted = null;
        OnAttackEnded = null;
        OnHealthChanged = null;
        OnPlayerDied = null;
        OnPlayerRevived = null;
        OnAnimationBoolChanged = null;
        OnAnimationTriggered = null;
        OnSoundRequested = null;
        OnFootstepRequested = null;
        OnWallContactChanged = null;
        OnWallJumped = null;
        OnCeilingStickChanged = null;
        OnInputLocked = null;
    }
}
