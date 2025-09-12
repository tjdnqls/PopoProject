using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
[DisallowMultipleComponent]
public class BackChaserFSM : MonoBehaviour, global::IDamageable
{
    public enum State { Patrol, Alert, Chase, AttackWindup, Attacking, AttackRecover, Return, Guard, Dead }

    [Header("Anim")]
    public SpriteAnimationManager anim;     // 선택
    public SpriteRenderer sr;
    [SerializeField] private string idleAnim = "Idle";
    [SerializeField] private string runAnim = "Run";
    [SerializeField] private string attackStartAnim = "AttackStart"; // 공격 대기(선행)
    [SerializeField] private string attackAnim = "Attack";           // 공격 본동작
    [SerializeField] private string blockAnim = "Block";
    [SerializeField] private string deathAnim = "Death";

    [Header("Refs")]
    [SerializeField] private Rigidbody2D rb;
    [SerializeField] private Collider2D body;

    [Header("Waypoints (A<->B)")]
    [SerializeField] private Transform waypointA;
    [SerializeField] private Transform waypointB;
    [SerializeField] private Vector2 fallbackLocalA = new Vector2(-3, 0);
    [SerializeField] private Vector2 fallbackLocalB = new Vector2(3, 0);
    [SerializeField] private float arriveEps = 0.08f;

    [Header("Move")]
    [SerializeField] private float patrolSpeed = 2.2f;
    [SerializeField] private float chaseSpeed = 3.8f;
    [SerializeField] private float accel = 25f;
    [SerializeField] private float stationarySpeedEps = 0.05f;

    [Header("Detect")]
    [SerializeField] private LayerMask playerMask;           // ★ P1/P2 레이어 포함!
    [SerializeField] private float detectRadius = 5f;
    [SerializeField] private float detectVerticalTolerance = 1.5f;
    [SerializeField] private string preferPlayerTag = "Player2"; // 둘 다 있으면 P2 우선
    [SerializeField] private float alertWaitSec = 1.0f;

    [Header("Give Up / Stuck")]
    [SerializeField] private float lostGiveUpSec = 2.0f;
    [SerializeField] private float stuckGiveUpSec = 2.0f;

    [Header("Obstacle Probe")]
    [SerializeField] private LayerMask obstacleMask;
    [SerializeField] private float wallCheckDist = 0.30f;
    [SerializeField] private float lowWallCheckDist = 0.25f;
    [SerializeField] private float feetYOffset = 0.05f;

    [Header("Attack")]
    [SerializeField] private int attackDamage = 1;
    [SerializeField] private float attackRange = 0.35f;            // 엣지 기준 사거리
    [SerializeField] private float attackHorizontalRange = 0.40f;  // 폴백 수평 사거리
    [SerializeField] private float attackVerticalTolerance = 0.9f;
    [SerializeField] private float attackWindupSec = 1.0f;         // 공격 대기 시간
    [SerializeField] private float attackRecoverSec = 0.5f;
    [SerializeField] private Color windupColor = new Color(1f, 0.2f, 0.2f, 1f);

    [Header("Attack Hitbox (child)")]
    [SerializeField] private GameObject attackHitbox;              // 자식 히트박스 GO(Trigger Collider2D 필수)
    [SerializeField] private float hitboxActiveSeconds = 0.20f;
    [SerializeField] private int hitboxDamage = 1;
    [SerializeField] private bool damageOnlyCurrentTarget = true;  // 현재 추적 대상만

    [Header("Back-Only Damage (입는 쪽) — 완화된 ‘뒤’ 인식")]
    [SerializeField] private bool backOnlyDamage = true;
    [SerializeField, Tooltip("뒤쪽 각도 한계(cosθ). -1(완전 뒤)~0(정면). 더 작을수록 '진짜 뒤'만 인정")]
    private float backConeCosMax = -0.85f;
    [SerializeField, Tooltip("내 뒤쪽으로 최소 얼마나 더 들어왔는지(X축 전후 성분, 월드유닛)")]
    private float backMinBehindX = 0.12f;

