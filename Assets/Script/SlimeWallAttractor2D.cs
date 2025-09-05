using UnityEngine;

/// <summary>
/// 붙어있는 동안 플레이어를 벽 쪽(법선 반대 방향)으로 "흡착"시키고
/// 아래로는 천천히 미끄러지게 만든다.
/// 슬라임 오브젝트(벽) 쪽에 붙여서 사용.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class SlimeWallAttractor2D : MonoBehaviour
{
    [Header("Who is Player")]
    [Tooltip("플레이어가 속한 레이어 마스크")]
    public LayerMask playerMask;

    [Header("Suction / Stickiness")]
    [Tooltip("벽 쪽으로 당기는 지속 힘(Force). 값이 클수록 더 강하게 붙음.")]
    public float pullForce = 70f;

    [Tooltip("벽 바깥으로 튀어나가려는(법선 방향) 속도를 깎는 계수")]
    public float normalDamping = 12f;

    [Header("Slide")]
    [Tooltip("슬라임에 붙었을 때 최대 하강 속도(음수)")]
    public float slideMaxFall = -5.5f;

    [Tooltip("접촉을 '측면 벽'으로 간주할 최소 법선 X 성분")]
    [Range(0f, 1f)]
    public float sideNormalThreshold = 0.55f;

    private Collider2D _col;

    void Awake()
    {
        _col = GetComponent<Collider2D>();
        // 트리거 슬라임도 지원하려면 쿼리 옵션 ON
        Physics2D.queriesHitTriggers = true;
    }

    // --- Non-Trigger 슬라임 ---
    void OnCollisionStay2D(Collision2D c)
    {
        if (!IsPlayer(c.collider)) return;
        var rb = c.rigidbody;
        if (rb == null) return;

        // 접촉점들 중 "옆면"에 해당하는 법선 선택
        Vector2 n = BestSideNormal(c, sideNormalThreshold);
        if (n == Vector2.zero) return; // 위/아래 접촉 등은 무시

        ApplySuction(rb, n);
    }

    // --- Trigger 슬라임 ---
    void OnTriggerStay2D(Collider2D other)
    {
        if (!IsPlayer(other)) return;
        var rb = other.attachedRigidbody;
        if (rb == null) return;

        // 트리거는 법선이 없으니 상대 중심 위치로 좌/우 추정
        Vector2 n = GuessSideNormalFromCenters(other.bounds.center, _col.bounds.center);
        if (n == Vector2.zero) return;

        ApplySuction(rb, n);
    }

    // ======================================================

    bool IsPlayer(Collider2D col)
    {
        return (playerMask.value & (1 << col.gameObject.layer)) != 0;
    }

    static Vector2 BestSideNormal(Collision2D c, float minAbsNx)
    {
        Vector2 best = Vector2.zero;
        float bestScore = 0f;

        for (int i = 0; i < c.contactCount; i++)
        {
            var n = c.GetContact(i).normal;
            float ax = Mathf.Abs(n.x);
            // 옆면(법선이 X축 쪽)만 채택, 위/아래(법선 Y축 위주)는 제외
            if (ax >= minAbsNx && Mathf.Abs(n.y) < 0.7f)
            {
                if (ax > bestScore)
                {
                    bestScore = ax;
                    best = n;
                }
            }
        }
        return best; // 왼쪽 벽이면 n≈(+1,0), 오른쪽 벽이면 n≈(-1,0)
    }

    static Vector2 GuessSideNormalFromCenters(Vector2 playerCenter, Vector2 wallCenter)
    {
        float dx = playerCenter.x - wallCenter.x;
        if (Mathf.Abs(dx) < 0.001f) return Vector2.zero;
        // 벽의 "바깥 법선"을 가정: 플레이어가 벽의 왼쪽에 있으면 벽 법선은 +X, 오른쪽에 있으면 -X
        return (dx < 0f) ? Vector2.right : Vector2.left;
    }

    void ApplySuction(Rigidbody2D rb, Vector2 wallNormal)
    {
        var pm = rb ? rb.GetComponent<PlayerMouseMovement>() : null;
        if (pm != null && (pm.isCarrying || pm.isCarried || pm.IsSlimeSuppressed))
            return; // ★ 캐리/이탈 유예 중엔 흡착 무시

        Vector2 pullDir = -wallNormal;
        rb.AddForce(pullDir * pullForce, ForceMode2D.Force);

        float vn = Vector2.Dot(rb.linearVelocity, wallNormal);
        if (vn > 0f)
        {
            float damp = Mathf.Min(vn, normalDamping);
            rb.linearVelocity -= wallNormal * damp;
        }

        var v = rb.linearVelocity;
        if (v.y < slideMaxFall)
        {
            v.y = slideMaxFall;
            rb.linearVelocity = v;
        }
    }
}
