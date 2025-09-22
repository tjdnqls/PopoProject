using System;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(32760)]
[DisallowMultipleComponent]
public class ObjectCarrier2DMulti : MonoBehaviour
{
    public enum CarryMode { AddPlatformVelocity, MoveDeltaWithPlatform }
    public enum XFollowMode { PositionSnap, VelocityAdd, Hybrid }

    [Header("Layer Names (auto-resolve)")]
    [SerializeField] private string playerLayerName = "Player";
    [SerializeField] private string boxLayerName = "Box";

    [Header("Multi Modules")]
    [Tooltip("여러 개의 운반체(플랫폼)를 한 번에 처리합니다.")]
    public bool multiModules = true;

    [Serializable]
    public class CarrierModule
    {
        [Header("Module Root / Zone / Platform")]
        public string name;
        [Tooltip("플랫폼의 루트 Transform (없으면 이 컴포넌트의 transform)")]
        public Transform root;
        [Tooltip("운반 영역으로 사용할 Collider2D (없으면 root의 Collider2D 자동 할당)")]
        public Collider2D zoneCollider;
        [Tooltip("플랫폼의 이동 델타/속도를 계산할 Rigidbody2D (없으면 root의 Rigidbody2D 또는 Transform)")]
        public Rigidbody2D rbPlatform;

        [Header("Carrier Zone")]
        public Vector2 padding = new Vector2(0.1f, 0.1f);
        [Min(8)] public int maxHits = 64;
        [Tooltip("플랫폼이 한 스텝에 이동한 것으로 간주할 최대 델타(발광체 순간이동 등 급격 이동 시 안전장치)")]
        public float maxCarryDeltaPerStep = 3.0f;

        [Header("Apply")]
        public CarryMode carryMode = CarryMode.AddPlatformVelocity;
        public XFollowMode xFollowMode = XFollowMode.PositionSnap;

        [Header("‘위에 서 있음’(AABB)")]
        [Tooltip("플랫폼 윗면에서 이 거리 안이면 '위에'로 간주")]
        public float topSnapHeight = 0.18f;
        [Tooltip("X축으로 이 정도 오버랩이면 옆면으로 치지 않고 위에로 인정")]
        public float sideEpsilon = 0.05f;
        [Tooltip("스냅 이동 최소 허용치(너무 작은 이동 억제)")]
        public float xSnapEpsilon = 0.0015f;

        [Header("Jump Release(점프 시 X 스냅 해제)")]
        [Tooltip("이 속도 이상 위로 튀면 점프했다고 간주")]
        public float jumpVyThreshold = 1.2f;
        [Tooltip("이전 프레임 대비 상향 속도 증가량이 이 값 이상이면 점프 간주(민감)")]
        public float jumpVyDeltaThreshold = 1.5f;
        [Tooltip("점프 인식 후 X-스냅을 끄는 시간(초)")]
        public float releaseGrace = 0.12f;

        [Header("Debug")]
        public bool drawGizmo = false;
        public Color gizmoColor = new Color(0f, 1f, 1f, 0.18f);

        // ─── Runtime caches per module ───
        [NonSerialized] public Vector2 prevPos;
        [NonSerialized] public bool hasPrevPos;
        [NonSerialized] public Collider2D[] hitsBuf;

        [NonSerialized] public readonly Dictionary<Rigidbody2D, Vector2> lastAddedVel = new(64);
        [NonSerialized] public readonly HashSet<Rigidbody2D> touchedThisStep = new();
        [NonSerialized] public readonly List<Rigidbody2D> toRemove = new(32);

        // 점프 릴리즈 추적
        [NonSerialized] public readonly Dictionary<Rigidbody2D, float> releaseUntil = new(64);
        [NonSerialized] public readonly Dictionary<Rigidbody2D, float> prevVy = new(64);
    }

    public List<CarrierModule> modules = new();

    private int targetMask;

