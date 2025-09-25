using System;
using System.Reflection;
using UnityEngine;

public interface IInvincible
{
    bool Invincible { get; set; }
}

[DisallowMultipleComponent]
public class GodModeService : MonoBehaviour
{
    public static GodModeService Instance { get; private set; }

    [Header("Targets (HP 컴포넌트 자체를 참조)")]
    [SerializeField] private MonoBehaviour player1HP;
    [SerializeField] private MonoBehaviour player2HP;

    [Header("State")]
    [SerializeField] private bool active;
    [SerializeField] private float timerRemaining = -1f; // <0 이면 무제한

    private void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        ApplyToAll();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (!active) return;
        if (timerRemaining > 0f)
        {
            timerRemaining -= Time.unscaledDeltaTime;
            if (timerRemaining <= 0f) Set(false);
        }
    }

    // --------- Public API ----------
    public static void SetGod(bool on, float durationSeconds = -1f)
        => Instance?.Set(on, durationSeconds);

    public static void ToggleGod()
        => Instance?.Set(!Instance.active, -1f);

    public bool IsActive => active;

    public void Set(bool on, float durationSeconds = -1f)
    {
        active = on;
        timerRemaining = (on && durationSeconds > 0f) ? durationSeconds : -1f;
        ApplyToAll();
        Debug.Log($"[GodMode] {(active ? "ON" : "OFF")}" + (timerRemaining > 0f ? $" ({timerRemaining:0.#}s)" : ""));
    }

    private void ApplyToAll()
    {
        ApplyTo(player1HP);
        ApplyTo(player2HP);
    }

    private void ApplyTo(MonoBehaviour hp)
    {
        if (!hp) return;

        // 1) IInvincible 직접 지원
        if (hp is IInvincible inv)
        {
            inv.Invincible = active;
            return;
        }

        var t = hp.GetType();

        // 2) SetInvincible(bool)
        var m = t.GetMethod("SetInvincible", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(bool) }, null);
        if (m != null) { m.Invoke(hp, new object[] { active }); return; }

        // 3) 프로퍼티
        var p = t.GetProperty("IsInvincible", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
             ?? t.GetProperty("Invincible", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (p != null && p.CanWrite) { p.SetValue(hp, active); return; }

        // 4) 필드
        var f = t.GetField("isInvincible", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
             ?? t.GetField("Invincible", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
             ?? t.GetField("IsInvincible", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f != null) { f.SetValue(hp, active); return; }

        // 5) 실패: 안내만
        Debug.LogWarning($"[GodMode] '{hp.name}'에서 무적 플래그를 찾지 못했습니다. " +
                         "해당 HP 스크립트에 SetInvincible(bool) 또는 bool IsInvincible/Invincible을 추가하거나, IInvincible을 구현해 주세요.");
    }
}
