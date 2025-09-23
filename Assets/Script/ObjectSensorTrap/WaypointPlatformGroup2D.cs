using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[DisallowMultipleComponent]
public class WaypointPlatformGroup2D : MonoBehaviour
{
    public enum PathMode { PingPong, Loop, OneShot }
    public enum MoveMode { ConstantSpeed, EaseInOut }
    public enum AnchorMode { ColliderBoundsCenter, TransformPivot, Custom }
    [Serializable] public class Node { public Transform point; public float waitSeconds = 0f; }

    // ======= Path =======
    [Header("Path")]
    [SerializeField] private Node[] nodes;
    [SerializeField] private PathMode pathMode = PathMode.PingPong;
    [SerializeField] private MoveMode moveMode = MoveMode.EaseInOut;
    [SerializeField] private float speed = 2.5f;
    [SerializeField] private float easeDistance = 0.3f;

    [Header("Start")]
    [SerializeField] private int startIndex = 0;
    [SerializeField] private bool startReverse = false;
    [SerializeField] private bool snapToStartNode = true;
    [SerializeField] private bool waitAtStartNode = false;

    // ======= Trigger / Hold / JustGo =======
    [Header("Trigger / Hold (작동 가드)")]
    [SerializeField] private bool triggerMode = false;
    [SerializeField] private bool holdMode = false;
    [SerializeField] private bool stopHold = false;
    [SerializeField] private bool stopHoldReverse = false;

    [Header("JustGo (Trigger 필요, 왕복 N회 후 정지)")]
    [SerializeField] private bool justGoMode = false;
    [SerializeField, Min(1)] private int justGoRoundTrips = 1;

    [Header("Trigger Sources")]
    [SerializeField] private List<Collider2D> triggerColliders = new();
    [SerializeField] private bool allTrigger = false;
    [SerializeField] private bool selectTrigger = false;
    [SerializeField] private List<Collider2D> selectTriggerColliders = new();
    [SerializeField] private bool countTrigger = false;
    [SerializeField] private int countThreshold = 1;

    [Header("Trigger Layers / Perf")]
    [SerializeField] private string playerLayerName = "Player";
    [SerializeField] private string boxLayerName = "Box";
    [SerializeField] private int triggerMaxHits = 32;
    [SerializeField, Range(0.05f, 1f)] private float returnSpeedMul = 0.5f;

    // ======= Movers (Multi Modules) =======
    [Serializable]
    public class Mover
    {
        public string name;
        public Rigidbody2D rb;
        public Collider2D col;
        public Transform customAnchor;
        public Vector2 extraOffset;
        [Header("Anchor Mode")]
        public AnchorMode anchorMode = AnchorMode.ColliderBoundsCenter;

        [NonSerialized] public Vector2 anchorOffset;
        [NonSerialized] public Vector2 prevPos;
        [NonSerialized] public bool hasPrev;
        [NonSerialized] public Vector2 PlatformDelta;
        [NonSerialized] public Vector2 PlatformVelocity;

        [NonSerialized] public List<Collider2D> allCols;
    }

    [Header("Multi Modules")]
    public bool multiModules = true;
    public List<Mover> movers = new();

    // ======= 새 옵션: Movers 간 충돌 무시 & Pivot =======
    [Header("Movers Collision / Pivot")]
    public bool ignoreCollisionsAmongMovers = true;
    public int pivotMoverIndex = 0;
    public Collider2D pivotColliderOverride;
    public bool alignPathToPivotAtStart = true;

    // ======= Sound =======
    [Header("Sound")]
    [SerializeField] private bool useMoveLoop = true;
    [SerializeField] private string moveLoopKey = "MovingPlatform";
    [SerializeField] private Transform moveLoopAttach; // null이면 this.transform
    [SerializeField, Tooltip("이동 판정 민감도(제곱거리)")] private float moveEpsilon = 1e-6f;
    private bool _loopPlaying = false;

    // ======= Debug =======
    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private Color pathColor = new Color(1f, 0.9f, 0.2f, 0.9f);
    [SerializeField] private Color pointColor = new Color(0.3f, 1f, 0.9f, 0.9f);
    [SerializeField] private Color triggerGizmoColor = new Color(0.2f, 0.8f, 1f, 0.18f);

