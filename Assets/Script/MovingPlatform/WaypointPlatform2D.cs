using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Globalization; // ← for InvariantCulture
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

[RequireComponent(typeof(Rigidbody2D))]
[DisallowMultipleComponent]
public class WaypointPlatform2D : MonoBehaviour
{
    public enum PathMode { PingPong, Loop, OneShot }
    public enum MoveMode { ConstantSpeed, EaseInOut }
    public enum AnchorMode { ColliderBoundsCenter, TilemapBoundsCenter, TransformPivot, Custom }

    // ───────── Node 확장: 웨이포인트 도착 시 쉐이크 옵션 ─────────
    [Serializable]
    public class Node
    {
        public Transform point;
        public float waitSeconds = 0f;

        [Header("Camera Shake on Arrive")]
        public bool shakeOnArrive = false;
        [Min(0f)] public float shakeIntensity = 0.8f;
        [Min(0f)] public float shakeDuration = 0.12f;
    }

    // ───────── Path ─────────
    [Header("Path")]
    [SerializeField] private Node[] nodes;
    [SerializeField] private PathMode pathMode = PathMode.PingPong;
    [SerializeField] private MoveMode moveMode = MoveMode.EaseInOut;
    [SerializeField] private float speed = 2.5f;
    [SerializeField] private float easeDistance = 0.3f;

    // ───────── Anchor ─────────
    [Header("Anchor (경로 정합 기준)")]
    [SerializeField] private AnchorMode anchorMode = AnchorMode.ColliderBoundsCenter; // 기본: 콜라이더 중심
    [SerializeField] private Transform customAnchor;

    // ───────── Start ─────────
    [Header("Start")]
    [SerializeField] private int startIndex = 0;
    [SerializeField] private bool startReverse = false;
    [SerializeField] private bool snapToStartNode = true;
    [SerializeField] private bool waitAtStartNode = false;

    // ───────── Trigger / Hold / StopHold ─────────
    [Header("Trigger / Hold (작동 가드)")]
    [Tooltip("ON이면 처음엔 정지. 트리거에 Player/Box가 닿으면 이후 동작")]
    [SerializeField] private bool triggerMode = false;

    [Tooltip("Trigger ON일 때만 의미. 닿아있는 동안 전진, 끊기면 startIndex로 서서히 복귀")]
    [SerializeField] private bool holdMode = false;

    [Tooltip("Trigger ON일 때만 의미. 닿아있는 동안만 전진, 떨어지면 즉시 그 자리에서 정지")]
    [SerializeField] private bool stopHold = false;

    [Tooltip("StopHold ON일 때만 의미. 닿아있으면 정지, 떨어져있을 때만 전진")]
    [SerializeField] private bool stopHoldReverse = false;

    // === JustGo: 지정 횟수만 왕복하고 멈춤(Trigger 필요, 다른 Hold/StopHold와 중복 불가)
    [Header("JustGo (Trigger 필요, 왕복 N회 후 정지)")]
    [Tooltip("트리거에 닿으면 지정한 왕복 횟수만 이동 후 정지. 동작 중엔 추가 트리거 무시.")]
    [SerializeField] private bool justGoMode = false;

    [Tooltip("왕복 횟수(끝단→반대 끝단→원래 끝단 = 1회)")]
    [SerializeField, Min(1)] private int justGoRoundTrips = 1;

    // ───────── Trigger Sources ─────────
    [Header("Trigger Sources")]
    [Tooltip("복수의 트리거 콜라이더 등록(비면 플랫폼 자신의 콜라이더/타일맵 경계를 사용)")]
    [SerializeField] private List<Collider2D> triggerColliders = new();

    [Tooltip("모든 트리거가 닿아 있어야 작동(All)")]
    [SerializeField] private bool allTrigger = false;

    [Tooltip("AllTrigger ON일 때만 의미. 선택한 트리거들만 모두 닿으면 작동")]
    [SerializeField] private bool selectTrigger = false;

    [Tooltip("SelectTrigger에서 요구하는 트리거들")]
    [SerializeField] private List<Collider2D> selectTriggerColliders = new();

    [Tooltip("AllTrigger ON일 때만 의미. N개 이상의 트리거가 닿으면 작동")]
    [SerializeField] private bool countTrigger = false;

    [SerializeField] private int countThreshold = 1;

    [Header("Trigger Layers / Perf")]
    [SerializeField] private string playerLayerName = "Player";
    [SerializeField] private string boxLayerName = "Box";
    [SerializeField] private int triggerMaxHits = 32;
    [SerializeField, Range(0.05f, 1f)] private float returnSpeedMul = 0.5f; // Hold 복귀 속도 배율

