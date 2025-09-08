using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(Rigidbody2D))]
[DisallowMultipleComponent]
public class MonsterKillerTrigger2D : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private string monsterLayerName = "Monster"; // 감지 대상 레이어
    [SerializeField] private float killDelay = 3.0f;              // (최후수단) 감지 후 제거 딜레이
    [SerializeField] private bool targetRootWithRigidbody = true; // Rigidbody 루트로 판정할지

    [Header("Detection Modes")]
    [SerializeField] private bool killOnEnter = true;             // Enter 시 처리
    [SerializeField] private bool killOnStay = true;              // Stay 시 처리(이미 겹침 보완)
    [SerializeField] private bool scanOnEnableOnce = true;        // 활성화 직후 1회 스캔

    [Header("Attack Window (optional)")]
    [Tooltip("공격 윈도우 중에만 처형되도록 제한합니다.")]
    [SerializeField] private bool requireAttackWindow = false;
    private bool isAttackWindow = true; // requireAttackWindow=false면 항상 true로 취급

    [Header("Animation (SELF)")]
    // 이 스크립트가 붙은 오브젝트(무기/트랩 등)의 연출용.
    // 몬스터의 Death는 타겟 쪽 FSM/Health로 통지합니다.
    public SpriteAnimationManager anim; // Idle / Run / AttackStart / Attack / Hit / Death
    [SerializeField] private bool playSelfAnimOnKill = false; // 처형 성공 시 자기 애니 재생할지

    [Header("Damage Call")]
    [Tooltip("IDamageable에 전달할 데미지 값 (대부분 1이면 충분, Monster FSM은 수치와 무관하게 즉사 처리 가능)")]
    [SerializeField] private int damageAmount = 1;

    [Header("On Hit: Change Layer")]
    [Tooltip("피격이 인정되면 몬스터 레이어를 변경합니다.")]
    [SerializeField] private bool changeLayerOnHit = true;
    [SerializeField] private string newLayerNameOnHit = "Default";
    [SerializeField] private bool changeWholeHierarchy = true; // 자식까지 모두 변경

    private int monsterLayer = -1;
    private int newLayerOnHit = 0; // Default(보통 0). 이름 못 찾으면 0 사용.
    private Rigidbody2D rb;
    private Collider2D col;

    // 중복 처리 방지(동일 대상 다중 호출 억제)
    private readonly HashSet<int> _scheduled = new HashSet<int>();

    // Overlap 콜렉션(할당 최소화)
    private static readonly Collider2D[] _overlapBuf = new Collider2D[16];
    private ContactFilter2D _filter;

    void Awake()
    {
        monsterLayer = LayerMask.NameToLayer(monsterLayerName);
        newLayerOnHit = LayerMask.NameToLayer(newLayerNameOnHit);
        if (newLayerOnHit < 0) newLayerOnHit = 0; // 이름 못 찾으면 Default(0)

        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();

        // 트리거 권장 세팅
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        col.isTrigger = true;

        if (monsterLayer < 0)
            Debug.LogWarning($"[MonsterKillerTrigger2D] Layer '{monsterLayerName}' not found.");

        // 몬스터 레이어만 필터링 (유효할 때만)
        _filter = new ContactFilter2D { useTriggers = true };
        if (monsterLayer >= 0)
        {
            _filter.useLayerMask = true;
            _filter.layerMask = 1 << monsterLayer;
        }
        else
        {
            _filter.useLayerMask = false; // 잘못된 레이어면 필터 비활성(실행은 되지만 경고만)
        }

        // 공격 윈도우 초기화
        isAttackWindow = !requireAttackWindow || isAttackWindow;
    }

    void OnEnable()
    {
        _scheduled.Clear();
        rb?.WakeUp();

        // 활성화 시 이미 겹쳐 있던 대상 즉시 처리(히트박스 켜지는 공격에 유용)
        if (scanOnEnableOnce && (!requireAttackWindow || isAttackWindow))
        {
            // Unity 6.1: Collider2D.OverlapCollider 사용
            int count = col.Overlap(_filter, _overlapBuf);
            for (int i = 0; i < count; i++)
            {
                HandleCollider(_overlapBuf[i]);
            }
        }
    }

    // 공격 윈도우 제어: 애니메이션 이벤트에서 호출
    public void AttackWindowBegin() { isAttackWindow = true; }
    public void AttackWindowEnd() { isAttackWindow = false; }

    private bool PassesWindow() => !requireAttackWindow || isAttackWindow;

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!killOnEnter) return;
        HandleCollider(other);
    }

    void OnTriggerStay2D(Collider2D other)
    {
        if (!killOnStay) return;
        HandleCollider(other);
    }

    private void HandleCollider(Collider2D other)
    {
        if (!PassesWindow() || monsterLayer < 0 || other == null) return;

        // Rigidbody 루트를 대상으로 할지 여부
        GameObject victimGO = (targetRootWithRigidbody && other.attachedRigidbody)
            ? other.attachedRigidbody.gameObject
            : other.gameObject;

        if (victimGO.layer != monsterLayer) return;

        int id = victimGO.GetInstanceID();
        if (_scheduled.Contains(id)) return; // 중복 방지

        // ★ 피격 인정: 즉시 레이어 변경 (Monster → Default 등)
        if (changeLayerOnHit)
        {
            CameraShaker.Shake(1f, 0.2f);
            ChangeLayerOnHit(victimGO);
        }
            

        bool killed = TryKillVictim(victimGO);

        if (killed)
        {
            if (playSelfAnimOnKill) PlayOnceSelf("Hit", "Death");
            _scheduled.Add(id);
        }
        else
        {
            // 최후수단: 상대가 IDamageable/OnHit를 지원하지 않을 때 - 연출 + 지연 제거
            TryPlayTargetDeathAnimFallback(victimGO);
            ScheduleFallbackDestroy(victimGO, killDelay);
            _scheduled.Add(id);
        }
    }

    // === 핵심: 몬스터쪽 Death 시퀀스 호출 ===
    private bool TryKillVictim(GameObject victimGO)
    {
        // 1) 같은 루트 체인에서 IDamageable 찾기
        var damageable = victimGO.GetComponentInParent<IDamageable>();
        if (damageable != null)
        {
            // 방향/접점이 필요 없으면 오버로드(1개 인자)도 허용되도록 호출
            try
            {
                damageable.TakeDamage(damageAmount, transform.position, Vector2.zero);
            }
            catch
            {
                // 인터페이스 구현이 단일 인자만 가진 커스텀일 수도 있음 → 백업
                victimGO.SendMessage("TakeDamage", damageAmount, SendMessageOptions.DontRequireReceiver);
            }
            return true;
        }

        // 2) SendMessage 백업 (우리 MonsterABPatrolFSM은 OnHit(int) 지원)
        victimGO.SendMessage("OnHit", damageAmount, SendMessageOptions.DontRequireReceiver);

        // 성공 여부를 알 수 없으므로 false 반환 → 최후수단 흐름으로
        return false;
    }

    // === 최후수단: 타겟 쪽 애니만 재생 시도(있으면) ===
    private void TryPlayTargetDeathAnimFallback(GameObject victimGO)
    {
        var targetAnim = victimGO.GetComponentInChildren<SpriteAnimationManager>();
        if (targetAnim != null)
        {
            if (targetAnim.HasClip("Hit"))
                targetAnim.PlayOnce("Hit", "Death", true);
            else if (targetAnim.HasClip("Death"))
                targetAnim.PlayOnce("Death", null, true);
        }
    }

    // === 최후수단: 지연 파괴 ===
    private void ScheduleFallbackDestroy(GameObject victimGO, float delay)
    {
        var dd = victimGO.GetComponent<DelayedDestroy>();
        if (!dd) dd = victimGO.AddComponent<DelayedDestroy>();
        dd.Schedule(delay);
    }

    // === 자기 자신(무기/트랩) 연출용 ===
    private void PlayOnceSelf(string key, string fallback = null, bool forceRestart = true)
    {
        if (anim == null || string.IsNullOrEmpty(key)) return;
        anim.PlayOnce(key, fallback, forceRestart);
    }

    // === 레이어 변경 ===
    private void ChangeLayerOnHit(GameObject victimGO)
    {
        if (changeWholeHierarchy)
        {
            var all = victimGO.GetComponentsInChildren<Transform>(true);
            foreach (var t in all) t.gameObject.layer = newLayerOnHit;
        }
        else
        {
            victimGO.layer = newLayerOnHit;
        }
    }
}