    // ======= Internals (Group driver) =======
    private int dir = +1;
    private int curr;
    private float waitTimer = 0f;
    private int triggerMask;
    private Collider2D[] sharedBuf;

    private Vector2 groupPos;
    private bool groupPosInit;
    private Vector2 pathOffsetFromPivot;

    private bool triggered = false;
    private bool contacting = false;

    private bool justGoActive = false;
    private int justGoTripsDone = 0;
    private int justGoHomeEdge = -1;
    private bool justGoVisitedOppositeEdge = false;
    private bool justGoBlockUntilContactClears = false;

    private struct ColPair { public Collider2D a, b; }
    private readonly List<ColPair> ignoredPairs = new();

    private void Awake()
    {
        if (nodes == null || nodes.Length < 2) AutoCollectWaypointsIfEmpty();

        startIndex = Mathf.Clamp(startIndex, 0, Math.Max(0, (nodes?.Length ?? 1) - 1));
        dir = startReverse ? -1 : +1;

        if (!groupPosInit)
        {
            Vector2 startAnchor = (nodes != null && nodes.Length > 0 && nodes[startIndex].point)
                ? (Vector2)nodes[startIndex].point.position
                : (Vector2)transform.position;

            groupPos = startAnchor;
            groupPosInit = true;
            if (waitAtStartNode && nodes != null && nodes.Length > startIndex)
                waitTimer = Mathf.Max(0f, nodes[startIndex].waitSeconds);

            curr = NextIndexFrom(startIndex, ref dir, pathMode, nodes?.Length ?? 0);
        }

        foreach (var m in movers)
        {
            if (m == null || !m.rb) continue;
            m.anchorOffset = ComputeAnchorOffset(m);
            m.allCols = CollectAllColliders(m);
        }

        RecomputePathOffsetFromPivot();

        triggerMask = LayerMask.GetMask(playerLayerName, boxLayerName);
        sharedBuf = new Collider2D[Mathf.Max(8, triggerMaxHits)];

        triggered = !triggerMode;
        contacting = false;

        justGoActive = false;
        justGoTripsDone = 0;
        justGoHomeEdge = -1;
        justGoVisitedOppositeEdge = false;
        justGoBlockUntilContactClears = false;

        if (ignoreCollisionsAmongMovers) ApplyIgnoreCollisionsAmongMovers(true);

        if (useMoveLoop && moveLoopAttach == null) moveLoopAttach = transform;
    }

    private void OnEnable()
    {
        if (ignoreCollisionsAmongMovers) ApplyIgnoreCollisionsAmongMovers(true);
    }

    private void OnDisable()
    {
        if (ignoredPairs.Count > 0) ApplyIgnoreCollisionsAmongMovers(false);
        SetLoopState(false);
    }

    private void OnDestroy()
    {
        SetLoopState(false);
    }

    private void OnValidate()
    {
        if (speed < 0.01f) speed = 0.01f;
        if (easeDistance < 0f) easeDistance = 0f;
        if (triggerMaxHits < 8) triggerMaxHits = 8;
        if (countThreshold < 1) countThreshold = 1;
        if (justGoRoundTrips < 1) justGoRoundTrips = 1;

        if (!triggerMode) { holdMode = false; stopHold = false; stopHoldReverse = false; }
        else
        {
            if (!allTrigger) { selectTrigger = false; countTrigger = false; }
            if (selectTrigger && countTrigger) countTrigger = false;
            int n = triggerColliders != null ? triggerColliders.Count(c => c) : 0;
            if (n > 0 && countThreshold > n) countThreshold = n;
        }

        if (justGoMode)
        {
            holdMode = false; stopHold = false; stopHoldReverse = false;
            if (pathMode != PathMode.PingPong) pathMode = PathMode.PingPong;
        }
    }

    // ======= PIVOT 계산 =======
    private void RecomputePathOffsetFromPivot()
    {
        pathOffsetFromPivot = Vector2.zero;
        if (!alignPathToPivotAtStart || nodes == null || nodes.Length == 0 || nodes[startIndex].point == null)
            return;

        Vector2 nodeStart = (Vector2)nodes[startIndex].point.position;
        Vector2 pivotAnchor = GetPivotAnchorWorld();
        pathOffsetFromPivot = pivotAnchor - nodeStart;
        groupPos = nodeStart + pathOffsetFromPivot;
    }

