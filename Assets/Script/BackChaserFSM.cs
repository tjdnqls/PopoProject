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
    [SerializeField] private string attackStartAnim = "AttackStart";
    [SerializeField] private string attackAnim = "Attack";
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
    [SerializeField] private LayerMask playerMask;
    [SerializeField] private float detectRadius = 5f;
    [SerializeField] private float detectVerticalTolerance = 1.5f;
    [SerializeField] private string preferPlayerTag = "Player2";
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
    [SerializeField] private float attackRange = 0.35f;
    [SerializeField] private float attackHorizontalRange = 0.40f;
    [SerializeField] private float attackVerticalTolerance = 0.9f;
    [SerializeField] private float attackWindupSec = 1.0f;
    [SerializeField] private float attackRecoverSec = 0.5f;
    [SerializeField] private Color windupColor = new Color(1f, 0.2f, 0.2f, 1f);

    [Header("Attack Hitbox (child)")]
    [SerializeField] private GameObject attackHitbox;
    [SerializeField] private float hitboxActiveSeconds = 0.20f;
    [SerializeField] private int hitboxDamage = 1;
    [SerializeField] private bool damageOnlyCurrentTarget = true;

    [Header("Back-Only Damage (입는 쪽) — 완화된 ‘뒤’ 인식")]
    [SerializeField] private bool backOnlyDamage = true;
    [SerializeField, Tooltip("뒤쪽 각도 한계(cosθ). -1(완전 뒤)~0(정면)")]
    private float backConeCosMax = -0.85f;
    [SerializeField, Tooltip("내 뒤쪽으로 최소 얼마나 들어왔는지(X축 성분)")]
    private float backMinBehindX = 0.12f;

    [Header("Backstab Correction")]
    [SerializeField, Tooltip("노멀 기반 전방 접근 판정 임계치(cos). 전방 흔적이면 뒤판정 무효")]
    [Range(-1f, 1f)] private float frontNormalCosMin = 0.1f;

    [Header("Block by Position (Monkill)")]
    [SerializeField] private string monkillLayerName = "Monkill";
    [SerializeField] private string player1Tag = "Player1";
    [SerializeField] private float guardHoldSec = 0.5f;
    [SerializeField] private float blockNoCollisionSec = 0.35f;
    [SerializeField] private float guardShakeAmp = 0.4f;
    [SerializeField] private float guardShakeDur = 0.15f;
    [SerializeField] private float despawnDelay = 5f;

    [Header("Monkill Front/Back Tuning")]
    [SerializeField, Tooltip("접촉 법선이 전방과 이 값 이상이면 '정면'으로 본다")]
    private float frontDotThreshold = 0.15f;
    [SerializeField, Tooltip("에지 전후 투영 버퍼. 이 값 이상이면 정면으로 본다")]
    private float frontEdgeBuffer = 0.06f;

    [Header("AI Options")]
    [SerializeField] private bool attackAnyPlayerInRange = true;
    [SerializeField] private bool switchToPreferWhileChasing = false;

    [Header("Debug View")]
    [SerializeField] private bool showAttackWindowGizmo = true;
    [SerializeField] private bool alwaysShowGizmo = true;
    [SerializeField] private Color attackGizmoColor = new Color(1f, 0f, 0f, 0.18f);
    [SerializeField] private Color attackGizmoEdgeColor = new Color(1f, 0f, 0f, 0.9f);
    [SerializeField] private Color fallbackGizmoColor = new Color(1f, 0.55f, 0f, 0.12f);
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
        if (sr) sr.color = baseColor;
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

    private void TickAlert() { StopHorizontal(); }

    private void TickChase()
    {
        Play(runAnim);

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

    private void TickAttackRecover() { StopHorizontal(); }

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

        Color start = sr ? sr.color : Color.white;
        float t = 0f;
        Play(attackStartAnim, true);
        if (attackHitbox && attackHitbox.activeSelf) attackHitbox.SetActive(false);

        while (t < attackWindupSec)
        {
            t += Time.fixedDeltaTime;
            if (sr) sr.color = Color.Lerp(start, windupColor, Mathf.Clamp01(t / attackWindupSec));
            yield return new WaitForFixedUpdate();

            if (state == State.Dead) { if (sr) sr.color = start; _lookLocked = false; yield break; }
        }
        if (sr) sr.color = baseColor;

        state = State.Attacking;
        Play(attackAnim, true);

        if (attackHitbox != null)
        {
            var hb = attackHitbox.GetComponent<OneShotMeleeHitbox>();
            hb.Configure(
                ownerRoot: transform.root,
                playerMask: playerMask,
                damage: hitboxDamage,
                onlyTarget: damageOnlyCurrentTarget ? commitTargetRoot : null
            );

            attackHitbox.SetActive(true);
            yield return new WaitForSeconds(hitboxActiveSeconds);
            attackHitbox.SetActive(false);
        }
        else
        {
            if (commitTargetRoot) ApplyDamage(commitTargetRoot);
            else if (currentTarget) ApplyDamage(currentTarget);
            yield return new WaitForSeconds(hitboxActiveSeconds);
        }

        state = State.AttackRecover;
        float r = 0f;
        while (r < attackRecoverSec) { r += Time.fixedDeltaTime; yield return new WaitForFixedUpdate(); }
        _lookLocked = false;

        if (currentTarget && StillValidTarget(currentTarget))
        {
            if (WithinAttackWindow(currentTarget)) EnterAttackWindup();
            else EnterChase();
        }
        else EnterReturn();
    }

    // ---------- 공격 즉시 취소(가드 인터럽트용) ----------
    private void CancelAttackIfAny()
    {
        if (state == State.AttackWindup || state == State.Attacking || state == State.AttackRecover)
        {
            if (logicCo != null) { StopCoroutine(logicCo); logicCo = null; }
            if (sr) sr.color = baseColor;
            if (attackHitbox && attackHitbox.activeSelf) attackHitbox.SetActive(false);
            _lookLocked = false;
        }
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

    // ---------- Damage in (뒤에서만 맞음: 보정된 백어택) ----------
    public void TakeDamage(int amount, Vector2 hitPoint, Vector2 hitNormal)
    {
        if (!backOnlyDamage) { KillImmediate(); return; }

        if (IsRearHitCorrected(hitPoint, hitNormal))
        {
            KillImmediate();
        }
        // 정면/측면/압착 상황이면 무효
    }

    private bool IsRearHitCorrected(Vector2 hitPoint, Vector2 hitNormal)
    {
        Vector2 fwd = new Vector2(dir, 0f).normalized;

        Vector2 cp = body ? body.ClosestPoint(hitPoint) : hitPoint;
        Vector2 toEdge = cp - (Vector2)transform.position;

        float longProj = Vector2.Dot(toEdge, fwd);

        float cosEdge = (toEdge.sqrMagnitude > 1e-6f)
            ? Vector2.Dot(fwd, toEdge.normalized)
            : 1f;

        bool frontByNormal = false;
        if (hitNormal.sqrMagnitude > 1e-6f)
        {
            Vector2 attackDir = (-hitNormal).normalized;
            float cosN = Vector2.Dot(fwd, attackDir);
            frontByNormal = (cosN >= frontNormalCosMin);
        }

        bool deepBehind = (longProj <= -backMinBehindX);
        bool inBackCone = (cosEdge <= backConeCosMax);

        if (frontByNormal) return false;
        return deepBehind && inBackCone;
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

    // ★★ 새 전방 판정: 접촉 법선/에지 기반으로 겹침에서도 안정적으로 앞/뒤 구분
    private bool IsFrontContact(Collider2D other)
    {
        if (!body || !other) return true; // 안전하게 정면으로 간주(즉사 오탐 방지)
        Vector2 fwd = new Vector2(dir, 0f);

        var d = Physics2D.Distance(body, other);

        Vector2 toOther;
        if (d.isOverlapped)
        {
            // 겹침일 때도 normal은 body→other 방향을 가리킴
            toOther = d.normal; // 단위벡터
        }
        else
        {
            // 에지-에지 최소 벡터를 사용
            toOther = (d.pointB - d.pointA);
            if (toOther.sqrMagnitude > 1e-8f) toOther.Normalize(); else toOther = d.normal;
        }

        float dotN = Vector2.Dot(fwd, toOther);
        if (dotN >= frontDotThreshold) return true;

        // 보조: 우리 콜라이더 에지 기준 장축 투영으로도 한 번 더 확인
        Vector2 cp = body.ClosestPoint(other.bounds.center);
        float longProj = Vector2.Dot(cp - (Vector2)transform.position, fwd);
        if (longProj >= frontEdgeBuffer) return true;

        return false;
    }

    private void HandleMonkillHit(Collider2D monkillCol)
    {
        Transform p1 = FindPlayer1Root(monkillCol.transform);

        bool frontContact = IsFrontContact(monkillCol);
        int moveSign = GetMoveSign();

        if (p1)
        {
            int relSign = Mathf.Sign(p1.position.x - transform.position.x) >= 0 ? +1 : -1;
            bool playerAhead = frontContact || ((moveSign != 0) ? (relSign == moveSign) : (relSign == dir));

            if (playerAhead)
            {
                CancelAttackIfAny();
                if (logicCo != null) { StopCoroutine(logicCo); logicCo = null; }
                StartCoroutine(BlockRoutine(p1, monkillCol));
                return;
            }
            StartDeathSequence("MonkillBehind");
            return;
        }

        if (frontContact)
        {
            CancelAttackIfAny();
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

        Transform t;
        if (!TryPickTarget(out t)) t = playerRoot;
        currentTarget = t;

        if (currentTarget && StillValidTarget(currentTarget) && WithinAttackWindow(currentTarget))
        {
            EnterAttackWindup();
        }
        else
        {
            EnterChase();
        }
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

        if (ClosestTargetCollider(t, out var bestCol, out float edge, out float vy))
        {
            if (vy > attackVerticalTolerance) return false;
            return edge <= attackRange;
        }

        float vy2 = Mathf.Abs(t.position.y - transform.position.y);
        if (vy2 > attackVerticalTolerance) return false;
        float hx = Mathf.Abs(t.position.x - transform.position.x);
        return hx <= attackHorizontalRange;
    }

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
