// ===================== MonsterABPatrolFSM.cs =====================
using Game.AI;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class MonsterABPatrolFSM : MonoBehaviour, IDamageable, IAggroPingOwner
{
    // [FIX-DEATH] Dead 상태
    public enum State { Patrol, Alert, Chase, AttackWindup, Return, Dead }

    [Header("Animation")]
    public SpriteAnimationManager anim; // Idle / Run / AttackStart / Attack / Hit / Death

    [Header("Refs")]
    [SerializeField] private Rigidbody2D rb;
    [SerializeField] private Collider2D body;
    [SerializeField] private SpriteRenderer sr;

    [Header("Stop-All on Tag Hit")]
    [SerializeField] private string stopOnTag = "Monkill";
    [SerializeField] private bool freezeRigidbodyOnStop = true;
    [SerializeField] private bool disableComponentOnStop = true;
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
    [SerializeField] private LayerMask groundMask;
    [SerializeField] private LayerMask obstacleMask;
    [SerializeField] private LayerMask playerMask;

    [Header("Ray Probes")]
    [SerializeField] private float wallCheckDist = 0.30f;
    [SerializeField] private float lowWallCheckDist = 0.25f;
    [SerializeField] private float ledgeForward = 0.25f;
    [SerializeField] private float ledgeDownDist = 0.60f;
    [SerializeField] private float feetYOffset = 0.05f;
    [SerializeField] private float lowWallYOffset = 0.10f;

    [Header("Melee Hitbox (child pulse)")]
    [SerializeField] private GameObject meleeHitbox;
    [SerializeField] private float meleeActiveSeconds = 0.2f;
    [SerializeField] private bool useHitboxDamage = true;
    [SerializeField] private Vector2 meleeOffset = new Vector2(0.6f, 0f);
    [SerializeField] private bool flipHitboxBySpriteFlip = true;

    // 히트박스 캐시
    private Vector3 _meleeLocalPosZLocked;
    private BoxCollider2D _hbBox;
    private CapsuleCollider2D _hbCapsule;
    private CircleCollider2D _hbCircle;
    private Vector2 _colliderOffset0;

    [Header("Aggro Detection (by Ping)")]
    [SerializeField] private float pingScanRadius = 6f;
    [SerializeField] private AggroPing2D pingPrefab;
    [SerializeField] private Transform pingMuzzle;
    [SerializeField] private float pingCooldownPerTarget = 0.5f;
    [SerializeField] private int maxConcurrentPings = 4;

    [Header("Ping Flight")]
    [SerializeField] private float pingSpeed = 18f;
    [SerializeField] private float pingLifetime = 0.8f;
    [SerializeField] private float pingLeadFactor = 0f;

    [Header("Alert / Chase / Return")]
    [SerializeField] private float alertStopSec = 0.5f;
    [SerializeField] private GameObject exclamationPrefab;
    [SerializeField] private Vector2 exclamationOffset = new Vector2(0f, 1.2f);
    [SerializeField] private float maxChaseSeconds = 5f;

    [Header("Attack (one-shot / hitbox)")]
    [SerializeField] private int attackDamage = 1;
    [SerializeField] private float attackWindupSec = 1.0f;
    [SerializeField] private float attackRecoverSec = 0.2f;
    [SerializeField] private Color attackColor = new Color(1f, 0.2f, 0.2f, 1f);

    [Header("Attack Tuning (Edge Distance)")]
    [SerializeField] private float attackEdgeRange = 0.35f;
    [SerializeField] private float attackVerticalTolerance = 0.9f;

    [Header("Death")]
    [SerializeField] private float despawnDelay = 3f;

    // ====== 추가: Death VFX (Blood) ======
    [Header("Death VFX (Blood)")]
    [SerializeField] private GameObject blood0Prefab;
    [SerializeField] private GameObject blood1Prefab;
    [SerializeField] private int burstBloodCount = 10;              // 즉시 분출 개수
    [SerializeField] private float burstRadius = 0.35f;            // 즉시 분출 반경
    [SerializeField] private Vector2 burstSpeedRange = new Vector2(1.2f, 3.0f); // 즉시 분출 속도
    [SerializeField] private float sustainDelay = 0.3f;            // 발 분출 시작 지연
    [SerializeField] private float sustainDuration = 3.0f;         // 발 분출 지속 시간
    [SerializeField] private Vector2 sustainIntervalRange = new Vector2(0.06f, 0.20f); // 분출 간격
    [SerializeField] private float sustainJitter = 0.06f;          // 발 분출 위치 지터
    [SerializeField] private float bloodLifetime = 3.0f;           // 혈흔 자동 소멸 시간

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private bool logPing = false;

    // ---------- Return 길막 공격 ----------
    [Header("Return Block Attack")]
    [SerializeField] private float returnBlockCheckDist = 0.9f;
    [SerializeField] private float returnBlockYTolerance = 0.9f;
    [SerializeField] private float returnBlockedAttackDelay = 2f;
    private float returnBlockTimer = 0f;
    private Transform returnBlockingPlayer = null;

    // ---------- Direct Player Detect (fallback) ----------
    [Header("Direct Player Detect (Fallback)")]
    [SerializeField] private bool enableDirectDetect = true;
    [SerializeField] private float directDetectRadius = 4f;

    // 내부 상태
    private State state;
    private Vector2 wpA, wpB;
    private int patrolTargetIndex; // 0:A, 1:B
    private int dir;               // +1/-1
    private Vector2 homePos;
    private float chaseStartTime;
    private Coroutine alertCo, attackCo;

    // 핑 관리
    private readonly Dictionary<int, float> _lastPingTime = new Dictionary<int, float>();
    private int _activePings = 0;

    // 탐지 버퍼
    private static readonly Collider2D[] _hits = new Collider2D[16];

    // 타겟
    private Transform currentTarget;

    // 기타
    private LayerMask blockingMask; // obstacle에서 player 제외
    private bool isDying = false;

    // 충돌 무시(플레이어 루트 단위 캐시)
    private readonly HashSet<int> _ignoredPlayerRoots = new HashSet<int>();

    // Dead/Stopped 가드
    private bool IsDeadOrStopped => isDying || isStoppedByTag || state == State.Dead;

    // 사망 위치 고정
    private Vector3 _deathPos;
    private Quaternion _deathRot;
    private Vector3 _deathFeetPos; // ★ 발 위치 캐시

    // ---------- Helpers ----------
    private static bool IsOnLayerMask(int layer, LayerMask mask) => (mask.value & (1 << layer)) != 0;
    private static bool SameTargetBranch(Transform a, Transform b)
    {
        if (!a || !b) return false;
        return a == b || a.IsChildOf(b) || b.IsChildOf(a);
    }
    private static bool HasPlayerTagInParents(Transform t)
    {
        for (Transform p = t; p != null; p = p.parent)
            if (p.CompareTag("Player")) return true;
        return false;
    }

    private void PlayAnim(string key, bool forceRestart = false)
    {
        if (anim == null || string.IsNullOrEmpty(key)) return;
        if (anim.IsOneShotActive) return;
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
        PositionMeleeHitbox();
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

        // obstacle에서 player 제외(플레이어를 벽으로 보지 않기)
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
            _meleeLocalPosZLocked = meleeHitbox.transform.localPosition;
            _hbBox = meleeHitbox.GetComponent<BoxCollider2D>();
            _hbCapsule = meleeHitbox.GetComponent<CapsuleCollider2D>();
            _hbCircle = meleeHitbox.GetComponent<CircleCollider2D>();

            if (_hbBox) _colliderOffset0 = _hbBox.offset;
            else if (_hbCapsule) _colliderOffset0 = _hbCapsule.offset;
            else if (_hbCircle) _colliderOffset0 = _hbCircle.offset;
        }

        // 시작 시 Player 루트들과 충돌 미리 끊기
        var players = GameObject.FindGameObjectsWithTag("Player");
        foreach (var p in players) IgnorePlayerRootCollisions(p.transform);
    }

    private void OnEnable()
    {
        if (IsDeadOrStopped) return;

        var v = rb.linearVelocity; v.x = dir * patrolSpeed; rb.linearVelocity = v;
        PlayAnim("Run", true);
        SetFlipByDir(dir);
    }

    private void FixedUpdate()
    {
        if (IsDeadOrStopped)
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
            case State.Dead: StopHorizontal(); break;
        }
    }

    // 사망 위치 고정(다른 스크립트가 Transform을 움직여도 무효화)
    private void LateUpdate()
    {
        if (state == State.Dead)
        {
            transform.position = _deathPos;
            transform.rotation = _deathRot;
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

        // 1) 핑 기반 후보 스캔 & 발사
        ScanAndShootPings();

        // 2) 직감지(백업)
        if (enableDirectDetect)
        {
            if (TryDetectNearest(playerMask, directDetectRadius, out Transform p))
            {
                currentTarget = p;
                EnterAlert();
            }
        }
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
        if (IsDeadOrStopped) return;
        if (attackCo != null) StopCoroutine(attackCo);
        state = State.AttackWindup;
        attackCo = StartCoroutine(AttackRoutine(target));
    }

    private IEnumerator AttackRoutine(Transform snapshotTarget)
    {
        if (!sr) yield break;

        PositionMeleeHitbox();

        Color startC = sr.color; float t = 0f;
        PlayOnce("AttackStart");
        while (t < attackWindupSec)
        {
            if (IsDeadOrStopped) yield break;
            t += Time.fixedDeltaTime;
            float a = Mathf.Clamp01(t / attackWindupSec);
            sr.color = Color.Lerp(startC, attackColor, a);
            yield return new WaitForFixedUpdate();
        }

        sr.color = Color.white;
        PlayOnce("Attack", "Idle");

        if (useHitboxDamage && meleeHitbox)
        {
            var hb = meleeHitbox.GetComponent<MeleeHitboxOnce>();
            if (hb) hb.Arm(attackDamage, transform);
            else meleeHitbox.SetActive(true);

            float elapsed = 0f;
            while (elapsed < meleeActiveSeconds)
            {
                if (IsDeadOrStopped) break;
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (hb) hb.Disarm();
            else meleeHitbox.SetActive(false);
        }
        else
        {
            if (!IsDeadOrStopped &&
                snapshotTarget &&
                IsOnLayerMask(snapshotTarget.gameObject.layer, playerMask) &&
                WithinAttackWindow(snapshotTarget))
            {
                ApplyDamage(snapshotTarget);
            }
        }

        float r = 0f;
        while (r < attackRecoverSec)
        {
            if (IsDeadOrStopped) yield break;
            r += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }

        if (!IsDeadOrStopped) state = State.Chase;
    }

    // ============ Return ============
    private void TickReturn()
    {
        PlayAnim("Run");

        int retDir = (homePos.x > transform.position.x) ? +1 : -1;
        SetFlipByDir(retDir);

        if (FrontWall(retDir) || LowFrontWall(retDir) || LedgeAhead(retDir))
            StopHorizontal();
        else
            MoveHorizontalTowards(retDir * patrolSpeed);

        // 길막 감지 & 2초 유지 시 공격/추격 전환
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

        // 복귀 중에도 직감지 백업
        if (enableDirectDetect && TryDetectNearest(playerMask, directDetectRadius, out Transform p))
        {
            currentTarget = p;
            EnterAlert();
        }
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
        if (IsDeadOrStopped) return;

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
        while (t < alertStopSec)
        {
            if (IsDeadOrStopped) yield break;
            t += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }
        EnterChase();
    }

    private void EnterChase()
    {
        if (IsDeadOrStopped) return;
        if (!currentTarget) { state = State.Return; return; }
        state = State.Chase;
        chaseStartTime = Time.time;
        PlayAnim("Run", true);
    }

    private void EnterReturn()
    {
        if (IsDeadOrStopped) return;
        state = State.Return;
        StopHorizontal();
        currentTarget = null;
        returnBlockTimer = 0f;
        returnBlockingPlayer = null;
        PlayAnim("Run");
    }

    // ============ Direct Detect ============
    private bool TryDetectNearest(LayerMask mask, float radius, out Transform nearest)
    {
        nearest = null;

        var filter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = mask,
            useTriggers = true
        };

        int n = Physics2D.OverlapCircle((Vector2)transform.position, radius, filter, _hits);
        if (n <= 0) return false;

        float best = float.PositiveInfinity;
        for (int i = 0; i < n; i++)
        {
            var c = _hits[i];
            if (!c) continue;
            Transform t = c.attachedRigidbody ? c.attachedRigidbody.transform : c.transform;
            float d = ((Vector2)t.position - (Vector2)transform.position).sqrMagnitude;
            if (d < best) { best = d; nearest = t; }
        }
        return nearest != null;
    }

    // ============ Ping-based Detection ============
    private void ScanAndShootPings()
    {
        if (IsDeadOrStopped) return;
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
        if (IsDeadOrStopped) return;

        Vector2 origin = pingMuzzle ? (Vector2)pingMuzzle.position : Eyes();
        Vector2 aim = TargetAimPoint(target);

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
        if (IsDeadOrStopped) return;
        if (!hitPlayer) return;
        currentTarget = hitPlayer;
        EnterAlert();
    }

    // ============ Damage / Death ============
    // 오버로드: 어떤 호출이 와도 사망 시퀀스로 진입
    public void TakeDamage(int amount)
    {
        if (IsDeadOrStopped) return;
        CameraShaker.Shake(0.4f, 0.5f);
        StartDeathSequence("TakeDamage(int)");
    }

    public void TakeDamage(int amount, Vector2 hitPoint, Vector2 hitNormal)
    {
        if (IsDeadOrStopped) return;
        CameraShaker.Shake(0.4f, 0.5f);
        StartDeathSequence("TakeDamage(int,vec,vec)");
    }

    public void OnHit(int damage)
    {
        if (IsDeadOrStopped) return;
        CameraShaker.Shake(0.4f, 0.5f);
        StartDeathSequence("OnHit");
    }

    // 사망 시 공통 진입: 전면 정지 + Hit→Death 연출 + 위치 고정 + Blood VFX
    private void StartDeathSequence(string reason)
    {
        if (isDying) return;
        isDying = true;
        state = State.Dead;

        // 좌표 고정 스냅샷 (★ Feet도 이 타이밍에 미리 계산해둔다)
        _deathPos = transform.position;
        _deathRot = transform.rotation;
        _deathFeetPos = GetFeetWorldFallback(); // body.enabled 끄기 전에 계산

        // === 발 분출 예약 ===
        if ((blood0Prefab || blood1Prefab) && sustainDuration > 0f)
            StartCoroutine(FootBloodSustain());

        // 모든 코루틴 종료
        StopAllCoroutines();

        // === 즉시 혈흔 버스트 ===
        SpawnBloodBurst(GetBodyCenterFallback(), burstBloodCount);

        // 이동/물리 완전 봉인
        StopHorizontal();
        if (rb)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.constraints = RigidbodyConstraints2D.FreezeAll;
            rb.simulated = false; // 물리 비활성
        }

        // 충돌/히트박스 차단
        if (body) body.enabled = false;
        ForceDisableMeleeHitbox();

        // 시각 정리
        if (sr) sr.color = Color.white;

        // 연출: Hit가 있으면 Hit→Death, 없으면 Death 바로
        if (anim && anim.HasClip("Hit"))
            PlayOnce("Hit", "Death", true);
        else
            PlayOnce("Death", null, true);

#if UNITY_EDITOR
        Debug.Log($"[Monster] Death start ({reason})", this);
#endif
        StartCoroutine(DeathDespawn());
    }

    private IEnumerator DeathDespawn()
    {
        float t = 0f;
        while (t < despawnDelay)
        {
            t += Time.deltaTime;
            yield return null;
        }
        Destroy(gameObject);
    }

    // === Blood VFX Helpers ===
    private Vector3 GetBodyCenterFallback()
    {
        if (body != null)
        {
            var b = body.bounds;
            return b.center;
        }
        return transform.position;
    }

    private Vector3 GetFeetWorldFallback()
    {
        if (body != null)
        {
            var b = body.bounds;
            return new Vector3(b.center.x, b.min.y + feetYOffset, transform.position.z);
        }
        // 대충 발 위치 추정 (콜라이더가 없을 경우)
        return transform.position + new Vector3(0f, -0.25f, 0f);
    }

    private void SpawnBloodBurst(Vector3 center, int count)
    {
        if (!blood0Prefab && !blood1Prefab) return;

        for (int i = 0; i < count; i++)
        {
            var prefab = (UnityEngine.Random.value < 0.5f || !blood1Prefab) ? blood0Prefab : blood1Prefab;
            if (!prefab) continue;

            Vector2 dir = UnityEngine.Random.insideUnitCircle.normalized;
            float dist = UnityEngine.Random.Range(0.05f, burstRadius);
            Vector3 pos = center + (Vector3)(dir * dist);

            var go = Instantiate(prefab, pos, Quaternion.identity);
            if (go.TryGetComponent<Rigidbody2D>(out var r2d))
            {
                float spd = UnityEngine.Random.Range(burstSpeedRange.x, burstSpeedRange.y);
                r2d.AddForce(dir * spd, ForceMode2D.Impulse);
                r2d.AddTorque(UnityEngine.Random.Range(-10f, 10f), ForceMode2D.Impulse);
            }
            if (bloodLifetime > 0f) Destroy(go, bloodLifetime);
        }
    }

    private IEnumerator FootBloodSustain()
    {
        // 시작 지연
        if (sustainDelay > 0f) yield return new WaitForSeconds(sustainDelay);

        float t = 0f;
        while (t < sustainDuration)
        {
            // 발 기준 위치 + 지터
            Vector2 jitter = UnityEngine.Random.insideUnitCircle * sustainJitter;
            Vector3 pos = _deathFeetPos + (Vector3)jitter;

            // 위쪽 반구 쏘기(자연스러운 분사)
            Vector2 dir = (Vector2.up + UnityEngine.Random.insideUnitCircle * 0.6f).normalized;
            float spd = UnityEngine.Random.Range(burstSpeedRange.x * 0.6f, burstSpeedRange.y);

            var prefab = (UnityEngine.Random.value < 0.5f || !blood1Prefab) ? blood0Prefab : blood1Prefab;
            if (prefab)
            {
                var go = Instantiate(prefab, pos, Quaternion.identity);
                if (go.TryGetComponent<Rigidbody2D>(out var r2d))
                {
                    r2d.AddForce(dir * spd, ForceMode2D.Impulse);
                    r2d.AddTorque(UnityEngine.Random.Range(-12f, 12f), ForceMode2D.Impulse);
                }
                if (bloodLifetime > 0f) Destroy(go, bloodLifetime);
            }

            float wait = UnityEngine.Random.Range(sustainIntervalRange.x, sustainIntervalRange.y);
            t += wait;
            yield return new WaitForSeconds(wait);
        }
    }

    // Death/Instant Kill 양쪽에서 히트박스 완전 비활성화
    private void ForceDisableMeleeHitbox()
    {
        if (!meleeHitbox) return;
        var hb = meleeHitbox.GetComponent<MeleeHitboxOnce>();
        if (hb) hb.Disarm();
        meleeHitbox.SetActive(false);
    }

    // === Monkill 즉사 ===
    private IEnumerator DieInstantByTag(string reason)
    {
        if (isDying) yield break;
        StartDeathSequence(reason);
        yield break;
    }

    // ============ Helpers ============
    private Vector2 Eyes()
    {
        var b = body.bounds;
        return new Vector2(b.center.x, b.max.y + 0.51f);
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
        if (IsDeadOrStopped) return;
        if (!target) return;

        if (target.TryGetComponent<IDamageable>(out var dmg))
        {
            dmg.TakeDamage(attackDamage, transform.position, new Vector2(dir, 0));
            return;
        }

        var dmgInParent = target.GetComponentInParent<IDamageable>();
        if (dmgInParent != null)
        {
            dmgInParent.TakeDamage(attackDamage, transform.position, new Vector2(dir, 0));
            return;
        }

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
                return false;
        }

        if (hit.collider && HasPlayerTagInParents(hit.collider.transform))
            return false;

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
            return false;

        return true;
    }
    private bool LedgeAhead(int d)
    {
        Vector2 origin = Feet() + new Vector2(d * (body.bounds.extents.x + ledgeForward), 0.02f);
        return !Physics2D.Raycast(origin, Vector2.down, ledgeDownDist, groundMask);
    }

    private Vector2 GetPatrolTarget() => (patrolTargetIndex == 0) ? wpA : wpB;
    private void TogglePatrolTarget() => patrolTargetIndex = (patrolTargetIndex == 0) ? 1 : 0;

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
        if (_ignoredPlayerRoots.Contains(id)) return;

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
        if (IsDeadOrStopped) return;

        if (!isDying && c.collider && c.collider.CompareTag(stopOnTag))
        {
            StartCoroutine(DieInstantByTag($"Collision with {stopOnTag}"));
            return;
        }

        TryIgnoreIfPlayer(c.collider);
    }

    private void OnCollisionStay2D(Collision2D c)
    {
        if (IsDeadOrStopped) return;
        TryIgnoreIfPlayer(c.collider);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (IsDeadOrStopped) return;

        if (!isDying && other && other.CompareTag(stopOnTag))
        {
            StartCoroutine(DieInstantByTag($"Trigger with {stopOnTag}"));
            return;
        }

        TryIgnoreIfPlayer(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        if (IsDeadOrStopped) return;
        TryIgnoreIfPlayer(other);
    }

    private void OnDisable()
    {
        if (attackCo != null) { StopCoroutine(attackCo); attackCo = null; }
        if (alertCo != null) { StopCoroutine(alertCo); alertCo = null; }
        ForceDisableMeleeHitbox();
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        Vector2 a = waypointA ? (Vector2)waypointA.position : (Vector2)transform.position + fallbackLocalA;
        Vector2 b = waypointB ? (Vector2)waypointB.position : (Vector2)transform.position + fallbackLocalB;

        Gizmos.color = Color.green; Gizmos.DrawSphere(a, 0.08f);
        Gizmos.color = Color.blue; Gizmos.DrawSphere(b, 0.08f);
        Gizmos.color = Color.yellow; Gizmos.DrawLine(a, b);

        Gizmos.color = new Color(1, 0, 0, 0.35f);
        Gizmos.DrawWireSphere(transform.position, attackEdgeRange);

#if UNITY_EDITOR
        if (enableDirectDetect)
        {
            Gizmos.color = new Color(1f, 0.4f, 0f, 0.25f);
            Gizmos.DrawWireSphere(transform.position, directDetectRadius);
        }
#endif
    }

    // ---------- 히트박스 좌/우 미러링 ----------
    private void PositionMeleeHitbox()
    {
        if (!meleeHitbox || !flipHitboxBySpriteFlip) return;

        int facing = (sr && sr.flipX) ? -1 : 1;

        meleeHitbox.transform.localPosition = new Vector3(
            Mathf.Abs(meleeOffset.x) * facing,
            meleeOffset.y,
            _meleeLocalPosZLocked.z
        );

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