    private Vector2 GetPivotAnchorWorld()
    {
        if (pivotColliderOverride) return (Vector2)pivotColliderOverride.bounds.center;

        if (movers != null && pivotMoverIndex >= 0 && pivotMoverIndex < movers.Count)
        {
            var m = movers[pivotMoverIndex];
            if (m != null && m.rb) return m.rb.position + m.anchorOffset;
        }
        return groupPos;
    }

    // ======= 충돌 무시 세팅 =======
    private List<Collider2D> CollectAllColliders(Mover m)
    {
        var list = new List<Collider2D>(8);
        if (m.col) list.Add(m.col);
        if (m.rb)
        {
            var cols = m.rb.GetComponentsInChildren<Collider2D>(includeInactive: true);
            foreach (var c in cols) if (c && !list.Contains(c)) list.Add(c);
        }
        return list;
    }

    private void ApplyIgnoreCollisionsAmongMovers(bool enable)
    {
        if (!enable && ignoredPairs.Count > 0)
        {
            foreach (var p in ignoredPairs)
                if (p.a && p.b) Physics2D.IgnoreCollision(p.a, p.b, false);
            ignoredPairs.Clear();
            return;
        }

        if (!enable || movers == null) return;

        ignoredPairs.Clear();
        for (int i = 0; i < movers.Count; i++)
        {
            var A = movers[i]; if (A == null || A.allCols == null) continue;
            for (int j = i + 1; j < movers.Count; j++)
            {
                var B = movers[j]; if (B == null || B.allCols == null) continue;
                foreach (var ca in A.allCols)
                {
                    if (!ca) continue;
                    foreach (var cb in B.allCols)
                    {
                        if (!cb) continue;
                        Physics2D.IgnoreCollision(ca, cb, true);
                        ignoredPairs.Add(new ColPair { a = ca, b = cb });
                    }
                }
            }
        }
    }

