using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
[DisallowMultipleComponent]
public class TrapTouchDamage : MonoBehaviour
{
    [Header("Who to hit")]
    [SerializeField] private LayerMask playerMask = 0;   // Player, Player1, Player2 등 복수 레이어 포함

    [Header("Damage")]
    [SerializeField] private int damageAmount = 2;       // 닿으면 깎일 HP
    [SerializeField] private bool onEnterOnly = true;    // true: 접촉 순간 1회만, false: 머무는 동안 주기적
    [SerializeField] private float rehitCooldown = 0.2f; // Enter 모드 중복 방지 쿨다운

    [Header("Stay damage (if onEnterOnly=false)")]
    [SerializeField] private float stayInterval = 0.5f;  // Stay 모드일 때 반복 간격

    [Header("Debug")]
    [SerializeField] private bool logDebug = false;

    private readonly Dictionary<int, float> _nextAllowedTime = new Dictionary<int, float>();

    private void Awake()
    {
        var col = GetComponent<Collider2D>();
        if (!col.isTrigger && logDebug)
            Debug.LogWarning($"[TrapTouchDamage] {name} collider is not Trigger. 충돌형도 동작하나 Trigger 권장.");
    }

    private void OnEnable() => _nextAllowedTime.Clear();

    private void OnTriggerEnter2D(Collider2D other) { TryDamage(other, enterPhase: true); }
    private void OnTriggerStay2D(Collider2D other) { TryDamage(other, enterPhase: false); }
    private void OnCollisionEnter2D(Collision2D c) { TryDamage(c.collider, enterPhase: true); }
    private void OnCollisionStay2D(Collision2D c) { TryDamage(c.collider, enterPhase: false); }

    private void TryDamage(Collider2D other, bool enterPhase)
    {
        if (!other) return;

        // 플레이어 레이어 필터 (복수 레이어 지원)
        if ((playerMask.value & (1 << other.gameObject.layer)) == 0)
            return;

        // 루트 기준으로 중복 방지
        var root = other.attachedRigidbody ? other.attachedRigidbody.transform.root : other.transform.root;
        if (!root) return;

        int id = root.GetInstanceID();

        if (onEnterOnly && !enterPhase) return;

        float now = Time.time;
        float neededGap = onEnterOnly ? rehitCooldown : stayInterval;

        if (_nextAllowedTime.TryGetValue(id, out float nextTime) && now < nextTime)
            return; // 아직 쿨다운

        ApplyDamage(root, other);
        _nextAllowedTime[id] = now + neededGap;
    }

    private void ApplyDamage(Transform root, Collider2D hitCol)
    {
        // 1) 표준 인터페이스
        var dmgIf = root.GetComponentInChildren<global::IDamageable>();
        if (dmgIf != null)
        {
            Vector2 hitPoint = hitCol.bounds.center;
            Vector2 hitNormal = ((Vector2)root.position - (Vector2)transform.position).normalized;
            if (hitNormal.sqrMagnitude < 0.0001f) hitNormal = Vector2.up;

            dmgIf.TakeDamage(damageAmount, hitPoint, hitNormal);
            if (logDebug) Debug.Log($"[TrapTouchDamage] {root.name} IDamageable -{damageAmount}");
            return;
        }

        // 2) Player1/2 직접 지원(폴백)
        var p1 = root.GetComponentInChildren<Player1HP>();
        if (p1 != null) { p1.TakeDamage(damageAmount); if (logDebug) Debug.Log($"[TrapTouchDamage] {root.name} Player1HP -{damageAmount}"); return; }

        var p2 = root.GetComponentInChildren<Player2HP>();
        if (p2 != null) { p2.TakeDamage(damageAmount); if (logDebug) Debug.Log($"[TrapTouchDamage] {root.name} Player2HP -{damageAmount}"); return; }

        // 3) 최종 폴백
        root.SendMessage("TakeDamage", damageAmount, SendMessageOptions.DontRequireReceiver);
        root.SendMessage("OnHit", damageAmount, SendMessageOptions.DontRequireReceiver);
        if (logDebug) Debug.Log($"[TrapTouchDamage] {root.name} SendMessage -{damageAmount}");
    }
}
