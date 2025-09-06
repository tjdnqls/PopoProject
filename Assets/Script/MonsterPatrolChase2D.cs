// ===================== MonsterABPatrolFSM.cs =====================
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class MonsterABPatrolFSM : MonoBehaviour, IDamageable
{
    public enum State { Patrol, Alert, Chase, AttackWindup, Return }

    [Header("Animation")]
    public SpriteAnimationManager anim; // Idle / Run / AttackStart / Attack / Hit / Death

    [Header("Refs")]
    [SerializeField] private Rigidbody2D rb;
    [SerializeField] private Collider2D body;
    [SerializeField] private SpriteRenderer sr;

    [Header("Stop-All on Tag Hit")]
    [SerializeField] private string stopOnTag = "Monkill";
    [SerializeField] private bool freezeRigidbodyOnStop = true;   // FreezeAll 고정
    [SerializeField] private bool disableComponentOnStop = true;  // 이 스크립트 비활성화
    private bool isStoppedByTag = false;

    [Header("Waypoints (A <-> B 왕복)")]
    [SerializeField] private Transform waypointA;
    [SerializeField] private Transform waypointB;
    [SerializeField] private Vector2 fallbackLocalA = new Vector2(-3f, 0f);
    [SerializeField] private Vector2 fallbackLocalB = new Vector2(3f, 0f);
    [SerializeField] private float arriveEps = 0.08f;

    [Header("Move")]
    [SerializeField] private float patrolSpeed = 2.4f;
    [SerializeField] private float chaseSpeed = 3.8f;
    [SerializeField] private float accel = 25f;

    [Header("Ground / Obstacle Layers")]
    [SerializeField] private LayerMask groundMask;   // 바닥
    [SerializeField] private LayerMask obstacleMask; // 벽/턱 등
    [SerializeField] private LayerMask playerMask;   // 플레이어(핑/탐지 용도)

    [Header("Ray Probes")]
    [SerializeField] private float wallCheckDist = 0.30f;   // 정면 벽
    [SerializeField] private float lowWallCheckDist = 0.25f;// 하단 앞벽(턱)
    [SerializeField] private float ledgeForward = 0.25f;    // 발끝 전방
    [SerializeField] private float ledgeDownDist = 0.60f;   // 낙하 체크 깊이
    [SerializeField] private float feetYOffset = 0.05f;     // 발 위치 y 보정
    [SerializeField] private float lowWallYOffset = 0.10f;  // 하단 앞벽 레이 y

    [Header("Melee Hitbox (child pulse)")]
    [SerializeField] private GameObject meleeHitbox;     // 자식 히트박스(콜라이더 포함)
    [SerializeField] private float meleeActiveSeconds = 0.2f; // 활성 시간
    [SerializeField] private bool useHitboxDamage = true;     // true면 히트박스 방식
    [SerializeField] private Vector2 meleeOffset = new Vector2(0.6f, 0f); // 몬스터 기준 앞쪽 오프셋(양수 x)
    [SerializeField] private bool flipHitboxBySpriteFlip = true;          // 좌우 뒤집기 시 히트박스도 미러링

    // 히트박스 내부 캐시
    private Vector3 _meleeLocalPosZLocked;   // 원래 z 보존
    private BoxCollider2D _hbBox;
    private CapsuleCollider2D _hbCapsule;
    private CircleCollider2D _hbCircle;
    private Vector2 _colliderOffset0;        // 콜라이더 원래 offset

    [Header("Aggro Detection (by Ping)")]
    [Tooltip("어그로 후보를 모을 반경(핑 발사 트리거)")]
    [SerializeField] private float pingScanRadius = 6f;
    [Tooltip("핑 프리팹(반드시 Rigidbody2D + Trigger Collider2D 포함)")]
    [SerializeField] private AggroPing2D pingPrefab;
    [Tooltip("핑 발사 위치(없으면 눈 위치에서 발사)")]
    [SerializeField] private Transform pingMuzzle;
    [Tooltip("같은 타겟으로 핑 재발사 최소 쿨타임")]
    [SerializeField] private float pingCooldownPerTarget = 0.5f;
    [Tooltip("한 번에 발사 가능한 핑 수(스팸 방지)")]
    [SerializeField] private int maxConcurrentPings = 4;

    [Header("Ping Flight")]
    [SerializeField] private float pingSpeed = 18f;
    [SerializeField] private float pingLifetime = 0.8f;
    [Tooltip("핑 진행 방향 예측 보정(0=없음)")]
    [SerializeField] private float pingLeadFactor = 0f;

    [Header("Alert / Chase / Return")]
    [SerializeField] private float alertStopSec = 0.5f; // 5) 잠깐 멈춤
    [SerializeField] private GameObject exclamationPrefab;
    [SerializeField] private Vector2 exclamationOffset = new Vector2(0f, 1.2f);
    [Tooltip("추격이 이 시간 이상 지속되면 종료 후 복귀")]
    [SerializeField] private float maxChaseSeconds = 5f;

    [Header("Attack (one-shot / hitbox)")]
    [SerializeField] private int attackDamage = 1;
    [SerializeField] private float attackWindupSec = 1.0f;   // 점점 붉어짐
    [SerializeField] private float attackRecoverSec = 0.2f;  // 후딜
    [SerializeField] private Color attackColor = new Color(1f, 0.2f, 0.2f, 1f);

    [Header("Attack Tuning (Edge Distance)")]
    [Tooltip("콜라이더-콜라이더 가장자리 거리로 본 사거리")]
    [SerializeField] private float attackEdgeRange = 0.35f;
    [Tooltip("수직 위치 허용치(같은 발판 정도로 인정)")]
    [SerializeField] private float attackVerticalTolerance = 0.9f;

    [Header("Death")]
    [SerializeField] private float despawnDelay = 3f; // 피격 사망 후 제거 지연

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private bool logPing = false;

    // ---------- Return 상태: 길막 공격 트리거 ----------
    [Header("Return Block Attack")]
    [SerializeField] private float returnBlockCheckDist = 0.9f;   // 앞 탐지 거리
    [SerializeField] private float returnBlockYTolerance = 0.9f;  // 높이 허용(같은 발판 정도)
    [SerializeField] private float returnBlockedAttackDelay = 2f; // 2초 길막이면 공격
    private float returnBlockTimer = 0f;
    private Transform returnBlockingPlayer = null;

    // 내부 상태
    private State state;
    private Vector2 wpA, wpB;
    private int patrolTargetIndex; // 0:A, 1:B
    private int dir;               // +1/-1
    private Vector2 homePos;
    private float chaseStartTime;
    private Coroutine alertCo, attackCo;

    // 핑 관리
    private readonly Dictionary<int, float> _lastPingTime = new Dictionary<int, float>(); // key: target.GetInstanceID()
    private int _activePings = 0;

    // 탐지 버퍼
    private static readonly Collider2D[] _hits = new Collider2D[16];

    // 타겟
    private Transform currentTarget;

    // 기타
    private LayerMask blockingMask; // obstacle에서 player 제외한 벽 마스크
    private bool isDying = false;

    // ---- 충돌 무시(플레이어 루트 단위 캐시) ----
    private readonly HashSet<int> _ignoredPlayerRoots = new HashSet<int>();

    // ---------- Helpers ----------
    private static bool IsOnLayerMask(int layer, LayerMask mask) => (mask.value & (1 << layer)) != 0;
    private static bool SameTargetBranch(Transform a, Transform b)
    {
        if (!a || !b) return false;
        return a == b || a.IsChildOf(b) || b.IsChildOf(a);
    }
    // 부모 중 "Player" 태그가 있으면 true (시체가 Ground 레이어여도 태그는 Player일 수 있음)
    private static bool HasPlayerTagInParents(Transform t)
    {
        for (Transform p = t; p != null; p = p.parent)
            if (p.CompareTag("Player")) return true;
        return false;
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

    private void SetFlipByDir(int d)
    {
        if (sr) sr.flipX = d < 0;
        PositionMeleeHitbox(); // 바라보는 방향 바뀔 때 히트박스 위치/offset 동기화
    }

    private void Reset()
    {
        rb = GetComponent<Rigidbody2D>();
        body = GetComponent<Collider2D>();
        sr = GetComponentInChildren<SpriteRenderer>();
    }

    private void Awake()
    {
        if (!rb) rb = GetComponent<Rigidbody2D>();
        if (!body) body = GetComponent<Collider2D>();
        if (!sr) sr = GetComponentInChildren<SpriteRenderer>();

        rb.freezeRotation = true;
        homePos = transform.position;

        // obstacle에서 player를 제외 (플레이어를 벽으로 보지 않기)
        blockingMask = obstacleMask & ~playerMask;

        // 웨이포인트 확정
        wpA = waypointA ? (Vector2)waypointA.position : (Vector2)transform.position + fallbackLocalA;
        wpB = waypointB ? (Vector2)waypointB.position : (Vector2)transform.position + fallbackLocalB;

        // 시작 타깃: 가까운 포인트
        patrolTargetIndex = (Vector2.SqrMagnitude((Vector2)transform.position - wpA) <=
                             Vector2.SqrMagnitude((Vector2)transform.position - wpB)) ? 0 : 1;

        dir = ((GetPatrolTarget().x - transform.position.x) >= 0f) ? +1 : -1;
        state = State.Patrol;

        // 히트박스 캐시
        if (meleeHitbox)
        {
            _meleeLocalPosZLocked = meleeHitbox.transform.localPosition; // 원래 z 보존
            _hbBox = meleeHitbox.GetComponent<BoxCollider2D>();
            _hbCapsule = meleeHitbox.GetComponent<CapsuleCollider2D>();
            _hbCircle = meleeHitbox.GetComponent<CircleCollider2D>();

            if (_hbBox) _colliderOffset0 = _hbBox.offset;
            else if (_hbCapsule) _colliderOffset0 = _hbCapsule.offset;
            else if (_hbCircle) _colliderOffset0 = _hbCircle.offset;
        }

        // 시작 시 씬에 있는 Player 루트들과 충돌 미리 끊기(사체 포함)
        var players = GameObject.FindGameObjectsWithTag("Player");
        foreach (var p in players) IgnorePlayerRootCollisions(p.transform);
    }

    private void OnEnable()
    {
        var v = rb.linearVelocity; v.x = dir * patrolSpeed; rb.linearVelocity = v;
        PlayAnim("Run", true);
        SetFlipByDir(dir); // 내부에서 PositionMeleeHitbox 호출됨
    }

    private void FixedUpdate()
    {
        if (isStoppedByTag || isDying)
        {
            StopHorizontal();
            return;
        }

        switch (state)
        {
            case State.Patrol: TickPatrol(); break;
            case State.Alert: TickAlert(); break;
            case State.Chase: TickChase(); break;
            case State.AttackWindup: StopHorizontal(); break;
            case State.Return: TickReturn(); break;
        }
    }

    // ============ Patrol ============
    private void TickPatrol()
    {
        PlayAnim("Run");
        Vector2 target = GetPatrolTarget();
        dir = (target.x > transform.position.x) ? +1 : -1;
        SetFlipByDir(dir);

        MoveHorizontalTowards(dir * patrolSpeed);

        if (Mathf.Abs(target.x - transform.position.x) <= arriveEps)
        {
            TogglePatrolTarget();
        }
        else if (FrontWall(dir) || LowFrontWall(dir) || LedgeAhead(dir))
        {
            TogglePatrolTarget();
            dir = -dir;
            SetFlipByDir(dir);
            MoveHorizontalTowards(dir * patrolSpeed);
        }

        // 핑 기반 후보 스캔 & 발사
        ScanAndShootPings();
    }

    private void TickAlert()
    {
        StopHorizontal();
        PlayAnim("Idle");
    }

    // ============ Chase ============
    private void TickChase()
    {
        PlayAnim("Run");

        // 타겟이 플레이어 마스크에서 사라지면(사망→Ground 등) 바로 복귀
        if (currentTarget && !IsOnLayerMask(currentTarget.gameObject.layer, playerMask))
        {
            currentTarget = null;
            EnterReturn();
            return;
        }

        if (!currentTarget)
        {
            EnterReturn(); return;
        }

        // 추격 시간 제한
        if (Time.time - chaseStartTime >= maxChaseSeconds)
        {
            EnterReturn(); return;
        }

        int chaseDir = (currentTarget.position.x > transform.position.x) ? +1 : -1;
        SetFlipByDir(chaseDir);

        if (FrontWall(chaseDir) || LowFrontWall(chaseDir) || LedgeAhead(chaseDir))
            StopHorizontal();
        else
            MoveHorizontalTowards(chaseDir * chaseSpeed);

        if (WithinAttackWindow(currentTarget))
            EnterAttack(currentTarget);
    }

    // ============ Attack ============
    private void EnterAttack(Transform target)
    {
        if (attackCo != null) StopCoroutine(attackCo);
        state = State.AttackWindup;
        attackCo = StartCoroutine(AttackRoutine(target));
    }

    private IEnumerator AttackRoutine(Transform snapshotTarget)
    {
        if (!sr) yield break;

        // 공격 직전 방향-히트박스 위치 재보정(안전)
        PositionMeleeHitbox();

        // 공격 준비 (점점 붉어짐)
        Color startC = sr.color; float t = 0f;
        PlayOnce("AttackStart");
        while (t < attackWindupSec)
        {
            t += Time.fixedDeltaTime;
            float a = Mathf.Clamp01(t / attackWindupSec);
            sr.color = Color.Lerp(startC, attackColor, a);
            yield return new WaitForFixedUpdate();
        }

        // === 공격 개시 ===
        sr.color = Color.white;
        PlayOnce("Attack", "Idle");

        if (useHitboxDamage && meleeHitbox)
        {
            var hb = meleeHitbox.GetComponent<MeleeHitboxOnce>();
            if (hb) hb.Arm(attackDamage, transform);
            else meleeHitbox.SetActive(true);

            yield return new WaitForSeconds(meleeActiveSeconds);

            if (hb) hb.Disarm();
            else meleeHitbox.SetActive(false);
        }
        else
        {
            // 단발 판정(여전히 사정거리 & "플레이어 레이어"일 때만)
            if (snapshotTarget &&
                IsOnLayerMask(snapshotTarget.gameObject.layer, playerMask) &&
                WithinAttackWindow(snapshotTarget))
            {
                ApplyDamage(snapshotTarget);
            }
        }

        // 후딜
        float r = 0f;
        while (r < attackRecoverSec) { r += Time.fixedDeltaTime; yield return new WaitForFixedUpdate(); }

        state = State.Chase;
    }

    // ============ Return ============
    private void TickReturn()
    {
        PlayAnim("Run");

        int retDir = (homePos.x > transform.position.x) ? +1 : -1;
        SetFlipByDir(retDir);

        // 기본 복귀 이동
        if (FrontWall(retDir) || LowFrontWall(retDir) || LedgeAhead(retDir))
            StopHorizontal();
        else
            MoveHorizontalTowards(retDir * patrolSpeed);

        // ---- 길막 감지 & 2초 유지 시 공격/추격 전환 ----
        Transform blocker = DetectPlayerAhead(retDir);
        bool touchingPlayer = body && body.IsTouchingLayers(playerMask);
        bool almostStopped = Mathf.Abs(rb.linearVelocity.x) < 0.05f;

        if (blocker != null && (touchingPlayer || almostStopped))
        {
            returnBlockingPlayer = blocker;
            returnBlockTimer += Time.fixedDeltaTime;
        }
        else
        {
            returnBlockingPlayer = null;
            returnBlockTimer = 0f;
        }

        if (returnBlockTimer >= returnBlockedAttackDelay)
        {
            currentTarget = returnBlockingPlayer;

            if (currentTarget && WithinAttackWindow(currentTarget))
                EnterAttack(currentTarget);
            else
                EnterChase();

            returnBlockTimer = 0f;
            return;
        }

        // 복귀 완료
        if (Mathf.Abs(homePos.x - transform.position.x) <= arriveEps)
            KickstartPatrolLoop();
    }

    private void KickstartPatrolLoop()
    {
        int nearestIdx = (Vector2.SqrMagnitude((Vector2)transform.position - wpA) <=
                          Vector2.SqrMagnitude((Vector2)transform.position - wpB)) ? 0 : 1;
        int farIdx = (nearestIdx == 0) ? 1 : 0;

        patrolTargetIndex = farIdx;
        dir = (GetPatrolTarget().x > transform.position.x) ? +1 : -1;

        if (FrontWall(dir) || LowFrontWall(dir) || LedgeAhead(dir))
        {
            patrolTargetIndex = nearestIdx;
            dir = -dir;
        }

        rb.WakeUp();
        var v = rb.linearVelocity; v.x = dir * Mathf.Max(0.5f, patrolSpeed * 0.6f); rb.linearVelocity = v;
        SetFlipByDir(dir);
        state = State.Patrol;
        PlayAnim("Run", true);
    }

    // ============ State transitions ============
    private void EnterAlert()
    {
        state = State.Alert;
        StopHorizontal();
        PlayAnim("Idle", true);

        if (alertCo != null) StopCoroutine(alertCo);
        alertCo = StartCoroutine(AlertThenChase());
    }

    private IEnumerator AlertThenChase()
    {
        if (exclamationPrefab)
        {
            var go = Instantiate(exclamationPrefab, (Vector2)transform.position + exclamationOffset, Quaternion.identity, transform);
            Destroy(go, alertStopSec + 0.2f);
        }

        float t = 0f;
        while (t < alertStopSec) { t += Time.fixedDeltaTime; yield return new WaitForFixedUpdate(); }
        EnterChase();
    }

    private void EnterChase()
    {
        if (!currentTarget) { state = State.Return; return; }
        state = State.Chase;
        chaseStartTime = Time.time;
        PlayAnim("Run", true);
    }

    private void EnterReturn()
    {
        state = State.Return;
        StopHorizontal();
        currentTarget = null;

        // 길막 타이머 리셋
        returnBlockTimer = 0f;
        returnBlockingPlayer = null;

        PlayAnim("Run");
    }

    // ============ Ping-based Detection ============
    private void ScanAndShootPings()
    {
        if (!pingPrefab) return;
        ContactFilter2D filter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = playerMask,
            useTriggers = true
        };

        int n = Physics2D.OverlapCircle((Vector2)transform.position, pingScanRadius, filter, _hits);
        if (n <= 0) return;

        for (int i = 0; i < n; i++)
        {
            if (_activePings >= maxConcurrentPings) break;

            var col = _hits[i];
            if (!col) continue;
            Transform cand = col.attachedRigidbody ? col.attachedRigidbody.transform : col.transform;
            if (!cand) continue;

            int key = cand.GetInstanceID();
            if (_lastPingTime.TryGetValue(key, out float last) && (Time.time - last) < pingCooldownPerTarget)
                continue;

            FirePing(cand);
            _lastPingTime[key] = Time.time;
            _activePings++;
        }
    }

    private void FirePing(Transform target)
    {
        Vector2 origin = pingMuzzle ? (Vector2)pingMuzzle.position : Eyes();
        Vector2 aim = TargetAimPoint(target);

        // 간단한 예측(선택)
        if (pingLeadFactor > 0f && target.TryGetComponent<Rigidbody2D>(out var trb))
        {
            Vector2 vel = trb.linearVelocity;
            aim += vel * pingLeadFactor;
        }

        var ping = Instantiate(pingPrefab, origin, Quaternion.identity);
        ping.Init(
            owner: this,
            target: target,
            speed: pingSpeed,
            lifetime: pingLifetime,
            groundMask: groundMask,
            playerMask: playerMask,
            obstacleMask: obstacleMask,
            onDespawn: () => { _activePings = Mathf.Max(0, _activePings - 1); }
        );

        if (logPing) Debug.Log($"[Monster] FirePing -> {target.name}");
    }

    // 핑이 플레이어에 명중했을 때 호출됨
    public void OnAggroPingHit(Transform hitPlayer)
    {
        if (!hitPlayer) return;
        currentTarget = hitPlayer; // 이 순간에만 어그로 확정
        EnterAlert();              // 5) 잠시 멈추고 느낌표 → 6) 추격
    }

    // ============ Damage / Death ============
    public void TakeDamage(int amount, Vector2 hitPoint, Vector2 hitNormal)
    {
        if (isDying) return;
        StartCoroutine(HitDeathRoutine());
    }

    public void OnHit(int damage) // SendMessage 호환
    {
        if (isDying) return;
        StartCoroutine(HitDeathRoutine());
    }

    private IEnumerator HitDeathRoutine()
    {
        isDying = true;

        // 로직/물리 정지
        if (alertCo != null) { StopCoroutine(alertCo); alertCo = null; }
        if (attackCo != null) { StopCoroutine(attackCo); attackCo = null; }
        state = State.Patrol;
        if (rb)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            if (freezeRigidbodyOnStop)
                rb.constraints = RigidbodyConstraints2D.FreezeAll;
        }
        if (body) body.enabled = false; // 추가 타격/충돌 차단

        PlayOnce("Hit", "Death");
        yield return new WaitForSeconds(despawnDelay);

        Destroy(gameObject);
    }

    // ============ Helpers ============
    private Vector2 Eyes()
    {
        var b = body.bounds;
        return new Vector2(b.center.x, b.max.y + 0.51f); // 눈 높이 약간 위
    }
    private Vector2 TargetAimPoint(Transform t)
    {
        if (!t) return transform.position;
        if (t.TryGetComponent<Collider2D>(out var c))
            return (Vector2)c.bounds.center + new Vector2(0f, 0.25f);
        return (Vector2)t.position + new Vector2(0f, 0.25f);
    }

    private void StopAllBehaviours(string reason)
    {
        isStoppedByTag = true;

        if (alertCo != null) { StopCoroutine(alertCo); alertCo = null; }
        if (attackCo != null) { StopCoroutine(attackCo); attackCo = null; }
        state = State.Patrol;
        if (sr) sr.color = Color.white;

        if (rb)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            if (freezeRigidbodyOnStop)
                rb.constraints = RigidbodyConstraints2D.FreezeAll;
        }

        if (disableComponentOnStop)
            enabled = false;