    // ======= 메인 루프 =======
    private void FixedUpdate()
    {
        Vector2 prevGroupPos = groupPos;

        bool conditionMet = false;
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
                    if (!justGoBlockUntilContactClears && conditionMet) StartJustGo();
                    if (!conditionMet) justGoBlockUntilContactClears = false;
                }
                triggered = justGoActive;
                contacting = false;
            }
            else
            {
                if (!triggered && conditionMet) triggered = true;
                contacting = conditionMet;
            }
        }
        else
        {
            contacting = false;
            triggered = true;
        }

        if (!triggered)
        {
            UpdateMoverVelocities();
            SetLoopState(false);
            return;
        }

        if (!justGoMode)
        {
            if (triggerMode && (stopHold || stopHoldReverse))
            {
                bool allowAdvance = stopHold ? contacting : !contacting;
                if (!allowAdvance) { UpdateMoverVelocities(); SetLoopState(false); return; }
            }
            else if (triggerMode && holdMode)
            {
                if (!contacting)
                {
                    MoveGroupTowardsNode(startIndex, Mathf.Max(0.01f, speed * returnSpeedMul));
                    ApplyGroupPositionToMovers();
                    bool moved = (groupPos - prevGroupPos).sqrMagnitude > moveEpsilon;
                    SetLoopState(moved);
                    return;
                }
            }
        }

        if (nodes == null || nodes.Length < 2 || nodes[Mathf.Clamp(curr, 0, nodes.Length - 1)].point == null)
        {
            UpdateMoverVelocities(); SetLoopState(false); return;
        }
        if (waitTimer > 0f)
        {
            waitTimer -= Time.fixedDeltaTime;
            UpdateMoverVelocities(); SetLoopState(false); return;
        }

        Vector2 targetAnchor = (Vector2)nodes[curr].point.position + pathOffsetFromPivot;

        float step = speed * Time.fixedDeltaTime;
        if (moveMode == MoveMode.EaseInOut && easeDistance > 0f)
        {
            float dist = Vector2.Distance(groupPos, targetAnchor);
            float t = Mathf.Clamp01(dist / Mathf.Max(0.0001f, easeDistance));
            step *= Mathf.SmoothStep(0.15f, 1f, t);
        }

        Vector2 nextGroup = Vector2.MoveTowards(groupPos, targetAnchor, step);
        bool arrived = (nextGroup - targetAnchor).sqrMagnitude < 1e-6f;

        MoveAllMovers(nextGroup);
        bool movedNow = (nextGroup - prevGroupPos).sqrMagnitude > moveEpsilon;

        if (arrived)
        {
            int arrivedIndex = curr;
            waitTimer = Mathf.Max(0f, nodes[curr].waitSeconds);
            curr = NextIndexFrom(curr, ref dir, pathMode, nodes.Length);

            if (justGoMode && justGoActive && pathMode == PathMode.PingPong && nodes.Length >= 2)
            {
                int last = nodes.Length - 1;
                if (arrivedIndex == 0 || arrivedIndex == last) HandleJustGoEdge(arrivedIndex, last);
            }
        }

        groupPos = nextGroup;
        SetLoopState(movedNow);
    }

    private void MoveGroupTowardsNode(int nodeIndex, float spd)
    {
        if (nodes == null || nodes.Length == 0 || nodes[nodeIndex].point == null) return;
        Vector2 target = (Vector2)nodes[nodeIndex].point.position + pathOffsetFromPivot;

        float step = spd * Time.fixedDeltaTime;
        if (moveMode == MoveMode.EaseInOut && easeDistance > 0f)
        {
            float dist = Vector2.Distance(groupPos, target);
            float t = Mathf.Clamp01(dist / Mathf.Max(0.0001f, easeDistance));
            step *= Mathf.SmoothStep(0.15f, 1f, t);
        }

        Vector2 next = Vector2.MoveTowards(groupPos, target, step);
        MoveAllMovers(next);
        groupPos = next;
    }

    private void MoveAllMovers(Vector2 nextGroup)
    {
        foreach (var m in movers)
        {
            if (m == null || !m.rb) continue;

            Vector2 nowPos = m.rb.position;
            if (!m.hasPrev) { m.prevPos = nowPos; m.hasPrev = true; }

            Vector2 targetRb = nextGroup + m.extraOffset - m.anchorOffset;
            m.rb.MovePosition(targetRb);

            m.PlatformDelta = targetRb - m.prevPos;
            m.PlatformVelocity = (Time.fixedDeltaTime > 0f) ? (m.PlatformDelta / Time.fixedDeltaTime) : Vector2.zero;
            m.prevPos = targetRb;
        }
    }

    private void ApplyGroupPositionToMovers() => MoveAllMovers(groupPos);

    private void UpdateMoverVelocities()
    {
        foreach (var m in movers)
        {
            if (m == null || !m.rb) continue;
            m.PlatformDelta = Vector2.zero;
            m.PlatformVelocity = Vector2.zero;
            if (!m.hasPrev) { m.prevPos = m.rb.position; m.hasPrev = true; }
        }
    }

    private void EvaluateTriggers(out bool anyHit, out bool allHit, out int hitCount, out bool selectedAllHit)
    {
        anyHit = false; hitCount = 0; selectedAllHit = true;

        var list = (triggerColliders != null && triggerColliders.Count > 0) ? triggerColliders.Where(c => c).ToList() : null;
        Dictionary<Collider2D, bool> cache = new(16);

        if (list == null || list.Count == 0)
        {
            Bounds b = new Bounds(transform.position, Vector3.one * 0.5f);
            bool hit = Physics2D.OverlapBox((Vector2)b.center, (Vector2)b.size, 0f, triggerMask) != null;
            anyHit = hit; allHit = hit; selectedAllHit = hit; hitCount = hit ? 1 : 0; return;
        }

        allHit = true;
        foreach (var c in list)
        {
            bool hit = OverlapColliderBox(c);
            cache[c] = hit; anyHit |= hit; allHit &= hit; if (hit) hitCount++;
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
            return Physics2D.OverlapBox((Vector2)b.center, (Vector2)b.size, 0f, triggerMask) != null;
        }
    }

    private static int NextIndexFrom(int index, ref int dir, PathMode mode, int count)
    {
        int next = index + dir;
        if (next >= 0 && next < count) return next;
        switch (mode)
        {
            case PathMode.PingPong: dir *= -1; return Mathf.Clamp(index + dir, 0, count - 1);
            case PathMode.Loop: return (count > 0) ? (next % count + count) % count : 0;
            case PathMode.OneShot: return Mathf.Clamp(index, 0, count - 1);
        }
        return index;
    }

    private Vector2 ComputeAnchorOffset(Mover m)
    {
        Vector2 anchorWorld;
        switch (m.anchorMode)
        {
            case AnchorMode.ColliderBoundsCenter:
                if (m.col) anchorWorld = (Vector2)m.col.bounds.center;
                else if (m.rb) anchorWorld = m.rb.position;
                else anchorWorld = (Vector2)(m.customAnchor ? m.customAnchor.position : transform.position);
                break;
            case AnchorMode.Custom:
                anchorWorld = m.customAnchor ? (Vector2)m.customAnchor.position : (m.rb ? m.rb.position : (Vector2)transform.position);
                break;
            default:
                anchorWorld = m.rb ? m.rb.position : (Vector2)(m.customAnchor ? m.customAnchor.position : transform.position);
                break;
        }
        Vector2 rbPos = m.rb ? m.rb.position : (Vector2)transform.position;
        return anchorWorld - rbPos;
    }

    private void AutoCollectWaypointsIfEmpty()
    {
        var list = new List<Node>();
        foreach (Transform c in transform)
            if (c.name.IndexOf("Waypoint", StringComparison.OrdinalIgnoreCase) >= 0)
                list.Add(new Node { point = c, waitSeconds = 0f });
        if (list.Count >= 2) nodes = list.ToArray();
    }

    // ======= JustGo 보조 =======
    private void StartJustGo()
    {
        justGoActive = true; justGoTripsDone = 0; justGoVisitedOppositeEdge = false; justGoBlockUntilContactClears = false;
        justGoHomeEdge = GetNearestEdgeIndex();
    }
    private int GetNearestEdgeIndex()
    {
        if (nodes == null || nodes.Length < 2) return 0;
        int last = nodes.Length - 1;
        Vector2 pos = groupPos;
        Vector2 a = (Vector2)nodes[0].point.position + pathOffsetFromPivot;
        Vector2 z = (Vector2)nodes[last].point.position + pathOffsetFromPivot;
        return ((pos - a).sqrMagnitude <= (pos - z).sqrMagnitude) ? 0 : last;
    }
    private void EndJustGo()
    {
        justGoActive = false; triggered = false; justGoBlockUntilContactClears = true;
    }
    private void HandleJustGoEdge(int arrivedEdge, int lastIndex)
    {
        if (justGoHomeEdge < 0) return;
        int opposite = (justGoHomeEdge == 0) ? lastIndex : 0;
        if (arrivedEdge == opposite) { justGoVisitedOppositeEdge = true; return; }
        if (arrivedEdge == justGoHomeEdge && justGoVisitedOppositeEdge)
        {
            justGoTripsDone++; justGoVisitedOppositeEdge = false;
            if (justGoTripsDone >= justGoRoundTrips) EndJustGo();
        }
    }

    // ======= Sound helper =======
    private void SetLoopState(bool moving)
    {
        if (!useMoveLoop) return;

        if (moving)
        {
            if (!_loopPlaying)
            {
                SoundManager.StartLoop(moveLoopKey, moveLoopAttach ? moveLoopAttach : transform);
                _loopPlaying = true;
            }
        }
        else
        {
            if (_loopPlaying)
            {
                SoundManager.StopLoop(moveLoopKey);
                _loopPlaying = false;
            }
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

        if (triggerColliders != null && triggerColliders.Count > 0)
        {
            foreach (var c in triggerColliders.Where(c => c))
            {
                var b = c.bounds;
                Gizmos.color = triggerGizmoColor; Gizmos.DrawCube(b.center, b.size);
                Gizmos.color = Color.cyan; Gizmos.DrawWireCube(b.center, b.size);
            }
        }
    }
}
