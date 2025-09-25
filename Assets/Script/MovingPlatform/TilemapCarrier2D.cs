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
    [SerializeField] private string slimeLayerName = "Slime";

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

    [Header("Slime 특수 처리 (Ceiling)")]
    [Tooltip("이 캐리어 오브젝트가 Slime 레이어일 때, 머리로 천장에 붙어도 따라가게 할지")]
    [SerializeField] private bool ceilingCarryOnSlime = true;
    [Tooltip("천장 붙기(AABB) 판정 높이(플랫폼 하단 기준)")]
    [SerializeField] private float bottomSnapHeight = 0.18f;
    [Tooltip("머리 위 레이 길이에 더해지는 여유")]
    [SerializeField] private float bottomRaySlack = 0.06f;

    [Header("Ceiling Motion Control (입력 감촉)")]
    [Tooltip("천장에 붙어있을 때, 플랫폼과 같은 방향으로 움직이면 추가 가속 (unit/s^2)")]
    [SerializeField] private float ceilingAssistAccel = 30f;
    [Tooltip("천장에 붙어있을 때, 플랫폼과 반대 방향이면 감속 (unit/s^2)")]
    [SerializeField] private float ceilingResistAccel = 40f;
    [Tooltip("플랫폼 대비 허용되는 최대 상대 X속도 (unit/s)")]
    [SerializeField] private float ceilingMaxRelativeSpeed = 7f;

    [Header("Debug")]
    [SerializeField] private bool drawGizmo = false;
    [SerializeField] private Color gizmoColor = new Color(0f, 1f, 1f, 0.18f);

    private TilemapCollider2D tileCol;
    private CompositeCollider2D composite;
    private Rigidbody2D rbTile;

    private int targetMask;
    private int slimeLayer;

    private Collider2D[] hits;
    private ContactFilter2D _filter;

    private Vector2 prevPos;
    private bool hasPrevPos;

    private readonly Dictionary<Rigidbody2D, Vector2> lastAddedVel = new(64);
    private readonly HashSet<Rigidbody2D> touchedThisStep = new();
    private static readonly List<Rigidbody2D> s_toRemove = new(32);

    // 점프 릴리즈 추적
    private readonly Dictionary<Rigidbody2D, float> releaseUntil = new(64);
    private readonly Dictionary<Rigidbody2D, float> prevVy = new(64);

    // 천장 레이로 얻은 직전 접점 X (플랫폼 이동량을 레이 기반으로 추적)
    private readonly Dictionary<Rigidbody2D, float> lastCeilHitX = new(64);

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
        slimeLayer = LayerMask.NameToLayer(slimeLayerName);

        hits = new Collider2D[Mathf.Max(8, maxHits)];

        // Unity 6.1: OverlapBox + ContactFilter2D 사용
        _filter = new ContactFilter2D();
        _filter.SetLayerMask(targetMask);
        _filter.useTriggers = true; // 트리거 포함 필요 없으면 false
    }

    private void OnValidate()
    {
        if (maxHits < 8) maxHits = 8;
        if (padding.x < 0f) padding.x = 0f;
        if (padding.y < 0f) padding.y = 0f;
        if (topSnapHeight < 0.02f) topSnapHeight = 0.02f;
        if (bottomSnapHeight < 0.02f) bottomSnapHeight = 0.02f;
        if (xSnapEpsilon < 0.0005f) xSnapEpsilon = 0.0005f;
        if (releaseGrace < 0.02f) releaseGrace = 0.02f;

        targetMask = LayerMask.GetMask(playerLayerName, boxLayerName);
        slimeLayer = LayerMask.NameToLayer(slimeLayerName);

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

        bool isSlimeCarrier = gameObject.layer == slimeLayer;

        touchedThisStep.Clear();
        int count = Physics2D.OverlapBox(center, size, 0f, _filter, hits);
        for (int i = 0; i < count; i++)
        {
            var col = hits[i];
            if (!col) continue;
            var rb = col.attachedRigidbody;
            if (!rb || rb == rbTile) continue;
            if (!touchedThisStep.Add(rb)) continue;

            // 점프 릴리즈 (상향 가속 감지)
            float vy = rb.linearVelocity.y;
            float pvy = prevVy.TryGetValue(rb, out var _p) ? _p : vy;
            prevVy[rb] = vy;

            bool jumpDetected =
                (vy > jumpVyThreshold && (vy - pvy) > 0.0001f) ||
                ((vy - pvy) >= jumpVyDeltaThreshold);

            if (jumpDetected) releaseUntil[rb] = Time.time + releaseGrace;
            bool releaseActive = releaseUntil.TryGetValue(rb, out var until) && until > Time.time;

            // --- 접지/부착 판정 ---
            Bounds pb = col.bounds;

            // 바닥 위(플랫폼 top 근처)
            float platformTop = plat.max.y;
            bool verticallyOnTop = pb.min.y <= platformTop + 0.02f && pb.min.y >= platformTop - topSnapHeight;
            bool horizontallyOverlapTop =
                pb.max.x >= plat.min.x - sideEpsilon && pb.min.x <= plat.max.x + sideEpsilon;
            bool onTop = verticallyOnTop && horizontallyOverlapTop;

            // 천장: 플레이어 머리 위로 레이 → 이 플랫폼을 맞췄는지 + 접점X 획득
            bool onBottom = false;
            RaycastHit2D ceilHit = default;
            if (isSlimeCarrier && ceilingCarryOnSlime && !releaseActive)
            {
                onBottom = TryGetCeilingHit(pb, plat, out ceilHit);
            }

            bool isRidingTop = onTop;
            bool isRidingCeiling = onBottom; // 릴리즈 중엔 false 처리됨
            bool isRiding = isRidingTop || isRidingCeiling;

            switch (carryMode)
            {
                case CarryMode.AddPlatformVelocity:
                    {
                        // 이전 프레임에 더한 플랫폼 속도 제거
                        if (lastAddedVel.TryGetValue(rb, out var prevAdded))
                            rb.linearVelocity -= prevAdded;

                        Vector2 toAdd = platformVel;

                        // === X 처리 정책 ===
                        if (isRidingTop)
                        {
                            // 바닥 위: 기존 로직(스냅/하이브리드)
                            if (xFollowMode == XFollowMode.PositionSnap)
                            {
                                if (!releaseActive) toAdd.x = 0f;
                            }
                            else if (xFollowMode == XFollowMode.Hybrid)
                            {
                                if (!releaseActive) toAdd.x *= 0.35f;
                            }
                        }
                        else if (isRidingCeiling)
                        {
                            // 천장: 레이 기반으로 X를 스냅(= 플랫폼 이동량만큼만 보정)
                            toAdd.x = 0f; // X속도 상속은 하지 않고, 아래 MovePosition으로 처리
                        }

                        // Y는 바닥/천장 공통으로 상속
                        rb.linearVelocity += toAdd;
                        lastAddedVel[rb] = toAdd;

                        // === X 스냅 처리 ===
                        if (isRidingTop && !releaseActive && xFollowMode != XFollowMode.VelocityAdd && Mathf.Abs(frameDelta.x) > 0f)
                        {
                            // 바닥 위: 프레임 델타 스냅
                            float wantX = rb.position.x + frameDelta.x;
                            if (Mathf.Abs(wantX - rb.position.x) > xSnapEpsilon)
                                rb.MovePosition(new Vector2(wantX, rb.position.y));
                        }
                        else if (isRidingCeiling && ceilHit.collider)
                        {
                            // 천장: 레이 접점X의 프레임 간 변화량만큼 따라감
                            float hitX = ceilHit.point.x;
                            if (lastCeilHitX.TryGetValue(rb, out float prevHitX))
                            {
                                float dx = Mathf.Clamp(hitX - prevHitX, -maxCarryDeltaPerStep, maxCarryDeltaPerStep);
                                if (Mathf.Abs(dx) > 0f)
                                    rb.MovePosition(new Vector2(rb.position.x + dx, rb.position.y));
                            }
                            lastCeilHitX[rb] = hitX;

                            // 천장 상태에서의 ‘가/감속’ 및 상대속도 제한
                            float relVx = rb.linearVelocity.x - platformVel.x; // 플랫폼 기준 상대 X속도
                            int moveDir = Mathf.Abs(relVx) > 0.001f ? (relVx > 0f ? 1 : -1) : 0;
                            int platDir = Mathf.Abs(platformVel.x) > 0.001f ? (platformVel.x > 0f ? 1 : -1) : 0;

                            if (moveDir != 0 && platDir != 0)
                            {
                                if (moveDir == platDir)
                                    relVx += ceilingAssistAccel * dt;                 // 같은 방향 → 가속
                                else
                                    relVx = Mathf.MoveTowards(relVx, 0f, ceilingResistAccel * dt); // 반대 방향 → 감속
                            }

                            relVx = Mathf.Clamp(relVx, -ceilingMaxRelativeSpeed, ceilingMaxRelativeSpeed);
                            rb.linearVelocity = new Vector2(platformVel.x + relVx, rb.linearVelocity.y);
                        }
                        else
                        {
                            // 천장 아님/릴리즈 중: 접점 기록 제거
                            if (lastCeilHitX.ContainsKey(rb)) lastCeilHitX.Remove(rb);
                        }

                        break;
                    }

                case CarryMode.MoveDeltaWithPlatform:
                    {
                        // 완전 델타 이동은 천장/바닥 동일
                        rb.MovePosition(rb.position + frameDelta);
                        // 천장 특화 가/감속만 적용
                        if (isRidingCeiling)
                        {
                            float relVx = rb.linearVelocity.x - platformVel.x;
                            int moveDir = Mathf.Abs(relVx) > 0.001f ? (relVx > 0f ? 1 : -1) : 0;
                            int platDir = Mathf.Abs(platformVel.x) > 0.001f ? (platformVel.x > 0f ? 1 : -1) : 0;
                            if (moveDir != 0 && platDir != 0)
                            {
                                if (moveDir == platDir) relVx += ceilingAssistAccel * dt;
                                else relVx = Mathf.MoveTowards(relVx, 0f, ceilingResistAccel * dt);
                            }
                            relVx = Mathf.Clamp(relVx, -ceilingMaxRelativeSpeed, ceilingMaxRelativeSpeed);
                            rb.linearVelocity = new Vector2(platformVel.x + relVx, rb.linearVelocity.y);
                        }
                        if (!isRidingCeiling && lastCeilHitX.ContainsKey(rb)) lastCeilHitX.Remove(rb);
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
                    if (lastCeilHitX.ContainsKey(kv.Key)) lastCeilHitX.Remove(kv.Key);
                }
            }
            for (int i = 0; i < s_toRemove.Count; i++) lastAddedVel.Remove(s_toRemove[i]);
        }

        prevPos = nowPos;
    }

    // 플레이어 머리 위로 레이캐스트하여 '이 타일맵의 천장' 접점 히트 얻기
    bool TryGetCeilingHit(Bounds pb, Bounds plat, out RaycastHit2D hit)
    {
        float startY = pb.max.y + 0.02f;
        float maxDist = bottomSnapHeight + Mathf.Max(0f, bottomRaySlack);

        // 레이 시작 X를 플랫폼 AABB 안쪽으로 클램프(가장자리 미스 방지)
        float clampedX = Mathf.Clamp(pb.center.x, plat.min.x + 0.05f, plat.max.x - 0.05f);
        Vector2 origin = new Vector2(clampedX, startY);

        int thisLayerMask = 1 << gameObject.layer;
        hit = Physics2D.Raycast(origin, Vector2.up, maxDist, thisLayerMask);

        if (!hit.collider) return false;

        // 반드시 '이' 타일맵(Composite 또는 TilemapCollider2D)
        if (composite && hit.collider == composite) return true;
        if (tileCol && hit.collider == tileCol) return true;

        // 같은 트랜스폼 계통 허용(합성 환경 대비)
        return hit.collider.transform == transform || hit.collider.transform.IsChildOf(transform);
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