    // ───────── Camera Shake ─────────
    [Header("Camera Shake")]
    [Tooltip("발동 시작(움직임 허용이 되는 순간)에 1회 흔듭니다.")]
    [SerializeField] private bool shakeOnActivate = true;
    [SerializeField, Min(0f)] private float activateShakeIntensity = 0.8f;
    [SerializeField, Min(0f)] private float activateShakeDuration = 0.12f;

    // (선택) 트리거 없이 시작하자마자 흔들고 싶을 때
    [SerializeField] private bool shakeOnAwake = false;
    [SerializeField, Min(0f)] private float awakeShakeIntensity = 0.8f;
    [SerializeField, Min(0f)] private float awakeShakeDuration = 0.12f;

    // ───────── Renderer Safety ─────────
    [Header("Renderer Safety (Tile 사라짐 방지)")]
    [SerializeField] private bool expandCullingToPath = true;
    [SerializeField] private bool includeTriggerAreaInCulling = true;
    [SerializeField] private Vector2 cullingPadding = new Vector2(2f, 2f);
    [SerializeField] private bool fallbackToIndividualIfExpandFails = true;

    // ───────── Debug ─────────
    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private Color pathColor = new Color(1f, 0.9f, 0.2f, 0.9f);
    [SerializeField] private Color pointColor = new Color(0.3f, 1f, 0.9f, 0.9f);
    [SerializeField] private Color triggerGizmoColor = new Color(0.2f, 0.8f, 1f, 0.18f);

    // ───────── Internals ─────────
    private Rigidbody2D rb;
    private Tilemap tilemap;
    private TilemapRenderer tr;
    private TilemapCollider2D tileCol;
    private CompositeCollider2D compCol;

    private int dir = +1;
    private int curr;
    private float waitTimer = 0f;
    private Vector2 prevPos;
    private bool hasPrev;
    private Vector2 anchorOffset;

    private bool triggered = false;   // 일반 트리거 래치
    private bool contacting = false;  // 이번 프레임 조건 충족 여부
    private bool returning = false;   // Hold 복귀 중
    private int triggerMask;
    private Collider2D[] sharedBuf;

    public Vector2 PlatformDelta { get; private set; }
    public Vector2 PlatformVelocity { get; private set; }

    // === JustGo: 상태 변수
    private bool justGoActive = false;
    private int justGoTripsDone = 0;
    private int justGoHomeEdge = -1;
    private bool justGoVisitedOppositeEdge = false;
    private bool justGoBlockUntilContactClears = false;

    // ★ CameraShake 판단을 위한 에지 감지
    private bool prevTriggered = false;
    private bool prevContacting = false;

    // ───────── Lifecycle ─────────
    private void Reset()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        AutoCollectWaypointsIfEmpty();
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        if (rb.bodyType == RigidbodyType2D.Dynamic) rb.bodyType = RigidbodyType2D.Kinematic;

        tilemap = GetComponent<Tilemap>();
        tr = GetComponent<TilemapRenderer>();
        tileCol = GetComponent<TilemapCollider2D>();
        compCol = GetComponent<CompositeCollider2D>();

        if (nodes == null || nodes.Length < 2) AutoCollectWaypointsIfEmpty();

        startIndex = Mathf.Clamp(startIndex, 0, Math.Max(0, (nodes?.Length ?? 1) - 1));
        dir = startReverse ? -1 : +1;

        RecomputeAnchorOffset(); // ★ 콜라이더 중심 기준

        if (nodes != null && nodes.Length >= 2 && nodes[startIndex].point)
        {
            if (snapToStartNode)
            {
                Vector2 a = nodes[startIndex].point.position;
                Vector2 startPos = a - anchorOffset;
                rb.position = startPos;
                transform.position = startPos;
                prevPos = startPos;
                hasPrev = true;
            }

            if (waitAtStartNode) waitTimer = Mathf.Max(0f, nodes[startIndex].waitSeconds);
            curr = NextIndexFrom(startIndex, ref dir, pathMode, nodes.Length);
        }
        else curr = startIndex;

        triggerMask = LayerMask.GetMask(playerLayerName, boxLayerName);
        sharedBuf = new Collider2D[Mathf.Max(8, triggerMaxHits)];

        // 일반 트리거 래치 초기화
        triggered = !triggerMode;
        contacting = false;
        returning = false;

