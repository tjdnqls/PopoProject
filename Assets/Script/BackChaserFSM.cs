using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
[DisallowMultipleComponent]
public class BackChaserFSM : MonoBehaviour, global::IDamageable
{
    public enum State { Patrol, Alert, Chase, AttackWindup, Attacking, AttackRecover, Return, Guard, Dead }

    // ===== Anim =====
    [Header("Anim")]
    public SpriteAnimationManager anim;     // 선택
    public SpriteRenderer sr;
    [SerializeField] private string idleAnim = "Idle";
    [SerializeField] private string runAnim = "Run";
    [SerializeField] private string attackStartAnim = "AttackStart";
    [SerializeField] private string attackAnim = "Attack";
    [SerializeField] private string blockAnim = "Block";
    [SerializeField] private string deathAnim = "Death";

    // ===== Damage Gate: 플레이어가 이 콜라이더들과 접촉 중일 때만 피해 허용 =====
    [Header("Damage Gate (Player must touch these child colliders)")]
    [SerializeField] private Collider2D[] damageAcceptZones;         // 플레이어 접촉 중이면 대미지 허용
    [SerializeField] private bool includeTriggersForGate = true;     // 플레이어 콜라이더가 Trigger여도 인정
    private static readonly Collider2D[] _gateBuf = new Collider2D[16];
    private ContactFilter2D _gateFilter;

    // ===== Child rotation sync (Z 회전만 동기화) =====
    [Header("Child Rotation Sync")]
    [SerializeField] private bool syncChildRotation = true;               // 부모 회전 싱크 on/off
    [SerializeField] private bool syncOnlyKinematicChildren = true;       // Kinematic만 동기화(권장)
    private readonly List<Rigidbody2D> _childBodies = new List<Rigidbody2D>();
    private float _lastParentRotZ;

    // ===== Refs =====
    [Header("Refs")]
    [SerializeField] private Rigidbody2D rb;
    [SerializeField] private Collider2D body;

    // ===== Patrol Waypoints =====
    [Header("Waypoints (A<->B)")]
    [SerializeField] private Transform waypointA;
    [SerializeField] private Transform waypointB;
    [SerializeField] private Vector2 fallbackLocalA = new Vector2(-3, 0);
    [SerializeField] private Vector2 fallbackLocalB = new Vector2(3, 0);
    [SerializeField] private float arriveEps = 0.08f;

    // ===== Move =====
    [Header("Move")]
    [SerializeField] private float patrolSpeed = 2.2f;
    [SerializeField] private float chaseSpeed = 3.8f;
    [SerializeField] private float accel = 25f;
    [SerializeField] private float stationarySpeedEps = 0.05f;

    // ===== Detect =====
    [Header("Detect")]
    [SerializeField] private LayerMask playerMask;
    [SerializeField] private float detectRadius = 5f;
    [SerializeField] private float detectVerticalTolerance = 1.5f;
    [SerializeField] private string preferPlayerTag = "Player2";
    [SerializeField] private float alertWaitSec = 1.0f;

    // ===== Give Up / Stuck =====
    [Header("Give Up / Stuck")]
    [SerializeField] private float lostGiveUpSec = 2.0f;
    [SerializeField] private float stuckGiveUpSec = 2.0f;

    // ===== Obstacle Probe =====
    [Header("Obstacle Probe")]
    [SerializeField] private LayerMask obstacleMask;
    [SerializeField] private float wallCheckDist = 0.30f;
    [SerializeField] private float lowWallCheckDist = 0.25f;
    [SerializeField] private float feetYOffset = 0.05f;

    // ===== Attack (outgoing) =====
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

    // ===== Hazard (Monkill 등) =====
    [Header("Hazard")]
    [SerializeField] private string hazardLayerName = "Monkill";
    private int _hazardLayer;

    // ===== AI Options =====
    [Header("AI Options")]
    [SerializeField] private bool attackAnyPlayerInRange = true;
    [SerializeField] private bool switchToPreferWhileChasing = false;
    [SerializeField] private string player1Tag = "Player1"; // 표적 승격용 태그

    // ===== Debug =====
    [Header("Debug View")]
    [SerializeField] private bool showAttackWindowGizmo = true;
    [SerializeField] private bool alwaysShowGizmo = true;
    [SerializeField] private Color attackGizmoColor = new Color(1f, 0f, 0f, 0.18f);
    [SerializeField] private Color attackGizmoEdgeColor = new Color(1f, 0f, 0.9f, 1f);
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
    private Coroutine guardCo;             // ★ 가드 중첩 방지
    private bool _lookLocked;
    private static readonly Collider2D[] _buf = new Collider2D[12];
    private bool isDying = false;