#if UNITY_EDITOR
        Debug.Log($"[Monster] Stopped by tag '{stopOnTag}' ({reason})", this);
#endif
    }

    private void ApplyDamage(Transform target)
    {
        if (!target) return;

        if (target.TryGetComponent<IDamageable>(out var dmg))
            dmg.TakeDamage(attackDamage, transform.position, new Vector2(dir, 0));
        else if (target.TryGetComponent<PlayerHealth>(out var hp))
            hp.ApplyDamage(attackDamage);
        else
            target.SendMessage("OnHit", attackDamage, SendMessageOptions.DontRequireReceiver);
    }

    // Movement / Probes
    private void MoveHorizontalTowards(float targetSpeedX)
    {
        Vector2 v = rb.linearVelocity;
        v.x = Mathf.MoveTowards(v.x, targetSpeedX, accel * Time.fixedDeltaTime);
        rb.linearVelocity = v;
    }
    private void StopHorizontal()
    {
        Vector2 v = rb.linearVelocity; v.x = 0f; rb.linearVelocity = v;
    }
    private Vector2 Feet()
    {
        var b = body.bounds;
        return new Vector2(b.center.x, b.min.y + feetYOffset);
    }

    // 플레이어를 벽으로 보지 않도록 playerMask 제외한 blockingMask 사용
    // 단, '현재 타겟'을 무시하는 예외는 그 타겟이 아직 플레이어 레이어일 때만 적용.
    // 추가: 맞은 물체(혹은 그 부모)가 Player 태그면(시체가 Ground여도) 벽으로 처리하지 않음.
    private bool FrontWall(int d)
    {
        Vector2 origin = Feet() + new Vector2(d * (body.bounds.extents.x + 0.02f), 0.15f);
        var hit = Physics2D.Raycast(origin, Vector2.right * d, wallCheckDist, blockingMask);

        if (!hit) return false;

        if (currentTarget && hit.collider)
        {
            bool isCurrentTargetCollider = SameTargetBranch(hit.collider.transform, currentTarget);
            bool targetStillPlayerLayer = IsOnLayerMask(hit.collider.gameObject.layer, playerMask);
            if (isCurrentTargetCollider && targetStillPlayerLayer)
                return false; // 타겟이 아직 Player 레이어일 때만 무시
        }

        if (hit.collider && HasPlayerTagInParents(hit.collider.transform))
            return false; // ★ Ground가 된 플레이어 시체도 통과

        return true;
    }
    private bool LowFrontWall(int d)
    {
        Vector2 origin = Feet() + new Vector2(d * (body.bounds.extents.x + 0.02f), lowWallYOffset);
        var hit = Physics2D.Raycast(origin, Vector2.right * d, lowWallCheckDist, blockingMask);

        if (!hit) return false;

        if (currentTarget && hit.collider)
        {
            bool isCurrentTargetCollider = SameTargetBranch(hit.collider.transform, currentTarget);
            bool targetStillPlayerLayer = IsOnLayerMask(hit.collider.gameObject.layer, playerMask);
            if (isCurrentTargetCollider && targetStillPlayerLayer)
                return false;
        }

        if (hit.collider && HasPlayerTagInParents(hit.collider.transform))
            return false; // ★ Ground가 된 플레이어 시체도 통과

        return true;
    }
    private bool LedgeAhead(int d)
    {
        Vector2 origin = Feet() + new Vector2(d * (body.bounds.extents.x + ledgeForward), 0.02f);
        return !Physics2D.Raycast(origin, Vector2.down, ledgeDownDist, groundMask);
    }

    private Vector2 GetPatrolTarget() => (patrolTargetIndex == 0) ? wpA : wpB;
    private void TogglePatrolTarget() => patrolTargetIndex = (patrolTargetIndex == 0) ? 1 : 0;

    // === 공격 거리: 콜라이더-대-콜라이더 가장자리 거리 사용
    private float EdgeDistanceTo(Transform t)
    {
        if (!t) return float.MaxValue;

        if (body && t.TryGetComponent<Collider2D>(out var tc))
        {
            var d = Physics2D.Distance(body, tc);
            return d.isOverlapped ? 0f : d.distance;
        }
        return Vector2.Distance(t.position, transform.position);
    }

    private bool WithinAttackWindow(Transform t)
    {
        if (!t) return false;
        float vy = Mathf.Abs(t.position.y - transform.position.y);
        if (vy > attackVerticalTolerance) return false;
        return EdgeDistanceTo(t) <= attackEdgeRange;
    }

    private float DistanceTo(Transform t) => t ? Vector2.Distance(t.position, transform.position) : float.MaxValue;

    // ---- Player(태그)와의 물리 충돌 완전 무시: 시체가 Ground여도 루트에 Player 태그 있으면 끊는다 ----
    private void IgnorePlayerRootCollisions(Transform anyChildInPlayerRoot)
    {
        if (!body || !anyChildInPlayerRoot) return;
        var root = anyChildInPlayerRoot.root;
        if (!root) return;

        int id = root.GetInstanceID();
        if (_ignoredPlayerRoots.Contains(id)) return; // 이미 처리

        var allCols = root.GetComponentsInChildren<Collider2D>(true);
        foreach (var c in allCols)
        {
            if (c && c != body)
                Physics2D.IgnoreCollision(body, c, true);
        }
        _ignoredPlayerRoots.Add(id);
    }

    private void TryIgnoreIfPlayer(Collider2D other)
    {
        if (!other) return;
        if (HasPlayerTagInParents(other.transform))
            IgnorePlayerRootCollisions(other.transform);
    }

    private void OnCollisionEnter2D(Collision2D c)
    {
        // stopOnTag 처리
        if (!isStoppedByTag && c.collider && c.collider.CompareTag(stopOnTag))
            StopAllBehaviours($"Collision with {stopOnTag}");

        // ★ 플레이어(루트에 Player 태그)와는 항상 물리 충돌 무시 (생존/사망 상관없이)
        TryIgnoreIfPlayer(c.collider);
    }

    private void OnCollisionStay2D(Collision2D c)
    {
        // 혹시 Enter에서 놓친 경우에도 계속 끊어준다
        TryIgnoreIfPlayer(c.collider);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!isStoppedByTag && other && other.CompareTag(stopOnTag))
            StopAllBehaviours($"Trigger with {stopOnTag}");

        // 트리거여도 플레이어 루트면 전부 무시 (자식에 Trigger가 있을 수 있음)
        TryIgnoreIfPlayer(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        TryIgnoreIfPlayer(other);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        Vector2 a = waypointA ? (Vector2)waypointA.position : (Vector2)transform.position + fallbackLocalA;
        Vector2 b = waypointB ? (Vector2)waypointB.position : (Vector2)transform.position + fallbackLocalB;

        Gizmos.color = Color.green; Gizmos.DrawSphere(a, 0.08f);
        Gizmos.color = Color.blue; Gizmos.DrawSphere(b, 0.08f);
        Gizmos.color = Color.yellow; Gizmos.DrawLine(a, b);

        // EdgeDistance용 시각 보조 (근사)
        Gizmos.color = new Color(1, 0, 0, 0.35f);
        Gizmos.DrawWireSphere(transform.position, attackEdgeRange);
    }

    // ---------- 히트박스 좌/우 미러링 ----------
    private void PositionMeleeHitbox()
    {
        if (!meleeHitbox || !flipHitboxBySpriteFlip) return;

        int facing = (sr && sr.flipX) ? -1 : 1;

        // 트랜스폼 로컬 위치를 방향에 맞게 미러링(원래 z는 유지)
        meleeHitbox.transform.localPosition = new Vector3(
            Mathf.Abs(meleeOffset.x) * facing,
            meleeOffset.y,
            _meleeLocalPosZLocked.z
        );

        // 콜라이더 offset.x도 좌/우 뒤집기
        if (_hbBox) _hbBox.offset = new Vector2(Mathf.Abs(_colliderOffset0.x) * facing, _colliderOffset0.y);
        if (_hbCapsule) _hbCapsule.offset = new Vector2(Mathf.Abs(_colliderOffset0.x) * facing, _colliderOffset0.y);
        if (_hbCircle) _hbCircle.offset = new Vector2(Mathf.Abs(_colliderOffset0.x) * facing, _colliderOffset0.y);
    }

    // ---------- Return 길막 감지 ----------
    private Transform DetectPlayerAhead(int d)
    {
        if (!body) return null;

        Bounds b = body.bounds;
        Vector2 size = new Vector2(returnBlockCheckDist, b.size.y * 0.8f);
        Vector2 center = new Vector2(
            b.center.x + d * (b.extents.x + size.x * 0.5f + 0.02f),
            b.min.y + size.y * 0.5f
        );

        Collider2D col = Physics2D.OverlapBox(center, size, 0f, playerMask);

#if UNITY_EDITOR
        Color c = col ? Color.cyan : new Color(0, 1, 1, 0.25f);
        Debug.DrawLine(center + new Vector2(-size.x / 2, -size.y / 2), center + new Vector2(size.x / 2, -size.y / 2), c, 0f);
        Debug.DrawLine(center + new Vector2(size.x / 2, -size.y / 2), center + new Vector2(size.x / 2, size.y / 2), c, 0f);
        Debug.DrawLine(center + new Vector2(size.x / 2, size.y / 2), center + new Vector2(-size.x / 2, size.y / 2), c, 0f);
        Debug.DrawLine(center + new Vector2(-size.x / 2, size.y / 2), center + new Vector2(-size.x / 2, -size.y / 2), c, 0f);
#endif
        if (!col) return null;

        float vy = Mathf.Abs(col.bounds.center.y - b.center.y);
        if (vy > returnBlockYTolerance) return null;

        return col.attachedRigidbody ? col.attachedRigidbody.transform : col.transform;
    }
}

// 선택형 인터페이스
public interface IDamageable
{
    void TakeDamage(int amount, Vector2 hitPoint, Vector2 hitNormal);
}

public class PlayerHealth : MonoBehaviour
{
    public int hp = 3;
    public void ApplyDamage(int dmg)
    {
        hp -= dmg;
        if (hp <= 0) { /* 사망 처리 */ }
    }
}