        // JustGo 초기화
        justGoActive = false;
        justGoTripsDone = 0;
        justGoHomeEdge = -1;
        justGoVisitedOppositeEdge = false;
        justGoBlockUntilContactClears = false;

        ApplyRendererSafety();

        // ★ 시작 즉시 흔들기(옵션)
        if (shakeOnAwake) DoCameraShake(awakeShakeIntensity, awakeShakeDuration);

        prevTriggered = triggered;
        prevContacting = contacting;
    }

    private void OnValidate()
    {
        if (speed < 0.01f) speed = 0.01f;
        if (easeDistance < 0f) easeDistance = 0f;
        if (nodes == null || nodes.Length < 2) return;

        startIndex = Mathf.Clamp(startIndex, 0, nodes.Length - 1);
        if (triggerMaxHits < 8) triggerMaxHits = 8;
        if (countThreshold < 1) countThreshold = 1;
        if (justGoRoundTrips < 1) justGoRoundTrips = 1;

        // 트리거 OFF면 관련 옵션 전부 OFF
        if (!triggerMode)
        {
            holdMode = false;
            stopHold = false;
            stopHoldReverse = false;
            // JustGo는 트리거 필요
        }
        else
        {
            if (!allTrigger)
            {
                selectTrigger = false;
                countTrigger = false;
            }
            if (selectTrigger && countTrigger) countTrigger = false;
            int n = triggerColliders != null ? triggerColliders.Count(c => c) : 0;
            if (n > 0 && countThreshold > n) countThreshold = n;
        }

        if (justGoMode)
        {
            holdMode = false;
            stopHold = false;
            stopHoldReverse = false;
            if (pathMode != PathMode.PingPong) pathMode = PathMode.PingPong;
        }
    }

    private void FixedUpdate()
    {
        Vector2 now = rb.position;
        if (!hasPrev) { prevPos = now; hasPrev = true; }

        // ── Trigger 평가 ──
        bool conditionMet = false; // 이번 프레임 '작동 조건'
        if (triggerMode)
        {
            EvaluateTriggers(out bool anyHit, out bool allHit, out int hitCount, out bool selectedAllHit);

            if (allTrigger)
            {
                if (selectTrigger && selectTriggerColliders.Count > 0) conditionMet = selectedAllHit;
                else if (countTrigger) conditionMet = hitCount >= Mathf.Max(1, countThreshold);
                else conditionMet = allHit;
            }
            else conditionMet = anyHit;

            if (justGoMode)
            {
                if (!justGoActive)
                {
                    if (!justGoBlockUntilContactClears && conditionMet)
                    {
                        StartJustGo(); // ★ 여기서 발동 시작 → 내부에서 쉐이크 호출
                    }
                    if (!conditionMet) justGoBlockUntilContactClears = false;
                }

                triggered = justGoActive;
                contacting = false;
            }
            else
            {
                if (!triggered && conditionMet) triggered = true; // 최초 발동 래치
                contacting = conditionMet;
            }
        }
        else
        {
            contacting = false;
            triggered = true; // 트리거 모드 OFF면 항상 진행 허용
        }

        // ★★★ 발동 시작 시점 감지(1프레임 에지) → 카메라 쉐이크
        if (shakeOnActivate)
        {
            bool contactStart = contacting && !prevContacting;

            // StopHold / Reverse의 '전진 허용' 에지 보정
            bool stopHoldEdge = stopHold && (prevContacting && !contacting);          // 트리거에서 벗어날 때 전진
            bool stopHoldRevEdge = stopHoldReverse && contactStart;                   // 트리거에 들어올 때 전진

            bool activationEdge =
                (!prevTriggered && triggered) ||                                     // 트리거 래치 true로 바뀐 순간
                (holdMode && contactStart) ||                                        // Hold: 닿기 시작할 때
                stopHoldEdge || stopHoldRevEdge;                                     // StopHold류 전진 에지

            if (activationEdge) DoCameraShake(activateShakeIntensity, activateShakeDuration);
        }

        // 진행 불가 상태면 정지
        if (!triggered)
        {
            UpdateVel(now);
            prevTriggered = triggered;
            prevContacting = contacting;
            return;
        }

        // ── JustGo가 아니면 Hold/StopHold 처리 ──
        if (!justGoMode)
        {
            // 우선순위: StopHold(Reverse) > Hold > 일반
            if (triggerMode && (stopHold || stopHoldReverse))
            {
                bool allowAdvance = stopHold ? contacting == false : contacting == true;
                if (!allowAdvance)
                {
                    returning = false;
                    UpdateVel(now);
                    prevTriggered = triggered;
                    prevContacting = contacting;
                    return;
                }
                returning = false; // 전진 허용 → 아래 일반 전진
            }
            else if (triggerMode && holdMode)
            {
                if (!contacting)
                {
                    returning = true;
                    MoveTowardsAnchorNode(startIndex, Mathf.Max(0.01f, speed * returnSpeedMul));
                    Vector2 want = (Vector2)nodes[startIndex].point.position - anchorOffset;
                    if (((Vector2)rb.position - want).sqrMagnitude < 1e-6f) { waitTimer = 0f; }
                    prevTriggered = triggered;
                    prevContacting = contacting;
                    return;
                }
                returning = false;
            }
        }

        // ── 일반 전진 ──
        if (nodes == null || nodes.Length < 2 || nodes[Mathf.Clamp(curr, 0, nodes.Length - 1)].point == null)
        {
            UpdateVel(now);
            prevTriggered = triggered;
            prevContacting = contacting;
            return;
        }
        if (waitTimer > 0f)
        {
            waitTimer -= Time.fixedDeltaTime;
            UpdateVel(now);
            prevTriggered = triggered;
            prevContacting = contacting;
            return;
        }

        Vector2 targetAnchor = nodes[curr].point.position;
        Vector2 targetRb = targetAnchor - anchorOffset;

        float step = speed * Time.fixedDeltaTime;
        if (moveMode == MoveMode.EaseInOut && easeDistance > 0f)
        {
            float dist = Vector2.Distance(now, targetRb);
            float t = Mathf.Clamp01(dist / Mathf.Max(0.0001f, easeDistance));
            step *= Mathf.SmoothStep(0.15f, 1f, t);
        }

        Vector2 next = Vector2.MoveTowards(now, targetRb, step);
        rb.MovePosition(next);

        if ((next - targetRb).sqrMagnitude < 1e-6f)
        {
            // === 도착 처리
            int arrivedIndex = curr;

            // ★ 웨이포인트 도착 시 카메라 쉐이크 (노드별 설정)
            var n = nodes[arrivedIndex];
            if (n != null && n.shakeOnArrive)
                DoCameraShake(Mathf.Max(0f, n.shakeIntensity), Mathf.Max(0f, n.shakeDuration));

            waitTimer = Mathf.Max(0f, nodes[curr].waitSeconds);
            curr = NextIndexFrom(curr, ref dir, pathMode, nodes.Length);

            // === JustGo: 끝단 도달 체크 & 왕복 카운트
            if (justGoMode && justGoActive && pathMode == PathMode.PingPong && nodes.Length >= 2)
            {
                int last = nodes.Length - 1;
                if (arrivedIndex == 0 || arrivedIndex == last)
                    HandleJustGoEdge(arrivedIndex, last);
            }
        }

        UpdateVel(rb.position);

        // 상태 업데이트(다음 프레임 에지 감지용)
        prevTriggered = triggered;
        prevContacting = contacting;
    }

    // ───────── Trigger 평가(멀티) ─────────
    private void EvaluateTriggers(out bool anyHit, out bool allHit, out int hitCount, out bool selectedAllHit)
    {
        anyHit = false;
        hitCount = 0;
        selectedAllHit = true;

        var list = (triggerColliders != null && triggerColliders.Count > 0) ? triggerColliders.Where(c => c).ToList() : null;

        Dictionary<Collider2D, bool> cache = new(16);

        if (list == null || list.Count == 0)
        {
            Bounds b = GetSelfBounds();
            bool hit = Physics2D.OverlapBox(
                (Vector2)b.center,
                new Vector2(b.size.x, b.size.y),
                0f,
                triggerMask
            ) != null;

            anyHit = hit;
            allHit = hit;
            selectedAllHit = hit;
            hitCount = hit ? 1 : 0;
            return;
        }

        allHit = true;
        foreach (var c in list)
        {
            bool hit = OverlapColliderBox(c);
            cache[c] = hit;
            anyHit |= hit;
            allHit &= hit;
            if (hit) hitCount++;
        }

        if (selectTrigger && selectTriggerColliders != null && selectTriggerColliders.Count > 0)
        {
            foreach (var sc in selectTriggerColliders)
            {
                if (!sc) { selectedAllHit = false; break; }
                bool v = cache.TryGetValue(sc, out var h) ? h : OverlapColliderBox(sc);
                if (!v) { selectedAllHit = false; break; }
            }
        }
        else selectedAllHit = false;

        bool OverlapColliderBox(Collider2D col)
        {
            Bounds b = col.bounds;
            return Physics2D.OverlapBox(
                (Vector2)b.center,
                new Vector2(b.size.x, b.size.y),
                0f,
                triggerMask
            ) != null;
        }
    }

    // ───────── 이동 보조/상태 ─────────
    private void MoveTowardsAnchorNode(int nodeIndex, float spd)
    {
        if (nodes == null || nodes.Length == 0 || nodes[nodeIndex].point == null) return;

        Vector2 now = rb.position;
        Vector2 targetRb = (Vector2)nodes[nodeIndex].point.position - anchorOffset;

        float step = spd * Time.fixedDeltaTime;
        if (moveMode == MoveMode.EaseInOut && easeDistance > 0f)
        {
            float dist = Vector2.Distance(now, targetRb);
            float t = Mathf.Clamp01(dist / Mathf.Max(0.0001f, easeDistance));
            step *= Mathf.SmoothStep(0.15f, 1f, t);
        }

        Vector2 next = Vector2.MoveTowards(now, targetRb, step);
        rb.MovePosition(next);
        UpdateVel(next);
    }

    private void UpdateVel(Vector2 newPos)
    {
        PlatformDelta = newPos - prevPos;
        PlatformVelocity = (Time.fixedDeltaTime > 0f) ? PlatformDelta / Time.fixedDeltaTime : Vector2.zero;
        prevPos = newPos;
    }

    private static int NextIndexFrom(int index, ref int dir, PathMode mode, int count)
    {
        int next = index + dir;
        if (next >= 0 && next < count) return next;

        switch (mode)
        {
            case PathMode.PingPong:
                dir *= -1;
                return Mathf.Clamp(index + dir, 0, count - 1);
            case PathMode.Loop:
                return (next % count + count) % count;
            case PathMode.OneShot:
                return Mathf.Clamp(index, 0, count - 1);
        }
        return index;
    }

    private void RecomputeAnchorOffset()
    {
        Vector2 anchorWorld;
        switch (anchorMode)
        {
            case AnchorMode.ColliderBoundsCenter:
                if (compCol) anchorWorld = compCol.bounds.center;
                else if (tileCol) anchorWorld = tileCol.bounds.center;
                else if (tilemap)
                {
                    Vector3 lc = tilemap.localBounds.center;
                    anchorWorld = tilemap.transform.TransformPoint(lc);
                }
                else anchorWorld = rb.position;
                break;
            case AnchorMode.TilemapBoundsCenter:
                if (tilemap)
                {
                    Vector3 lc = tilemap.localBounds.center;
                    anchorWorld = tilemap.transform.TransformPoint(lc);
                }
                else anchorWorld = rb.position;
                break;
            case AnchorMode.Custom:
                anchorWorld = customAnchor ? (Vector2)customAnchor.position : rb.position;
                break;
            default:
                anchorWorld = rb.position;
                break;
        }
        anchorOffset = anchorWorld - rb.position;
    }

    private void AutoCollectWaypointsIfEmpty()
    {
        var list = new List<Node>();
        foreach (Transform c in transform)
            if (c.name.IndexOf("Waypoint", StringComparison.OrdinalIgnoreCase) >= 0)
                list.Add(new Node { point = c, waitSeconds = 0f });
        if (list.Count >= 2) nodes = list.ToArray();
    }

    // ───────── Bounds 유틸 ─────────
    private Bounds GetSelfBounds()
    {
        if (compCol) return compCol.bounds;
        if (tileCol) return tileCol.bounds;
        if (tilemap)
        {
            var lb = tilemap.localBounds;
            var wc = tilemap.transform.TransformPoint(lb.center);
            var we = tilemap.transform.TransformVector(lb.extents);
            return new Bounds(wc, we * 2f);
        }
        return new Bounds(transform.position, Vector3.one);
    }

    // 렌더러 세이프티(깜빡임 방지)
    private void ApplyRendererSafety()
    {
        tr = tr ?? GetComponent<TilemapRenderer>();
        tilemap = tilemap ?? GetComponent<Tilemap>();
        if (!tr) return;

        if (!expandCullingToPath)
        {
            if (fallbackToIndividualIfExpandFails) TrySetRendererModeIndividual();
            return;
        }

        Bounds worldAabb = new Bounds(transform.position, Vector3.zero);
        bool hasAny = false;

        // 경로 AABB
        if (nodes != null && nodes.Length > 0)
        {
            Vector3 min = new(float.PositiveInfinity, float.PositiveInfinity, 0);
            Vector3 max = new(float.NegativeInfinity, float.NegativeInfinity, 0);
            bool any = false;
            for (int i = 0; i < nodes.Length; i++)
            {
                var p = nodes[i]?.point;
                if (!p) continue;
                Vector3 w = p.position;
                if (w.x < min.x) min.x = w.x;
                if (w.y < min.y) min.y = w.y;
                if (w.x > max.x) max.x = w.x;
                if (w.y > max.y) max.y = w.y;
                any = true;
            }
            if (any) AddBounds(ref worldAabb, ref hasAny, new Bounds((min + max) * 0.5f, (max - min)));
        }

        // 타일맵 경계
        if (tilemap)
        {
            var lb = tilemap.localBounds;
            var wc = tilemap.transform.TransformPoint(lb.center);
            var we = tilemap.transform.TransformVector(lb.extents);
            AddBounds(ref worldAabb, ref hasAny, new Bounds(wc, we * 2f));
        }

        // 트리거 영역(멀티 → 합집합)
        if (includeTriggerAreaInCulling)
        {
            Bounds tb = GetTriggerUnionBounds();
            AddBounds(ref worldAabb, ref hasAny, tb);
        }

        if (!hasAny) worldAabb = new Bounds(transform.position, Vector3.one);
        worldAabb.Expand(new Vector3(cullingPadding.x, cullingPadding.y, 0f));

        // 월드→로컬
        var t = tr.transform;
        Vector3 lCenter = t.InverseTransformPoint(worldAabb.center);
        Vector3 lx = t.InverseTransformVector(new Vector3(worldAabb.size.x, 0, 0));
        Vector3 ly = t.InverseTransformVector(new Vector3(0, worldAabb.size.y, 0));
        Vector3 lSize = new(Mathf.Abs(lx.x) + Mathf.Abs(ly.x), Mathf.Abs(lx.y) + Mathf.Abs(ly.y), 0f);

        bool ok = false;
        try
        {
            var type = typeof(TilemapRenderer);
            var propDetect = type.GetProperty("detectChunkCullingBounds", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var propBounds = type.GetProperty("chunkCullingBounds", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (propDetect != null) propDetect.SetValue(tr, false);
            if (propBounds != null)
            {
                var boundsType = propBounds.PropertyType;
                var ctor = boundsType.GetConstructor(new Type[] { typeof(Vector3), typeof(Vector3) });
                object localBounds = ctor.Invoke(new object[] { lCenter, lSize });
                propBounds.SetValue(tr, localBounds);
                ok = true;
            }
        }
        catch { ok = false; }

        if (!ok && fallbackToIndividualIfExpandFails) TrySetRendererModeIndividual();
    }

    private Bounds GetTriggerUnionBounds()
    {
        bool any = false;
        Bounds acc = new Bounds(transform.position, Vector3.zero);
        if (triggerColliders != null)
        {
            foreach (var c in triggerColliders)
            {
                if (!c) continue;
                AddBounds(ref acc, ref any, c.bounds);
            }
        }
        if (!any)
        {
            var b = GetSelfBounds();
            acc = b;
            any = true;
        }
        return acc;
    }

    private static void AddBounds(ref Bounds acc, ref bool hasAny, Bounds add)
    {
        if (!hasAny)
        {
            acc = add;
            hasAny = true;
        }
        else acc.Encapsulate(add);
    }

    private void TrySetRendererModeIndividual()
    {
        try
        {
            if (tr && tr.mode != TilemapRenderer.Mode.Individual) tr.mode = TilemapRenderer.Mode.Individual;
        }
        catch { }
    }

    // === JustGo: 시작/종료/엣지 핸들링
    private void StartJustGo()
    {
        justGoActive = true;
        justGoTripsDone = 0;
        justGoVisitedOppositeEdge = false;
        justGoBlockUntilContactClears = false;

        // 현재 위치에서 더 가까운 끝단(0 또는 last)을 홈으로 고정
        justGoHomeEdge = GetNearestEdgeIndex();

        // ★ JustGo 시작도 '발동 시작'으로 간주 → 쉐이크
        if (shakeOnActivate) DoCameraShake(activateShakeIntensity, activateShakeDuration);
    }
    private int GetNearestEdgeIndex()
    {
        if (nodes == null || nodes.Length < 2) return 0;
        int last = nodes.Length - 1;

        Vector2 pos = rb.position;
        Vector2 a = (Vector2)nodes[0].point.position - anchorOffset;
        Vector2 z = (Vector2)nodes[last].point.position - anchorOffset;

        return ((pos - a).sqrMagnitude <= (pos - z).sqrMagnitude) ? 0 : last;
    }
    private void EndJustGo()
    {
        justGoActive = false;
        triggered = false;                  // 즉시 정지
        justGoBlockUntilContactClears = true; // 접촉이 한 번 떨어질 때까지 재시작 금지
    }

    private void HandleJustGoEdge(int arrivedEdge, int lastIndex)
    {
        if (justGoHomeEdge < 0) return;

        int opposite = (justGoHomeEdge == 0) ? lastIndex : 0;

        if (arrivedEdge == opposite)
        {
            // 반대 끝단 방문 체크
            justGoVisitedOppositeEdge = true;
            return;
        }

        // 홈 끝단으로 '복귀'했을 때만 1회 카운트
        if (arrivedEdge == justGoHomeEdge && justGoVisitedOppositeEdge)
        {
            justGoTripsDone++;
            justGoVisitedOppositeEdge = false;

            if (justGoTripsDone >= justGoRoundTrips)
                EndJustGo(); // 즉시 정지(다음 프레임부터 이동 안 함)
        }
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos || nodes == null || nodes.Length < 2) return;

        Gizmos.color = pathColor;
        for (int i = 0; i < nodes.Length - 1; i++)
            if (nodes[i].point && nodes[i + 1].point)
                Gizmos.DrawLine(nodes[i].point.position, nodes[i + 1].point.position);
        if (pathMode == PathMode.Loop && nodes[0].point && nodes[^1].point)
            Gizmos.DrawLine(nodes[^1].point.position, nodes[0].point.position);

        Gizmos.color = pointColor;
        foreach (var n in nodes) if (n.point) Gizmos.DrawSphere(n.point.position, 0.07f);

        var list = (triggerColliders != null && triggerColliders.Count > 0)
        ? triggerColliders.Where(c => c).ToList()
        : null;

        if (list != null && list.Count > 0)
        {
            foreach (var c in list)
            {
                var b = c.bounds;
                Gizmos.color = triggerGizmoColor;
                Gizmos.DrawCube(b.center, b.size);
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireCube(b.center, b.size);
            }
        }
        else
        {
            Bounds fb = GetSelfBounds();
            Gizmos.color = triggerGizmoColor;
            Gizmos.DrawCube(fb.center, fb.size);
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(fb.center, fb.size);
        }
    }

    // ───────── CameraShaker 호출 유틸 (안전 리플렉션) ─────────
    private static void DoCameraShake(float intensity, float duration)
    {
        try
        {
            // 1) 클래스 찾기 (모든 어셈블리에서)
            Type shakerType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                shakerType = asm.GetType("CameraShaker");
                if (shakerType != null) break;
            }
            if (shakerType == null) return;

            // 2) 오버로드 우선: Shake(string,string) → 없으면 Shake(float,float)
            MethodInfo m =
                shakerType.GetMethod("Shake", BindingFlags.Public | BindingFlags.Static, null,
                                     new Type[] { typeof(string), typeof(string) }, null)
                ?? shakerType.GetMethod("Shake", BindingFlags.Public | BindingFlags.Static, null,
                                        new Type[] { typeof(float), typeof(float) }, null);
            if (m == null) return;

            if (m.GetParameters()[0].ParameterType == typeof(string))
            {
                var s1 = intensity.ToString(CultureInfo.InvariantCulture);
                var s2 = duration.ToString(CultureInfo.InvariantCulture);
                m.Invoke(null, new object[] { s1, s2 });
            }
            else
            {
                m.Invoke(null, new object[] { intensity, duration });
            }
        }
        catch { /* fail-safe: 아무 것도 안 함 */ }
    }
}