    // ===== Unity =====
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
        _hazardLayer = LayerMask.NameToLayer(hazardLayerName);

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

        _gateFilter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = playerMask,                // Player만
            useTriggers = includeTriggersForGate
        };

        CacheChildBodies();
        _lastParentRotZ = rb ? rb.rotation : transform.eulerAngles.z;

        // 시작 시 시선 방향을 Y 로테이션으로 반영
        ApplyFacingY(dir);
    }

    private void OnEnable()
    {
        Play(runAnim, true);
        SetFlipByDir(dir); // 내부에서 Y 회전으로 처리
    }

    private void FixedUpdate()
    {
        if (state == State.Dead) { StopHorizontal(); return; }

        // ★ 가드 종료 후 얼어남 방지: Guard가 아닐 때 X 프리즈가 남아 있으면 해제
        EnsureUnfrozenX();

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

        // 부모 Z회전 → 자식 Rigidbody2D 회전 싱크
        SyncChildRotations();
    }

    private void OnDisable()
    {
        if (sr) sr.color = baseColor;
        if (logicCo != null) { StopCoroutine(logicCo); logicCo = null; }
        if (guardCo != null) { StopCoroutine(guardCo); guardCo = null; }
        // 안전: 비활성화 시에도 X 프리즈 잔존 제거
        if (rb) rb.constraints &= ~RigidbodyConstraints2D.FreezePositionX;
    }

    // ===== Waypoints =====
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

    // ===== Ticks =====
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

    // ===== State enter =====
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

    // ===== 공격 즉시 취소(가드 인터럽트용) =====
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

    // ===== Detect / target pick =====
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

    // ===== Damage out =====
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

    // ===== Damage in (게이트 접촉이면 피해 허용, 아니면 가드) =====
    public void TakeDamage(int amount, Vector2 hitPoint, Vector2 hitNormal)
    {
        if (state == State.Dead || isDying) return;

        if (IsPlayerTouchingGate())
        {
            // 대미지 허용(현재 설계: 즉사)
            KillImmediate();
        }
        else
        {
            // 방어 판정: 데미지 무효 + 가드 연출
            StartGuard(null);
        }
    }

    public void OnHit(int damage) { }

    // ===== Hazard 충돌: 게이트 우선 검사 → (있으면 즉사 / 없으면 가드) =====
    private void OnCollisionEnter2D(Collision2D c)
    {
        if (state == State.Dead || isDying) return;
        if (c.collider.gameObject.layer == _hazardLayer) HandleHazardHit(c.collider);
    }
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (state == State.Dead || isDying) return;
        if (other.gameObject.layer == _hazardLayer) HandleHazardHit(other);
    }

    private void HandleHazardHit(Collider2D col)
    {
        if (IsPlayerTouchingGate())
        {
            StartDeathSequence("HazardGateKill"); // 공격 받음(즉사)
            return;
        }
        StartGuard(col);
    }

    // ★ 가드 시작(중첩 방지 + 공격 인터럽트 + 항상 제약 복원)
    private void StartGuard(Collider2D optionalHazard)
    {
        CancelAttackIfAny();                        // 공격/윈드업 중단 & 시선락 해제
        if (guardCo != null) { StopCoroutine(guardCo); guardCo = null; } // 이전 가드 중단
        guardCo = StartCoroutine(BlockRoutine(null, optionalHazard));
    }

    // 자식 게이트 콜라이더 중 하나라도 Player와 "오버랩" 중인지?
    private bool IsPlayerTouchingGate()
    {
        if (damageAcceptZones == null || damageAcceptZones.Length == 0) return false; // 엄격 모드
        for (int i = 0; i < damageAcceptZones.Length; i++)
        {
            var z = damageAcceptZones[i];
            if (!z || !z.enabled) continue;
            int n = z.Overlap(_gateFilter, _gateBuf); // OverlapCollider 사용
            if (n > 0) return true;
        }
        return false;
    }

    [Header("Block/Death Settings")]
    [SerializeField] private float guardHoldSec = 0.5f;
    [SerializeField] private float blockNoCollisionSec = 0.35f;
    [SerializeField] private float guardShakeAmp = 0.4f;
    [SerializeField] private float guardShakeDur = 0.15f;
    [SerializeField] private float despawnDelay = 5f;

    private IEnumerator BlockRoutine(Transform playerRoot, Collider2D optionalHazard = null)
    {
        state = State.Guard;
        CameraShaker.Shake(guardShakeAmp, guardShakeDur);
        Play(blockAnim, true);

        // --- 제약 캐시 & 가드 제약 설정 ---
        var prevConstraints = rb ? rb.constraints : RigidbodyConstraints2D.FreezeRotation;
        if (rb)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            rb.constraints = RigidbodyConstraints2D.FreezeRotation | RigidbodyConstraints2D.FreezePositionX;
        }

        float collisionOffSec = Mathf.Max(blockNoCollisionSec, guardHoldSec);
        List<Collider2D> cached = null;

        // --- 임시 충돌 무시 ---
        if (body)
        {
            if (playerRoot)
            {
                cached = new List<Collider2D>(playerRoot.GetComponentsInChildren<Collider2D>(true));
                foreach (var pc in cached) if (pc) Physics2D.IgnoreCollision(body, pc, true);
            }
            if (optionalHazard) Physics2D.IgnoreCollision(body, optionalHazard, true);
        }

        // --- 대기 ---
        float t = 0f;
        while (t < guardHoldSec) { t += Time.fixedDeltaTime; yield return new WaitForFixedUpdate(); }
        if (collisionOffSec > guardHoldSec)
        {
            float r = 0f;
            while (r < (collisionOffSec - guardHoldSec)) { r += Time.fixedDeltaTime; yield return new WaitForFixedUpdate(); }
        }

        // --- finally: 반드시 원복 ---
        if (body)
        {
            if (cached != null) foreach (var pc in cached) if (pc) Physics2D.IgnoreCollision(body, pc, false);
            if (optionalHazard) Physics2D.IgnoreCollision(body, optionalHazard, false);
        }

        if (rb)
        {
            // 회전 프리즈 유지, X 프리즈 해제 보장
            var restored = prevConstraints | RigidbodyConstraints2D.FreezeRotation;
            restored &= ~RigidbodyConstraints2D.FreezePositionX;
            rb.constraints = restored;
        }

        // 상태/타이머 리셋
        stuckTimer = 0f;
        lastSeenTime = Time.time;

        // 다음 상태 전이
        Transform tTarget;
        if (!TryPickTarget(out tTarget)) tTarget = playerRoot;
        currentTarget = tTarget;

        if (currentTarget && StillValidTarget(currentTarget) && WithinAttackWindow(currentTarget))
            EnterAttackWindup();
        else
            EnterChase();

        guardCo = null;
    }

    // ===== Death sequence =====
    private void StartDeathSequence(string reason)
    {
        if (isDying) return;
        isDying = true;

        if (logicCo != null) { StopCoroutine(logicCo); logicCo = null; }
        if (guardCo != null) { StopCoroutine(guardCo); guardCo = null; }

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

    // ===== Movement / probes =====
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

    // ===== Facing by Y-rotation (스프라이트 플립/스케일 변경 금지) =====
    private void SetFlipByDir(int d)
    {
        if (_lookLocked) return;
        ApplyFacingY(d);
    }
    private void ForceFlipByDir(int d)
    {
        ApplyFacingY(d);
    }
    private void ApplyFacingY(int d)
    {
        // 오른쪽 바라봄: Y=0, 왼쪽: Y=180
        Vector3 e = transform.localEulerAngles;
        e.y = (d < 0) ? 180f : 0f;
        transform.localEulerAngles = e;
        // sr.flipX, localScale.x 변경하지 않음
    }

    // ===== Attack window =====
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

    // ===== Child bodies cache & rotation sync (Z만) =====
    private void CacheChildBodies()
    {
        _childBodies.Clear();
        var all = GetComponentsInChildren<Rigidbody2D>(true);
        foreach (var r in all)
        {
            if (!r || r == rb) continue; // 자기 자신 제외
            if (syncOnlyKinematicChildren && r.bodyType != RigidbodyType2D.Kinematic) continue;
            _childBodies.Add(r);
        }
    }

    private void SyncChildRotations()
    {
        if (!syncChildRotation) return;

        float parentRot = rb ? rb.rotation : transform.eulerAngles.z;
        if (Mathf.Approximately(parentRot, _lastParentRotZ)) return;

        float delta = Mathf.DeltaAngle(_lastParentRotZ, parentRot);
        for (int i = 0; i < _childBodies.Count; i++)
        {
            var cb = _childBodies[i];
            if (!cb) continue;
            cb.MoveRotation(cb.rotation + delta);
        }
        _lastParentRotZ = parentRot;
    }

    // ===== 안전장치: Guard가 아닐 때 X 프리즈 잔존 제거 =====
    private void EnsureUnfrozenX()
    {
        if (!rb) return;
        if (state == State.Guard) return;
        if ((rb.constraints & RigidbodyConstraints2D.FreezePositionX) != 0)
        {
            rb.constraints &= ~RigidbodyConstraints2D.FreezePositionX;
            rb.constraints |= RigidbodyConstraints2D.FreezeRotation; // 회전 프리즈는 유지
        }
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
