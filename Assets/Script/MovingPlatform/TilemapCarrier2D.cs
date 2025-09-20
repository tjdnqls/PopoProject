using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[DefaultExecutionOrder(32760)]
[RequireComponent(typeof(TilemapCollider2D))]
[DisallowMultipleComponent]
public class TilemapCarrier2D : MonoBehaviour
{
    public enum CarryMode { AddPlatformVelocity, MoveDeltaWithPlatform }
    public enum XFollowMode { PositionSnap, VelocityAdd, Hybrid }

    [Header("Layer Names (auto-resolve)")]
    [SerializeField] private string playerLayerName = "Player";
    [SerializeField] private string boxLayerName = "Box";

    [Header("Carrier Zone")]
    [SerializeField] private Vector2 padding = new Vector2(0.1f, 0.1f);
    [SerializeField] private int maxHits = 64;
    [SerializeField] private float maxCarryDeltaPerStep = 3.0f;

    [Header("Apply")]
    [SerializeField] private CarryMode carryMode = CarryMode.AddPlatformVelocity;
    [SerializeField] private XFollowMode xFollowMode = XFollowMode.PositionSnap;

    [Header("‘위에 서 있음’(AABB)")]
    [SerializeField] private float topSnapHeight = 0.18f;
    [SerializeField] private float sideEpsilon = 0.05f;
    [SerializeField] private float xSnapEpsilon = 0.0015f;

    [Header("Jump Release(점프 시 X 스냅 잠금 해제)")]
    [Tooltip("이 속도 이상 위로 튀면 점프했다고 간주")]
    [SerializeField] private float jumpVyThreshold = 1.2f;
    [Tooltip("이전 프레임 대비 상향 속도 증가량이 이 값 이상이면 점프 간주(충분히 민감)")]
    [SerializeField] private float jumpVyDeltaThreshold = 1.5f;
    [Tooltip("점프 인식 후 X-스냅을 끄는 시간(초)")]
    [SerializeField] private float releaseGrace = 0.12f;

    [Header("Debug")]
    [SerializeField] private bool drawGizmo = false;
    [SerializeField] private Color gizmoColor = new Color(0f, 1f, 1f, 0.18f);

    private TilemapCollider2D tileCol;
    private CompositeCollider2D composite;
    private Rigidbody2D rbTile;
    private int targetMask;
    private Collider2D[] hits;

    private Vector2 prevPos;
    private bool hasPrevPos;

    private readonly Dictionary<Rigidbody2D, Vector2> lastAddedVel = new(64);
    private readonly HashSet<Rigidbody2D> touchedThisStep = new();
    private static readonly List<Rigidbody2D> s_toRemove = new(32);

    // 점프 릴리즈 추적
    private readonly Dictionary<Rigidbody2D, float> releaseUntil = new(64);
    private readonly Dictionary<Rigidbody2D, float> prevVy = new(64);

    private void Reset()
    {
        tileCol = GetComponent<TilemapCollider2D>();
        composite = GetComponent<CompositeCollider2D>();
        rbTile = GetComponent<Rigidbody2D>();
    }

    private void Awake()
    {
        tileCol = GetComponent<TilemapCollider2D>();
        composite = GetComponent<CompositeCollider2D>();
        rbTile = GetComponent<Rigidbody2D>();

        targetMask = LayerMask.GetMask(playerLayerName, boxLayerName);
        hits = new Collider2D[Mathf.Max(8, maxHits)];
    }

    private void OnValidate()
    {
        if (maxHits < 8) maxHits = 8;
        if (padding.x < 0f) padding.x = 0f;
        if (padding.y < 0f) padding.y = 0f;
        if (topSnapHeight < 0.02f) topSnapHeight = 0.02f;
        if (xSnapEpsilon < 0.0005f) xSnapEpsilon = 0.0005f;
        if (releaseGrace < 0.02f) releaseGrace = 0.02f;
        targetMask = LayerMask.GetMask(playerLayerName, boxLayerName);
        if (hits == null || hits.Length != Mathf.Max(8, maxHits))
            hits = new Collider2D[Mathf.Max(8, maxHits)];
    }