    [Header("Block by Position (Monkill)")]
    [SerializeField] private string monkillLayerName = "Monkill";
    [SerializeField] private string player1Tag = "Player1";
    [SerializeField] private float guardHoldSec = 0.5f;
    [SerializeField] private float blockNoCollisionSec = 0.35f;
    [SerializeField] private float guardShakeAmp = 0.4f;
    [SerializeField] private float guardShakeDur = 0.15f;
    [SerializeField] private float despawnDelay = 5f;

    [Header("AI Options")]
    [SerializeField, Tooltip("사거리 안에 들어온 아무 플레이어라도 발견되면 그 대상으로 즉시 공격")]
    private bool attackAnyPlayerInRange = true;
    [SerializeField, Tooltip("추격 중에도 선호 태그(예: Player2)로 타깃을 재지정할지")]
    private bool switchToPreferWhileChasing = false; // 기본: P1 추격 유지

    [Header("Debug View")]
    [SerializeField] private bool showAttackWindowGizmo = true;
    [SerializeField] private bool alwaysShowGizmo = true;
    [SerializeField] private Color attackGizmoColor = new Color(1f, 0f, 0f, 0.18f);     // 전방 사각
    [SerializeField] private Color attackGizmoEdgeColor = new Color(1f, 0f, 0f, 0.9f);
    [SerializeField] private Color fallbackGizmoColor = new Color(1f, 0.55f, 0f, 0.12f); // 폴백 윈도우
    [SerializeField] private Color fallbackGizmoEdgeColor = new Color(1f, 0.55f, 0f, 0.9f);
    [SerializeField] private bool drawGizmos = true;

    // ---- runtime ----
    private State state;
    private Transform currentTarget;
    private int dir = +1; // +1:우 / -1:좌 (시선)
    private int patrolTargetIndex; // 0:A, 1:B
    private float lastSeenTime;
    private float stuckTimer;
    private Color baseColor;
    private Coroutine logicCo;
    private int _monkillLayer;

    private bool _lookLocked; // 공격 준비 중 시선락
    private static readonly Collider2D[] _buf = new Collider2D[12];

    private bool isDying = false;

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
        baseColor = sr ? sr.color : Color.white;
        _monkillLayer = LayerMask.NameToLayer(monkillLayerName);

        patrolTargetIndex = (Vector2.SqrMagnitude((Vector2)transform.position - WpA()) <=
                             Vector2.SqrMagnitude((Vector2)transform.position - WpB())) ? 0 : 1;

        dir = ((GetPatrolTarget().x - transform.position.x) >= 0f) ? +1 : -1;
        state = State.Patrol;

