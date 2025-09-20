using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
[DefaultExecutionOrder(100)] // 플랫폼 델타 계산 이후에 적용
public class StickyGroundFollower2D : MonoBehaviour
{
    [Header("Ground Settings")]
    [SerializeField] private string groundLayerName = "Ground";
    [Tooltip("바닥으로 인정할 최소 법선.y (클수록 '지면'만 인정)")]
    [Range(0f, 1f)][SerializeField] private float minGroundNormalY = 0.35f;

    [Header("Behavior")]
    [Tooltip("바닥이 움직인 만큼 위치 델타를 그대로 가산(권장).")]
    [SerializeField] private bool applyAsPositionDelta = true;

    [Tooltip("지면에서 떨어진 직후 잠깐은 계속 붙어있게(coyote 느낌)")]
    [SerializeField] private float coyoteStickSeconds = 0.06f;

    Rigidbody2D rb;
    int groundLayer;
    MovingPlatformMotion2D currentPlatform;
    float groundedUntil;
    Vector2 pendingDelta;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        groundLayer = LayerMask.NameToLayer(groundLayerName);

        // 물리 안정화 권장 세팅(상황 맞게 조정)
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
    }

    void FixedUpdate()
    {
        // 지면으로 인식 중이면 해당 프레임의 플랫폼 델타를 누적
        if (currentPlatform != null || Time.fixedTime <= groundedUntil)
        {
            if (currentPlatform != null)
                pendingDelta += currentPlatform.Delta;
        }

        if (applyAsPositionDelta && pendingDelta != Vector2.zero)
        {
            // 플레이어 자신의 이동 + 충돌은 그대로 두고,
            // 플랫폼 델타만 '추가 위치 이동'으로 보정
            rb.MovePosition(rb.position + pendingDelta);
            pendingDelta = Vector2.zero;
        }
    }

    void OnCollisionStay2D(Collision2D col)
    {
        // Ground 레이어만 체크
        if (col.collider.gameObject.layer != groundLayer) return;

        // 바닥 법선이 충분히 위쪽을 향하는 접점만 지면으로 인정
        for (int i = 0; i < col.contactCount; i++)
        {
            var cp = col.GetContact(i);
            if (cp.normal.y >= minGroundNormalY)
            {
                groundedUntil = Time.fixedTime + coyoteStickSeconds;

                // 해당 바닥에 MovingPlatformMotion2D가 있으면 참조
                var plat = col.collider.GetComponentInParent<MovingPlatformMotion2D>();
                currentPlatform = plat; // 없으면 null(정지 바닥)
                break;
            }
        }
    }

    void OnCollisionExit2D(Collision2D col)
    {
        if (col.collider.gameObject.layer != groundLayer) return;

        // 떠났다면 플랫폼 참조 해제(코요테 타임은 FixedUpdate에서 처리)
        var plat = col.collider.GetComponentInParent<MovingPlatformMotion2D>();
        if (plat != null && plat == currentPlatform)
            currentPlatform = null;
    }
}