    private void Awake()
    {
        targetMask = LayerMask.GetMask(playerLayerName, boxLayerName);

        if (modules == null || modules.Count == 0)
        {
            // 폴백: 자신을 하나의 모듈로 구성
            var m = new CarrierModule { name = name, root = transform };
            m.zoneCollider = GetComponent<Collider2D>();
            m.rbPlatform = GetComponent<Rigidbody2D>();
            m.maxHits = Mathf.Max(8, m.maxHits);
            m.hitsBuf = new Collider2D[m.maxHits];
            modules = new List<CarrierModule> { m };
        }

        // 자동 할당/버퍼 준비
        foreach (var m in modules)
        {
            if (!m.root) m.root = transform;
            if (!m.zoneCollider) m.zoneCollider = m.root.GetComponent<Collider2D>();
            if (!m.rbPlatform) m.rbPlatform = m.root.GetComponent<Rigidbody2D>();
            int need = Mathf.Max(8, m.maxHits);
            m.hitsBuf = (m.hitsBuf == null || m.hitsBuf.Length != need) ? new Collider2D[need] : m.hitsBuf;
        }
    }

    private void OnValidate()
    {
        targetMask = LayerMask.GetMask(playerLayerName, boxLayerName);
        if (modules == null) return;
        foreach (var m in modules)
        {
            if (m == null) continue;
            if (m.maxHits < 8) m.maxHits = 8;
            if (m.padding.x < 0f) m.padding.x = 0f;
            if (m.padding.y < 0f) m.padding.y = 0f;
            if (m.topSnapHeight < 0.02f) m.topSnapHeight = 0.02f;
            if (m.xSnapEpsilon < 0.0005f) m.xSnapEpsilon = 0.0005f;
            if (m.releaseGrace < 0.02f) m.releaseGrace = 0.02f;
            int need = Mathf.Max(8, m.maxHits);
            if (m.hitsBuf == null || m.hitsBuf.Length != need) m.hitsBuf = new Collider2D[need];
        }
    }

    private void FixedUpdate()
    {
        if (modules == null) return;
        foreach (var m in modules)
        {
            if (m == null) continue;
            TickModule(m);
        }
    }

