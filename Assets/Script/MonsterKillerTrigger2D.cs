using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(Rigidbody2D))]
[DisallowMultipleComponent]
public class MonsterKillerTrigger2D : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private string monsterLayerName = "Monster"; // 감지 대상 레이어
    [SerializeField] private float killDelay = 3.0f;              // 감지 후 제거 딜레이(초)
    [SerializeField] private bool targetRootWithRigidbody = true; // Rigidbody 루트로 판정할지

    [Header("Detection Modes")]
    [SerializeField] private bool killOnEnter = true;             // Enter 시 처리
    [SerializeField] private bool killOnStay = true;             // Stay 시 처리(이미 겹침 보완)
    [SerializeField] private bool scanOnEnableOnce = true;        // 활성화 직후 1회 스캔

    [Header("Attack Window (optional)")]
    [Tooltip("공격 윈도우 중에만 처형되도록 제한합니다.")]
    [SerializeField] private bool requireAttackWindow = false;
    private bool isAttackWindow = true; // requireAttackWindow=false면 항상 true로 취급

    [Header("Animation")]
    public SpriteAnimationManager anim; // Idle / Run / AttackStart / Attack / Hit / Death

    private int monsterLayer = -1;
    private Rigidbody2D rb;
    private Collider2D col;


    // 중복 스케줄 방지(동일 대상 다중 호출 억제)
    private readonly HashSet<int> _scheduled = new HashSet<int>();

    // Overlap 콜렉션(할당 최소화)
    private static readonly Collider2D[] _overlapBuf = new Collider2D[16];
    private ContactFilter2D _filter;

    void Awake()
    {
        monsterLayer = LayerMask.NameToLayer(monsterLayerName);

        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();

        // 트리거 세팅 권장
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        col.isTrigger = true;

        if (monsterLayer < 0)
            Debug.LogWarning($"[MonsterKillerTrigger2D] Layer '{monsterLayerName}' not found.");

        // 몬스터 레이어만 필터링
        _filter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = 1 << monsterLayer, // "Monster" 레이어만
            useTriggers = true               // 트리거 간 겹침도 포함
        };
        _filter.useTriggers = true; // 트리거 간 겹침도 인식
    }

    void OnEnable()
    {
        _scheduled.Clear();
        rb?.WakeUp();

        // 활성화 시 이미 겹쳐 있던 대상 즉시 처리(히트박스 켜지는 공격에 유용)
        if (scanOnEnableOnce && (!requireAttackWindow || isAttackWindow))
        {
            int count = col.Overlap(_filter, _overlapBuf);
            for (int i = 0; i < count; i++)
            {
                HandleCollider(_overlapBuf[i]);
            }
        }
    }

    // 공격 윈도우 제어: 애니메이션 이벤트에서 호출
    public void AttackWindowBegin()
    {
        isAttackWindow = true;
    }
    public void AttackWindowEnd()
    {
        isAttackWindow = false;
    }

    private bool PassesWindow()
    {
        return !requireAttackWindow || isAttackWindow;
    }

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
        if (monsterLayer < 0 || !PassesWindow()) return;
        if (other == null) return;

        // Rigidbody 루트를 대상으로 할지 여부
        GameObject hit = (targetRootWithRigidbody && other.attachedRigidbody)
            ? other.attachedRigidbody.gameObject
            : other.gameObject;

        if (hit.layer != monsterLayer) return;

        int id = hit.GetInstanceID();
        if (_scheduled.Contains(id)) return; // 중복 스케줄 방지

        // 연출(원샷 보호는 SpriteAnimationManager 내부에서 관리)
        PlayOnce("Hit", "Death");

        // 지연 파괴 스케줄
        var dd = hit.GetComponent<DelayedDestroy>();
        if (!dd) dd = hit.AddComponent<DelayedDestroy>();
        dd.Schedule(killDelay);

        _scheduled.Add(id);
    }

    private void PlayAnim(string key, bool forceRestart = false)
    {
        if (anim == null || string.IsNullOrEmpty(key)) return;
        if (anim.IsOneShotActive) return; // 1회 재생 보호
        anim.Play(key, forceRestart);
    }

    private void PlayOnce(string key, string fallback = null, bool forceRestart = true)
    {
        if (anim == null || string.IsNullOrEmpty(key)) return;
        anim.PlayOnce(key, fallback, forceRestart);
    }
}