#if UNITY_EDITOR
    [CustomEditor(typeof(WaypointPlatform2D))]
    public class WaypointPlatform2DEditor : Editor
    {
        SerializedProperty triggerMode, holdMode, stopHold, stopHoldReverse;
        SerializedProperty triggerColliders, allTrigger, selectTrigger, selectTriggerColliders, countTrigger, countThreshold;

        // === JustGo
        SerializedProperty justGoMode, justGoRoundTrips;

        // 참조용(경고 표기)
        SerializedProperty pathModeProp;

        static readonly string[] _exclude = {
            "m_Script",
            // Trigger/Hold/JustGo 그룹은 커스텀으로 그림
            "triggerMode","holdMode","stopHold","stopHoldReverse",
            "justGoMode","justGoRoundTrips",
            "triggerColliders","allTrigger","selectTrigger","selectTriggerColliders","countTrigger","countThreshold"
        };

        void OnEnable()
        {
            triggerMode = serializedObject.FindProperty("triggerMode");
            holdMode = serializedObject.FindProperty("holdMode");
            stopHold = serializedObject.FindProperty("stopHold");
            stopHoldReverse = serializedObject.FindProperty("stopHoldReverse");

            justGoMode = serializedObject.FindProperty("justGoMode");
            justGoRoundTrips = serializedObject.FindProperty("justGoRoundTrips");

            triggerColliders = serializedObject.FindProperty("triggerColliders");
            allTrigger = serializedObject.FindProperty("allTrigger");
            selectTrigger = serializedObject.FindProperty("selectTrigger");
            selectTriggerColliders = serializedObject.FindProperty("selectTriggerColliders");
            countTrigger = serializedObject.FindProperty("countTrigger");
            countThreshold = serializedObject.FindProperty("countThreshold");

            pathModeProp = serializedObject.FindProperty("pathMode");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawPropertiesExcluding(serializedObject, _exclude);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Trigger / Hold / JustGo (작동 가드)", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(triggerMode);

            using (new EditorGUI.DisabledScope(!triggerMode.boolValue))
            {
                EditorGUILayout.PropertyField(triggerColliders, new GUIContent("Trigger Colliders"), true);

                // === JustGo ===
                EditorGUILayout.Space(6);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("JustGo", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(justGoMode, new GUIContent("Enable JustGo"));
                    using (new EditorGUI.DisabledScope(!justGoMode.boolValue))
                    {
                        EditorGUILayout.PropertyField(justGoRoundTrips, new GUIContent("Round Trips"));
                        if ((WaypointPlatform2D.PathMode)pathModeProp.enumValueIndex != WaypointPlatform2D.PathMode.PingPong)
                            EditorGUILayout.HelpBox("JustGo는 PingPong 경로에서 동작합니다. (자동으로 PingPong으로 설정됩니다)", MessageType.Info);
                    }
                }

                // === Hold / StopHold (JustGo와 상호배타)
                using (new EditorGUI.DisabledScope(justGoMode.boolValue))
                {
                    EditorGUI.BeginChangeCheck();
                    bool h = EditorGUILayout.ToggleLeft("Hold Mode", holdMode.boolValue);
                    if (EditorGUI.EndChangeCheck()) { holdMode.boolValue = h; if (h) { stopHold.boolValue = false; stopHoldReverse.boolValue = false; } }

                    EditorGUI.BeginChangeCheck();
                    bool s = EditorGUILayout.ToggleLeft("Stop Hold", stopHold.boolValue);
                    if (EditorGUI.EndChangeCheck()) { stopHold.boolValue = s; if (s) { holdMode.boolValue = false; stopHoldReverse.boolValue = false; } }

                    EditorGUI.BeginChangeCheck();
                    bool r = EditorGUILayout.ToggleLeft("Stop Hold Reverse", stopHoldReverse.boolValue);
                    if (EditorGUI.EndChangeCheck()) { stopHoldReverse.boolValue = r; if (r) { holdMode.boolValue = false; stopHold.boolValue = false; } }
                }

                EditorGUILayout.Space(4);
                EditorGUILayout.PropertyField(allTrigger, new GUIContent("All Trigger (모든 트리거 필요)"));
                using (new EditorGUI.DisabledScope(!allTrigger.boolValue))
                {
                    EditorGUI.BeginChangeCheck();
                    bool sel = EditorGUILayout.ToggleLeft("Select Trigger (선택 트리거 모두 필요)", selectTrigger.boolValue);
                    if (EditorGUI.EndChangeCheck()) { selectTrigger.boolValue = sel; if (sel) countTrigger.boolValue = false; }
                    using (new EditorGUI.DisabledScope(!selectTrigger.boolValue))
                        EditorGUILayout.PropertyField(selectTriggerColliders, new GUIContent("Selected Colliders"), true);

                    EditorGUI.BeginChangeCheck();
                    bool cnt = EditorGUILayout.ToggleLeft("Count Trigger (N개 이상 필요)", countTrigger.boolValue);
                    if (EditorGUI.EndChangeCheck()) { countTrigger.boolValue = cnt; if (cnt) selectTrigger.boolValue = false; }
                    using (new EditorGUI.DisabledScope(!countTrigger.boolValue))
                        EditorGUILayout.PropertyField(countThreshold, new GUIContent("Count Threshold"));
                }

                if (justGoMode.boolValue && !triggerMode.boolValue)
                    EditorGUILayout.HelpBox("JustGo는 Trigger 모드가 켜져 있어야 시작됩니다.", MessageType.Warning);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