        if (attackHitbox)
        {
            attackHitbox.SetActive(false);
            if (!attackHitbox.TryGetComponent<OneShotMeleeHitbox>(out _))
                attackHitbox.AddComponent<OneShotMeleeHitbox>();
        }
    }

    private void OnEnable()
    {
        Play(runAnim, true);
        SetFlipByDir(dir);
    }

    private void FixedUpdate()
    {
        if (state == State.Dead) { StopHorizontal(); return; }

        switch (state)
        {
            case State.Patrol: TickPatrol(); break;
            case State.Alert: TickAlert(); break;
            case State.Chase: TickChase(); break;
            case State.AttackWindup: StopHorizontal(); break;
            case State.Attacking: StopHorizontal(); break;
            case State.AttackRecover: TickAttackRecover(); break;
            case State.Return: TickReturn(); break;
            case State.Guard: StopHorizontal(); break;
        }
    }

    private void OnDisable()
    {
        if (logicCo != null) { StopCoroutine(logicCo); logicCo = null; }
    }

    // ---------- Waypoint helpers ----------
    private Vector2 WpA() => waypointA ? (Vector2)waypointA.position : (Vector2)transform.position + fallbackLocalA;
    private Vector2 WpB() => waypointB ? (Vector2)waypointB.position : (Vector2)transform.position + fallbackLocalB;
    private Vector2 GetPatrolTarget() => (patrolTargetIndex == 0) ? WpA() : WpB();
    private void TogglePatrolTarget() => patrolTargetIndex = (patrolTargetIndex == 0) ? 1 : 0;
    private Vector2 NearestHome()
    {
        Vector2 a = WpA(), b = WpB();
        float da = Vector2.SqrMagnitude((Vector2)transform.position - a);
        float db = Vector2.SqrMagnitude((Vector2)transform.position - b);
        return (da <= db) ? a : b;
    }

    // ---------- Ticks ----------
    private void TickPatrol()
    {
        Play(runAnim);
        Vector2 target = GetPatrolTarget();
        dir = (target.x >= transform.position.x) ? +1 : -1;
        SetFlipByDir(dir);
        MoveHorizontalTowards(dir * patrolSpeed);

        if (Mathf.Abs(target.x - transform.position.x) <= arriveEps) TogglePatrolTarget();

        if (FrontWall(dir) || AlmostStopped()) { TogglePatrolTarget(); dir = -dir; SetFlipByDir(dir); }

        if (TryPickTarget(out Transform t)) { currentTarget = t; EnterAlert(); }
    }

    private void TickAlert()
    {
        StopHorizontal();
    }

    private void TickChase()
    {
        Play(runAnim);

        // 선호 타깃 승격은 "추격 유지" 옵션을 우선 고려
        if (switchToPreferWhileChasing || currentTarget == null || !StillValidTarget(currentTarget) || currentTarget.root.CompareTag(preferPlayerTag) == false)
        {
            if (!(currentTarget && currentTarget.root.CompareTag(player1Tag) && StillValidTarget(currentTarget)))
            {
                PromotePreferTargetIfVisible();
            }
        }

        if (!StillValidTarget(currentTarget))
        {
            if (Time.time - lastSeenTime >= lostGiveUpSec) { EnterReturn(); return; }
        }
        else lastSeenTime = Time.time;

        int chaseDir = (currentTarget && currentTarget.position.x >= transform.position.x) ? +1 : -1;
        SetFlipByDir(chaseDir);

        if (FrontWall(chaseDir)) StopHorizontal();
        else MoveHorizontalTowards(chaseDir * chaseSpeed);

        stuckTimer = (AlmostStopped() || FrontWall(chaseDir)) ? stuckTimer + Time.fixedDeltaTime : 0f;
        if (stuckTimer >= stuckGiveUpSec) { EnterReturn(); return; }

        // ---- 공격 진입 로직 강화 ----
        if (currentTarget && WithinAttackWindow(currentTarget))
        {
            EnterAttackWindup();
            return;
        }
        if (attackAnyPlayerInRange && TryFindTargetInAttackWindow(out var nearTarget))
        {
            currentTarget = nearTarget;
            EnterAttackWindup();
        }
    }

    private void TickAttackRecover()
    {
        StopHorizontal();
    }

    private void TickReturn()
    {
        Play(runAnim);

        int retDir = (NearestHome().x >= transform.position.x) ? +1 : -1;
        SetFlipByDir(retDir);
        if (FrontWall(retDir)) StopHorizontal();
        else MoveHorizontalTowards(retDir * patrolSpeed);

        if (Mathf.Abs(NearestHome().x - transform.position.x) <= arriveEps)
            state = State.Patrol;

        if (TryPickTarget(out Transform t)) { currentTarget = t; EnterAlert(); }
    }

    // ---------- State enter ----------
    private void EnterAlert()
    {
        state = State.Alert;
        StopHorizontal();
        Play(idleAnim, true);
        if (logicCo != null) StopCoroutine(logicCo);
        logicCo = StartCoroutine(AlertWaitThenChaseOrAttack());
    }

    private IEnumerator AlertWaitThenChaseOrAttack()
    {
        float t = 0f;
        while (t < alertWaitSec) { t += Time.fixedDeltaTime; yield return new WaitForFixedUpdate(); }

        if (currentTarget && StillValidTarget(currentTarget) && WithinAttackWindow(currentTarget))
            EnterAttackWindup();
        else
            EnterChase();
    }

    private void EnterChase()
    {
        state = State.Chase;
        stuckTimer = 0f;
        lastSeenTime = Time.time;
        Play(runAnim, true);
    }

    private void EnterReturn()
    {
        state = State.Return;
        currentTarget = null;
        Play(runAnim, true);
    }

    private void EnterAttackWindup()
    {
        if (logicCo != null) StopCoroutine(logicCo);
        state = State.AttackWindup;
        logicCo = StartCoroutine(AttackRoutine());
    }

    private IEnumerator AttackRoutine()
    {
        // ---------- 커밋 스냅샷: 이번 한 번은 무조건 휘두른다 ----------
        Transform commitTargetRoot = currentTarget ? currentTarget.root : null;

        int lockedDir = 0;
        if (commitTargetRoot)
            lockedDir = (commitTargetRoot.position.x >= transform.position.x) ? +1 : -1;
        else if (currentTarget)
            lockedDir = (currentTarget.position.x >= transform.position.x) ? +1 : -1;
        else
            lockedDir = dir != 0 ? dir : +1;

        ForceFlipByDir(lockedDir);
        dir = lockedDir;
        _lookLocked = true;

        // 1) 공격 대기(취소 없음)
        Color start = sr ? sr.color : Color.white;
        float t = 0f;
        Play(attackStartAnim, true);
        while (t < attackWindupSec)
        {
            t += Time.fixedDeltaTime;
            if (sr) sr.color = Color.Lerp(start, windupColor, Mathf.Clamp01(t / attackWindupSec));
            yield return new WaitForFixedUpdate();

            if (state == State.Dead) { if (sr) sr.color = start; _lookLocked = false; yield break; }
            // ※ 더 이상 타깃 유효성/사거리로 취소하지 않음 (최소 1회 보장)
        }
        if (sr) sr.color = baseColor;

        // 2) 공격 본동작 + 히트박스 on
        state = State.Attacking;
        Play(attackAnim, true);

        if (attackHitbox != null)
        {
            var hb = attackHitbox.GetComponent<OneShotMeleeHitbox>();
            hb.Configure(
                ownerRoot: transform.root,
                playerMask: playerMask,
                damage: hitboxDamage,
                // 커밋 타깃 기준으로 한 번은 확실히 공격
                onlyTarget: damageOnlyCurrentTarget ? commitTargetRoot : null
            );

            attackHitbox.SetActive(true);
            yield return new WaitForSeconds(hitboxActiveSeconds);
            attackHitbox.SetActive(false);
        }
        else
        {
            // 히트박스 없을 때도 커밋 타깃 우선
            if (commitTargetRoot) ApplyDamage(commitTargetRoot);
            else if (currentTarget) ApplyDamage(currentTarget);
            yield return new WaitForSeconds(hitboxActiveSeconds);
        }

        // 3) 쿨다운
        state = State.AttackRecover;
        float r = 0f;
        while (r < attackRecoverSec) { r += Time.fixedDeltaTime; yield return new WaitForFixedUpdate(); }
        _lookLocked = false;

        // 4) 재결정
        if (currentTarget && StillValidTarget(currentTarget))
        {
            if (WithinAttackWindow(currentTarget)) EnterAttackWindup();
            else EnterChase();
        }
        else EnterReturn();
    }

    // ---------- Detect / target pick ----------
    private bool TryPickTarget(out Transform best)
    {
        best = null;
        var filter = new ContactFilter2D { useLayerMask = true, layerMask = playerMask, useTriggers = true };
        int n = Physics2D.OverlapCircle((Vector2)transform.position, detectRadius, filter, _buf);
        if (n <= 0) return false;

        Transform prefer = null; float preferBest = float.PositiveInfinity;
        Transform nearest = null; float nearBest = float.PositiveInfinity;

        for (int i = 0; i < n; i++)
        {
            var c = _buf[i]; if (!c) continue;
            var t = c.attachedRigidbody ? c.attachedRigidbody.transform : c.transform;
            float vy = Mathf.Abs(t.position.y - transform.position.y); if (vy > detectVerticalTolerance) continue;
            float d = ((Vector2)t.position - (Vector2)transform.position).sqrMagnitude;

            if (!string.IsNullOrEmpty(preferPlayerTag) && t.root.CompareTag(preferPlayerTag))
            { if (d < preferBest) { preferBest = d; prefer = t; } }

            if (d < nearBest) { nearBest = d; nearest = t; }
        }

        best = prefer ? prefer : nearest;
        if (best) lastSeenTime = Time.time;
        return best != null;
    }

    private void PromotePreferTargetIfVisible()
    {
        if (string.IsNullOrEmpty(preferPlayerTag)) return;

        // 현재 유효한 타깃이 있고 P1이면 유지
        if (currentTarget && StillValidTarget(currentTarget) && currentTarget.root.CompareTag(player1Tag))
            return;

        var filter = new ContactFilter2D { useLayerMask = true, layerMask = playerMask, useTriggers = true };
        int n = Physics2D.OverlapCircle((Vector2)transform.position, detectRadius, filter, _buf);
        if (n <= 0) return;

        Transform prefer = null; float best = float.PositiveInfinity;
        for (int i = 0; i < n; i++)
        {
            var c = _buf[i]; if (!c) continue;
            var t = c.attachedRigidbody ? c.attachedRigidbody.transform : c.transform;
            if (!t.root.CompareTag(preferPlayerTag)) continue;
            float vy = Mathf.Abs(t.position.y - transform.position.y); if (vy > detectVerticalTolerance) continue;
            float d = ((Vector2)t.position - (Vector2)transform.position).sqrMagnitude;
            if (d < best) { best = d; prefer = t; }
        }
        if (prefer && prefer != currentTarget) { currentTarget = prefer; lastSeenTime = Time.time; }
    }

    private bool TryFindTargetInAttackWindow(out Transform best)
    {
        best = null;
        var filter = new ContactFilter2D { useLayerMask = true, layerMask = playerMask, useTriggers = true };
        int n = Physics2D.OverlapCircle((Vector2)transform.position, detectRadius, filter, _buf);
        if (n <= 0) return false;

        Transform prefer = null; float preferBest = float.PositiveInfinity;
        Transform nearest = null; float nearBest = float.PositiveInfinity;

        for (int i = 0; i < n; i++)
        {
            var c = _buf[i]; if (!c) continue;
            var t = c.attachedRigidbody ? c.attachedRigidbody.transform : c.transform;

            // 사거리/수직 허용치 검사
            if (!WithinAttackWindow(t)) continue;

            float d = ((Vector2)t.position - (Vector2)transform.position).sqrMagnitude;

            if (!string.IsNullOrEmpty(preferPlayerTag) && t.root.CompareTag(preferPlayerTag))
            {
                if (d < preferBest) { preferBest = d; prefer = t; }
            }

            if (d < nearBest) { nearBest = d; nearest = t; }
        }

        best = prefer ? prefer : nearest;
        return best != null;
    }

    private bool StillValidTarget(Transform t)
    {
        if (!t) return false;
        if (((1 << t.gameObject.layer) & playerMask) == 0) return false;
        if (Vector2.Distance(t.position, transform.position) > detectRadius) return false;
        if (Mathf.Abs(t.position.y - transform.position.y) > detectVerticalTolerance) return false;
        return true;
    }

    // ---------- Damage out ----------
    private void ApplyDamage(Transform t)
    {
        if (!t) return;

        var dmg3 = GetAnyDamageable(t);
        if (dmg3 != null)
        {
            Vector2 hitPoint = body ? (Vector2)body.bounds.center : (Vector2)transform.position;
            Vector2 hitNormal = new Vector2(dir, 0);
            dmg3.TakeDamage(attackDamage, hitPoint, hitNormal);
            return;
        }

        t.SendMessage("TakeDamage", attackDamage, SendMessageOptions.DontRequireReceiver);
        t.SendMessage("OnHit", attackDamage, SendMessageOptions.DontRequireReceiver);
    }

    private global::IDamageable GetAnyDamageable(Transform t)
    {
        var d = t.GetComponentInParent<global::IDamageable>();
        if (d != null) return d;
        return t.GetComponentInChildren<global::IDamageable>();
    }

    // ---------- Damage in (뒤에서만 맞음: 완화된 백어택) ----------
    public void TakeDamage(int amount, Vector2 hitPoint, Vector2 hitNormal)
    {
        if (!backOnlyDamage) { KillImmediate(); return; }

        Vector2 fwd = new Vector2(dir, 0f).normalized;
        Vector2 toHit = ((Vector2)hitPoint - (Vector2)transform.position);

        float dotDir = Vector2.Dot(fwd, toHit.normalized);
        float longProj = Vector2.Dot(toHit, fwd);

        bool deepBehind = (longProj <= -backMinBehindX);
        bool inBackCone = (dotDir <= backConeCosMax);

        if (deepBehind && inBackCone)
        {
            KillImmediate();
        }
    }

    public void OnHit(int damage) { }

    // ---------- Monkill 충돌(가드/즉사) ----------
    private void OnCollisionEnter2D(Collision2D c)
    {
        if (state == State.Dead || isDying) return;
        if (c.collider.gameObject.layer == _monkillLayer) HandleMonkillHit(c.collider);
    }
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (state == State.Dead || isDying) return;
        if (other.gameObject.layer == _monkillLayer) HandleMonkillHit(other);
    }

    // 전방 판정 유틸
    private bool IsAheadOfMe(Vector2 worldPoint, float cosThreshold = 0.2f)
    {
        Vector2 fwd = new Vector2(dir, 0f);
        Vector2 to = (worldPoint - (Vector2)transform.position);
        if (to.sqrMagnitude < 1e-6f) return true;
        float dot = Vector2.Dot(fwd, to.normalized);
        return dot >= cosThreshold;
    }

    private void HandleMonkillHit(Collider2D monkillCol)
    {
        Transform p1 = FindPlayer1Root(monkillCol.transform);

        bool aheadByDir = IsAheadOfMe(monkillCol.bounds.center);
        int moveSign = GetMoveSign();

        if (p1)
        {
            int relSign = Mathf.Sign(p1.position.x - transform.position.x) >= 0 ? +1 : -1;
            bool playerAhead = aheadByDir || ((moveSign != 0) ? (relSign == moveSign) : (relSign == dir));

            if (playerAhead)
            {
                if (logicCo != null) { StopCoroutine(logicCo); logicCo = null; }
                StartCoroutine(BlockRoutine(p1, monkillCol));
                return;
            }
            StartDeathSequence("MonkillBehind");
            return;
        }

        if (aheadByDir)
        {
            if (logicCo != null) { StopCoroutine(logicCo); logicCo = null; }
            StartCoroutine(BlockRoutine(null, monkillCol));
        }
        else
        {
            StartDeathSequence("MonkillBehind");
        }
    }

    private int GetMoveSign()
    {
        if (rb)
        {
            float vx = rb.linearVelocity.x;
            if (Mathf.Abs(vx) > 0.001f) return vx >= 0 ? +1 : -1;
        }
        return 0;
    }

    private IEnumerator BlockRoutine(Transform playerRoot, Collider2D optionalMonkill = null)
    {
        state = State.Guard;
        CameraShaker.Shake(guardShakeAmp, guardShakeDur);
        Play(blockAnim, true);

        var prevConstraints = rb ? rb.constraints : RigidbodyConstraints2D.None;
        if (rb)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            rb.constraints = RigidbodyConstraints2D.FreezeRotation | RigidbodyConstraints2D.FreezePositionX;
        }

        float collisionOffSec = Mathf.Max(blockNoCollisionSec, guardHoldSec);
        List<Collider2D> cached = null;

        if (body)
        {
            if (playerRoot)
            {
                cached = new List<Collider2D>(playerRoot.GetComponentsInChildren<Collider2D>(true));
                foreach (var pc in cached) if (pc) Physics2D.IgnoreCollision(body, pc, true);
            }
            if (optionalMonkill)
            {
                Physics2D.IgnoreCollision(body, optionalMonkill, true);
            }
        }

        yield return new WaitForSeconds(guardHoldSec);

        if (collisionOffSec > guardHoldSec)
            yield return new WaitForSeconds(collisionOffSec - guardHoldSec);

        if (body)
        {
            if (cached != null) foreach (var pc in cached) if (pc) Physics2D.IgnoreCollision(body, pc, false);
            if (optionalMonkill) Physics2D.IgnoreCollision(body, optionalMonkill, false);
        }

        if (rb) rb.constraints = prevConstraints;

        if (!TryPickTarget(out var t)) t = playerRoot;
        currentTarget = t;
        EnterChase();
    }

    private Transform FindPlayer1Root(Transform from)
    {
        for (Transform t = from; t != null; t = t.parent)
            if (t.CompareTag(player1Tag)) return t;
        return null;
    }

    // ---------- Death sequence ----------
    private void StartDeathSequence(string reason)
    {
        if (isDying) return;
        isDying = true;

        StopAllCoroutines();
        state = State.Dead;

        StopHorizontal();
        if (sr) sr.color = baseColor;

        if (rb)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.constraints = RigidbodyConstraints2D.FreezeAll;
            rb.simulated = false;
        }
        if (body) body.enabled = false;

        Play(deathAnim, true);
        StartCoroutine(DespawnAfter(despawnDelay));
    }

    private IEnumerator DespawnAfter(float sec)
    {
        float t = 0f;
        while (t < sec) { t += Time.deltaTime; yield return null; }
        Destroy(gameObject);
    }

    private void KillImmediate() => StartDeathSequence("Damage");

    // ---------- Movement / probes ----------
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
    private bool AlmostStopped() => Mathf.Abs(rb.linearVelocity.x) < stationarySpeedEps;

    private bool FrontWall(int d)
    {
        if (!body) return false;
        Bounds b = body.bounds;
        Vector2 originHigh = new Vector2(b.center.x + d * (b.extents.x + 0.02f), b.center.y + 0.15f);
        Vector2 originLow = new Vector2(b.center.x + d * (b.extents.x + 0.02f), b.min.y + feetYOffset);
        bool high = Physics2D.Raycast(originHigh, Vector2.right * d, wallCheckDist, obstacleMask);
        bool low = Physics2D.Raycast(originLow, Vector2.right * d, lowWallCheckDist, obstacleMask);
        return high || low;
    }

    private void SetFlipByDir(int d)
    {
        if (_lookLocked) return;
        if (sr) sr.flipX = d < 0;
    }
    private void ForceFlipByDir(int d)
    {
        if (sr) sr.flipX = d < 0;
    }

    // ---------- Attack window ----------
    private bool WithinAttackWindow(Transform t)
    {
        if (!t || !body) return false;

        // 루트의 모든 콜라이더 중 playerMask에 포함되고 가장 가까운 콜라이더로 판단
        if (ClosestTargetCollider(t, out var bestCol, out float edge, out float vy))
        {
            if (vy > attackVerticalTolerance) return false;
            return edge <= attackRange;
        }

        // 콜라이더를 못 찾는 희귀 케이스: 폴백
        float vy2 = Mathf.Abs(t.position.y - transform.position.y);
        if (vy2 > attackVerticalTolerance) return false;
        float hx = Mathf.Abs(t.position.x - transform.position.x);
        return hx <= attackHorizontalRange;
    }

    // 루트 아래의 모든 Collider2D 중에서 body와의 최소 에지 거리 콜라이더를 찾는다.
    private bool ClosestTargetCollider(Transform target, out Collider2D bestCol, out float minEdgeDist, out float vyAbs)
    {
        bestCol = null; minEdgeDist = float.PositiveInfinity; vyAbs = float.PositiveInfinity;
        if (!target || !body) return false;

        var cols = target.root.GetComponentsInChildren<Collider2D>(true);
        if (cols == null || cols.Length == 0) return false;

        Vector2 myCenter = body.bounds.center;
        int mask = playerMask.value;

        foreach (var tc in cols)
        {
            if (!tc || !tc.enabled) continue;
            // 플레이어 허트박스 레이어만 사용
            if (((1 << tc.gameObject.layer) & mask) == 0) continue;

            var dist = Physics2D.Distance(body, tc);
            float edge = dist.isOverlapped ? 0f : dist.distance;
            float vy = Mathf.Abs(tc.bounds.center.y - myCenter.y);

            if (edge < minEdgeDist)
            {
                minEdgeDist = edge;
                vyAbs = vy;
                bestCol = tc;
            }
        }
        return bestCol != null;
    }

    private Collider2D GetAnyCollider2D(Transform t)
    {
        var c = t.GetComponent<Collider2D>();
        if (c) return c;
        c = t.GetComponentInChildren<Collider2D>(true);
        if (c) return c;
        return t.GetComponentInParent<Collider2D>();
    }

    private void Play(string key, bool force = false)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (anim != null) anim.Play(key, force);
    }

    // ---- Gizmos (공격 범위 시각화) ----
    private void DrawAttackWindowGizmos()
    {
        if (!showAttackWindowGizmo) return;

        Collider2D col = body ? body : GetComponent<Collider2D>();
        if (!col) return;

        float fwd = Application.isPlaying ? (dir >= 0 ? 1f : -1f) : 1f;
        Bounds b = col.bounds;

        // 전방 사각(거리기반 attackRange 근사)
        Vector3 frontSize = new Vector3(
            Mathf.Max(0.02f, attackRange),
            Mathf.Max(0.02f, attackVerticalTolerance * 2f),
            0.1f);
        Vector3 frontCenter = new Vector3(
            b.center.x + fwd * (b.extents.x + frontSize.x * 0.5f),
            transform.position.y,
            0f);

        Gizmos.color = attackGizmoColor;
        Gizmos.DrawCube(frontCenter, frontSize);
        Gizmos.color = attackGizmoEdgeColor;
        Gizmos.DrawWireCube(frontCenter, frontSize);

        // 폴백 윈도우(수평거리/수직허용치)
        Vector3 fbSize = new Vector3(
            Mathf.Max(0.02f, attackHorizontalRange * 2f),
            Mathf.Max(0.02f, attackVerticalTolerance * 2f),
            0.1f);
        Vector3 fbCenter = new Vector3(transform.position.x, transform.position.y, 0f);

        Gizmos.color = fallbackGizmoColor;
        Gizmos.DrawCube(fbCenter, fbSize);
        Gizmos.color = fallbackGizmoEdgeColor;
        Gizmos.DrawWireCube(fbCenter, fbSize);
    }

    private void OnDrawGizmos()
    {
        if (alwaysShowGizmo) DrawAttackWindowGizmos();
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
        Gizmos.DrawWireSphere(transform.position, detectRadius);

        DrawAttackWindowGizmos();
    }
}
