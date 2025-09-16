// ===================== MonsterABPatrolFSM.cs =====================
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class MonsterABPatrolFSM : MonoBehaviour, IDamageable
{
    public enum State { Patrol, Alert, Chase, AttackWindup, Return, Dead }

    [Header("Animation")]
    public SpriteAnimationManager anim;

    [Header("Refs")]
    [SerializeField] private Rigidbody2D rb;
    [SerializeField] private Collider2D body;
    [SerializeField] private SpriteRenderer sr;
   
    [Header("Sound")]

    public AudioClip AttackSound;
    private AudioSource audioSource;
    public AudioClip DeathSound;

    [SerializeField] private State currentState;
    public State CurrentState => currentState; // 외부에서 읽기만 가능

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

    // ====== 감지 규칙 (LOS + 높이) ======
    [Header("Detect (LOS + Height)")]
    [SerializeField] private float detectRadius = 6f;
    [SerializeField, Tooltip("Ground/Obstacle가 사이에 있으면 감지 불가")]
    private bool requireLineOfSight = true;
    [SerializeField, Tooltip("시야를 막는 레이어 (대개 Ground | Obstacle)")]
    private LayerMask losBlockMask;
    [SerializeField, Tooltip("몬스터 기준 위로 이 값(유닛) 초과면 감지 안 함")]
    private float ignoreIfHigherThan = 5f;   // ≈ 5블럭
    [SerializeField, Tooltip("몬스터 기준 아래로 이 값(유닛) 초과면 감지 안 함")]
    private float ignoreIfLowerThan = 1f;   // ≈ 1블럭

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

    [Header("Death VFX (Blood)")]
    [SerializeField] private GameObject blood0Prefab;
    [SerializeField] private GameObject blood1Prefab;
    [SerializeField] private int burstBloodCount = 10;
    [SerializeField] private float burstRadius = 0.35f;
    [SerializeField] private Vector2 burstSpeedRange = new Vector2(1.2f, 3.0f);
    [SerializeField] private float sustainDelay = 0.3f;
    [SerializeField] private float sustainDuration = 3.0f;
    [SerializeField] private Vector2 sustainIntervalRange = new Vector2(0.06f, 0.20f);
    [SerializeField] private float sustainJitter = 0.06f;
    [SerializeField] private float bloodLifetime = 3.0f;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    // ---------- Return 길막 공격 ----------
    [Header("Return Block Attack")]
    [SerializeField] private float returnBlockCheckDist = 0.9f;
    [SerializeField] private float returnBlockYTolerance = 0.9f;
    [SerializeField] private float returnBlockedAttackDelay = 2f;
    private float returnBlockTimer = 0f;
    private Transform returnBlockingPlayer = null;

    // 내부 상태
    private State state;
    private Vector2 wpA, wpB;
    private int patrolTargetIndex; // 0:A, 1:B
    private int dir;
    private Vector2 homePos;
    private float chaseStartTime;
    private Coroutine alertCo, attackCo;

    private static readonly Collider2D[] _hits = new Collider2D[16];
    private Transform currentTarget;
    private LayerMask blockingMask;
    private bool isDying = false;

    private readonly HashSet<int> _ignoredPlayerRoots = new HashSet<int>();
    private bool IsDeadOrStopped => isDying || isStoppedByTag || state == State.Dead;

    private Vector3 _deathPos;
    private Quaternion _deathRot;
    private Vector3 _deathFeetPos;

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

        // 기본 LOS 차단 마스크가 비어있으면 Ground|Obstacle 사용
        if (losBlockMask == 0) losBlockMask = groundMask | obstacleMask;

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

        // LOS + 높이 규칙으로 감지
        if (TryDetectNearestWithRules(out Transform p))
        {
            currentTarget = p;
            EnterAlert();
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

        if (AttackSound)
        {
            Debug.Log("공격 소리 재생됌");
            var audioSource = GetComponent<AudioSource>();
            if (audioSource)
                audioSource.PlayOneShot(AttackSound);
        }

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

        if (Mathf.Abs(homePos.x - transform.position.x) <= arriveEps)
            KickstartPatrolLoop();

        // 복귀 중에도 감지
        if (TryDetectNearestWithRules(out Transform p))
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

    // ============ Detect (LOS + Height) ============
    private bool TryDetectNearestWithRules(out Transform nearest)
    {
        nearest = null;

        var filter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = playerMask,
            useTriggers = true
        };

        int n = Physics2D.OverlapCircle((Vector2)transform.position, detectRadius, filter, _hits);
        if (n <= 0) return false;

        float best = float.PositiveInfinity;
        for (int i = 0; i < n; i++)
        {
            var c = _hits[i];
            if (!c) continue;
            Transform t = c.attachedRigidbody ? c.attachedRigidbody.transform : c.transform;
            if (!t) continue;

            // 높이 규칙
            float dy = t.position.y - transform.position.y;
            if (dy > ignoreIfHigherThan) continue;
            if (dy < -ignoreIfLowerThan) continue;

            // LOS 규칙
            if (requireLineOfSight && !HasLineOfSightTo(t)) continue;

            float d = ((Vector2)t.position - (Vector2)transform.position).sqrMagnitude;
            if (d < best) { best = d; nearest = t; }
        }
        return nearest != null;
    }

    private bool HasLineOfSightTo(Transform t)
    {
        if (!t) return false;

        Vector2 from = body ? (Vector2)body.bounds.center : (Vector2)transform.position;
        Vector2 to;
        if (t.TryGetComponent<Collider2D>(out var tc))
            to = (Vector2)tc.bounds.center;
        else
            to = (Vector2)t.position;

        Vector2 dir = to - from;
        float dist = dir.magnitude;
        if (dist < 0.001f) return true;
        dir /= dist;

        var hit = Physics2D.Raycast(from, dir, dist, losBlockMask);
        return !hit; // 막히지 않으면 true
    }

    // ============ Helpers / Movement / Probes ============
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

        // Detect 반경 표시
        Gizmos.color = new Color(1f, 0.4f, 0f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, detectRadius);
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

    // ============ Damage / Death ============
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
    private void ForceDisableMeleeHitbox()
    {
        if (!meleeHitbox) return;

        // 한 번용 히트박스 스크립트가 붙어있다면 무장 해제
        var hb = meleeHitbox.GetComponent<MeleeHitboxOnce>();
        if (hb != null) hb.Disarm();

        // 히트박스 오브젝트 자체 비활성화
        meleeHitbox.SetActive(false);
    }
    private void StartDeathSequence(string reason)
    {
        if (isDying) return;
        isDying = true;
        state = State.Dead;

        _deathPos = transform.position;
        _deathRot = transform.rotation;
        _deathFeetPos = GetFeetWorldFallback();

        // VFX
        if ((blood0Prefab || blood1Prefab) && sustainDuration > 0f)
            StartCoroutine(FootBloodSustain());

        StopAllCoroutines();

        SpawnBloodBurst(GetBodyCenterFallback(), burstBloodCount);

        // 물리/충돌 봉인
        StopHorizontal();
        if (rb)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.constraints = RigidbodyConstraints2D.FreezeAll;
            rb.simulated = false;
        }

        if (body) body.enabled = false;
        ForceDisableMeleeHitbox();
        if (sr) sr.color = Color.white;

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
        // 1. 사운드 재생 (Destroy와 상관없이 재생)
        if (DeathSound != null) // DeathSound는 몬스터 사망시 재생할 AudioClip
        {
            AudioSource.PlayClipAtPoint(DeathSound, transform.position);
            // 또는 PlaySoundIndependent(DeathSound); <- 위에서 만든 함수 사용 가능
            Debug.Log("사망소리 재생됨");
        }

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
        if (sustainDelay > 0f) yield return new WaitForSeconds(sustainDelay);

        float t = 0f;
        while (t < sustainDuration)
        {
            Vector2 jitter = UnityEngine.Random.insideUnitCircle * sustainJitter;
            Vector3 pos = _deathFeetPos + (Vector3)jitter;

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

    // === Monkill 즉사 ===
    private IEnumerator DieInstantByTag(string reason)
    {
        if (isDying) yield break;
        StartDeathSequence(reason);
        yield break;
    }

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
}

// 선택형 인터페이스
public interface IDamageable
{
    void TakeDamage(int amount, Vector2 hitPoint, Vector2 hitNormal);
}