    private void TickModule(CarrierModule m)
    {
        Vector2 nowPos = m.rbPlatform ? m.rbPlatform.position : (Vector2)(m.root ? m.root.position : transform.position);
        if (!m.hasPrevPos) { m.prevPos = nowPos; m.hasPrevPos = true; return; }

        Vector2 frameDelta = nowPos - m.prevPos;
        if (m.maxCarryDeltaPerStep > 0f)
        {
            float cap = m.maxCarryDeltaPerStep;
            frameDelta.x = Mathf.Clamp(frameDelta.x, -cap, cap);
            frameDelta.y = Mathf.Clamp(frameDelta.y, -cap, cap);
        }

        float dt = Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : 0.02f;
        Vector2 platformVel = dt > 0f ? frameDelta / dt : Vector2.zero;

        Bounds plat = GetModuleBounds(m);
        Vector2 center = plat.center;
        Vector2 size = plat.size + (Vector3)m.padding * 2f;

        m.touchedThisStep.Clear();
        int count = Physics2D.OverlapBoxNonAlloc(center, size, 0f, m.hitsBuf, targetMask);

        for (int i = 0; i < count; i++)
        {
            var col = m.hitsBuf[i];
            if (!col) continue;
            var rb = col.attachedRigidbody;
            if (!rb || rb == m.rbPlatform) continue;
            if (!m.touchedThisStep.Add(rb)) continue;

            // 점프 릴리즈 갱신
            float vy = rb.linearVelocity.y;
            float pvy = m.prevVy.TryGetValue(rb, out var _p) ? _p : vy;
            m.prevVy[rb] = vy;

            bool jumpDetected =
                (vy > m.jumpVyThreshold && (vy - pvy) > 0.0001f) ||
                ((vy - pvy) >= m.jumpVyDeltaThreshold);

            if (jumpDetected) m.releaseUntil[rb] = Time.time + m.releaseGrace;
            bool releaseActive = m.releaseUntil.TryGetValue(rb, out var until) && until > Time.time;

            // '위에' 판정(AABB 근사)
            Bounds pb = col.bounds;
            float platformTop = plat.max.y;
            bool verticallyOnTop = pb.min.y <= platformTop + 0.02f && pb.min.y >= platformTop - m.topSnapHeight;
            bool horizontallyOverlap =
                pb.max.x >= plat.min.x - m.sideEpsilon && pb.min.x <= plat.max.x + m.sideEpsilon;
            bool onTop = verticallyOnTop && horizontallyOverlap;

            switch (m.carryMode)
            {
                case CarryMode.AddPlatformVelocity:
                    {
                        if (m.lastAddedVel.TryGetValue(rb, out var prevAdded))
                            rb.linearVelocity -= prevAdded;

                        Vector2 toAdd = platformVel;

                        if (m.xFollowMode == XFollowMode.PositionSnap)
                        {
                            if (onTop && !releaseActive) toAdd.x = 0f;
                        }
                        else if (m.xFollowMode == XFollowMode.Hybrid)
                        {
                            if (onTop && !releaseActive) toAdd.x *= 0.35f;
                        }

                        rb.linearVelocity += toAdd;
                        m.lastAddedVel[rb] = toAdd;

                        // X-스냅(릴리즈 중엔 금지)
                        if (m.xFollowMode != XFollowMode.VelocityAdd && onTop && !releaseActive && Mathf.Abs(frameDelta.x) > 0f)
                        {
                            float wantX = rb.position.x + frameDelta.x;
                            if (Mathf.Abs(wantX - rb.position.x) > m.xSnapEpsilon)
                                rb.MovePosition(new Vector2(wantX, rb.position.y));
                        }
                        break;
                    }

                case CarryMode.MoveDeltaWithPlatform:
                    {
                        rb.MovePosition(rb.position + frameDelta);
                        break;
                    }
            }
        }

        // 존에서 벗어난 리지드 정리
        if (m.carryMode == CarryMode.AddPlatformVelocity && m.lastAddedVel.Count > 0)
        {
            m.toRemove.Clear();
            foreach (var kv in m.lastAddedVel)
            {
                if (!m.touchedThisStep.Contains(kv.Key))
                {
                    if (kv.Key) kv.Key.linearVelocity -= kv.Value;
                    m.toRemove.Add(kv.Key);
                }
            }
            for (int i = 0; i < m.toRemove.Count; i++) m.lastAddedVel.Remove(m.toRemove[i]);
        }

        m.prevPos = nowPos;
    }

    private Bounds GetModuleBounds(CarrierModule m)
    {
        if (m.zoneCollider) return m.zoneCollider.bounds;

        // 콜라이더 미지정 시: root 하위 콜라이더 합집합
        var cols = m.root ? m.root.GetComponentsInChildren<Collider2D>() : Array.Empty<Collider2D>();
        bool any = false;
        Bounds acc = new Bounds(m.root ? m.root.position : transform.position, Vector3.zero);
        foreach (var c in cols)
        {
            if (!c) continue;
            if (!any) { acc = c.bounds; any = true; }
            else acc.Encapsulate(c.bounds);
        }
        if (!any) acc = new Bounds(m.root ? m.root.position : transform.position, Vector3.one);
        return acc;
    }

    private void OnDrawGizmosSelected()
    {
        if (modules == null) return;
        foreach (var m in modules)
        {
            if (m == null || !m.drawGizmo) continue;
            Bounds b = GetModuleBounds(m);
            Vector3 c = b.center;
            Vector3 s = b.size + (Vector3)m.padding * 2f;

            Gizmos.color = m.gizmoColor; Gizmos.DrawCube(c, s);
            Gizmos.color = Color.cyan; Gizmos.DrawWireCube(c, s);
        }
    }
}