    private void FixedUpdate()
    {
        Vector2 nowPos = rbTile ? rbTile.position : (Vector2)transform.position;
        if (!hasPrevPos) { prevPos = nowPos; hasPrevPos = true; return; }

        Vector2 frameDelta = nowPos - prevPos;
        if (maxCarryDeltaPerStep > 0f)
        {
            float m = maxCarryDeltaPerStep;
            frameDelta.x = Mathf.Clamp(frameDelta.x, -m, m);
            frameDelta.y = Mathf.Clamp(frameDelta.y, -m, m);
        }
        float dt = Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : 0.02f;
        Vector2 platformVel = dt > 0f ? frameDelta / dt : Vector2.zero;

        Bounds plat = (composite ? composite.bounds : tileCol.bounds);
        Vector2 center = plat.center;
        Vector2 size = plat.size + (Vector3)padding * 2f;

        touchedThisStep.Clear();
        int count = Physics2D.OverlapBoxNonAlloc(center, size, 0f, hits, targetMask);
        for (int i = 0; i < count; i++)
        {
            var col = hits[i];
            if (!col) continue;
            var rb = col.attachedRigidbody;
            if (!rb || rb == rbTile) continue;
            if (!touchedThisStep.Add(rb)) continue;

            // 점프 릴리즈 갱신(상향 속도 또는 급증 감지)
            float vy = rb.linearVelocity.y;
            float pvy = prevVy.TryGetValue(rb, out var _p) ? _p : vy;
            prevVy[rb] = vy;

            bool jumpDetected =
                (vy > jumpVyThreshold && (vy - pvy) > 0.0001f) ||
                ((vy - pvy) >= jumpVyDeltaThreshold);

            if (jumpDetected) releaseUntil[rb] = Time.time + releaseGrace;
            bool releaseActive = releaseUntil.TryGetValue(rb, out var until) && until > Time.time;

            // --- '위에' 판정(AABB 근사) ---
            Bounds pb = col.bounds;
            float platformTop = plat.max.y;
            bool verticallyOnTop = pb.min.y <= platformTop + 0.02f && pb.min.y >= platformTop - topSnapHeight;
            bool horizontallyOverlap =
                pb.max.x >= plat.min.x - sideEpsilon && pb.min.x <= plat.max.x + sideEpsilon;
            bool onTop = verticallyOnTop && horizontallyOverlap;

            switch (carryMode)
            {
                case CarryMode.AddPlatformVelocity:
                    {
                        if (lastAddedVel.TryGetValue(rb, out var prevAdded))
                            rb.linearVelocity -= prevAdded;

                        Vector2 toAdd = platformVel;

                        // X축 처리: 릴리즈 중이거나(onTop=false와 동일 처리) 아니면 스냅/하이브리드
                        if (xFollowMode == XFollowMode.PositionSnap)
                        {
                            if (onTop && !releaseActive) toAdd.x = 0f; // 스냅으로 처리할 거라 X속도 추가 안 함
                        }
                        else if (xFollowMode == XFollowMode.Hybrid)
                        {
                            if (onTop && !releaseActive) toAdd.x *= 0.35f;
                        }
                        // Y축은 그대로 상속
                        rb.linearVelocity += toAdd;
                        lastAddedVel[rb] = toAdd;

                        // === X 스냅(점프 릴리즈 동안엔 스냅 금지) ===
                        if (xFollowMode != XFollowMode.VelocityAdd && onTop && !releaseActive && Mathf.Abs(frameDelta.x) > 0f)
                        {
                            float wantX = rb.position.x + frameDelta.x;
                            if (Mathf.Abs(wantX - rb.position.x) > xSnapEpsilon)
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

        // 존에서 벗어난 리지드바디 정리
        if (carryMode == CarryMode.AddPlatformVelocity && lastAddedVel.Count > 0)
        {
            s_toRemove.Clear();
            foreach (var kv in lastAddedVel)
            {
                if (!touchedThisStep.Contains(kv.Key))
                {
                    if (kv.Key) kv.Key.linearVelocity -= kv.Value;
                    s_toRemove.Add(kv.Key);
                }
            }
            for (int i = 0; i < s_toRemove.Count; i++) lastAddedVel.Remove(s_toRemove[i]);
        }

        prevPos = nowPos;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmo) return;
        if (!tileCol) tileCol = GetComponent<TilemapCollider2D>();
        if (!composite) composite = GetComponent<CompositeCollider2D>();

        Bounds b = composite ? composite.bounds : (tileCol ? tileCol.bounds : new Bounds(transform.position, Vector3.one));
        Vector3 c = b.center;
        Vector3 s = b.size + (Vector3)padding * 2f;

        Gizmos.color = gizmoColor; Gizmos.DrawCube(c, s);
        Gizmos.color = Color.cyan; Gizmos.DrawWireCube(c, s);
    }
}
