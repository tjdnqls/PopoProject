using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class VerticalJumpPad : MonoBehaviour
{
    public PlayerMouseMovement carryjump;

    [Header("Jump Settings")]
    [Tooltip("발판이 부여하는 위쪽 초기 속도(y).")]
    public float jumpUpSpeed = 14f;
    [Tooltip("재걸이를 안고있을때 위쪽 초기 속도(y).")]
    public float carryjumpupspeed = 9f;

    [Header("Slow Fall Settings")]
    [Tooltip("느린 낙하 지속 시간(초).")]
    public float slowFallDuration = 0.8f;
    [Tooltip("재걸이를 안고있을때 낙하 지속 시간(초).")]
    public float carryslowFallDuration = 1.2f;
    [Tooltip("느린 낙하 중 최대 하강 속도(절댓값, m/s). 값이 작을수록 천천히 떨어집니다.")]
    public float maxFallSpeed = 6f;
    [Tooltip("재걸이를 안고있을때 느린 낙하 중 최대 하강 속도(절댓값, m/s). 값이 작을수록 천천히 떨어집니다.")]
    public float carrymaxFallSpeed = 8f;

    [Header("Filter")]
    [Tooltip("플레이어 레이어만 반응하려면 지정하세요. 비워도 동작합니다.")]
    public LayerMask playerMask;

    [Header("Misc")]
    [Tooltip("같은 프레임/짧은 시간에 중복 트리거 방지(초).")]
    public float rehitLockTime = 0.1f;
    [Tooltip("위에서 내려올 때만 작동하도록 제한할지 여부.")]
    public bool requireLandingFromAbove = true;

    private float lastFireTime = -999f;

    void Reset()
    {
        // 기본은 트리거 권장(콜리전도 지원)
        var col = GetComponent<Collider2D>();
        if (col != null) col.isTrigger = true;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if(carryjump.isCarried == true)
        {
            CarryTryFire(other.attachedRigidbody);
        }
        else
        {
            TryFire(other.attachedRigidbody);
        }
        
    }

    void OnCollisionEnter2D(Collision2D col)
    {
        if (carryjump.isCarried == true)
        {
            CarryTryFire(col.rigidbody, col);
        }
        else
        {
            TryFire(col.rigidbody, col);
        }
            
    }

    private void TryFire(Rigidbody2D rb, Collision2D col = null)
    {
        if (rb == null) return;

        if (playerMask.value != 0)
        {
            if (((1 << rb.gameObject.layer) & playerMask.value) == 0) return;
        }

        if (Time.time < lastFireTime + rehitLockTime) return;

        if (requireLandingFromAbove)
        {
            // 이미 위로 가는 중이면 패스
            if (rb.linearVelocity.y > 0.05f) return;

            // 콜리전인 경우: 상대가 발판보다 위에서 내려온 상황만 허용(대략 판정)
            if (col != null && rb.worldCenterOfMass.y + 0.01f < transform.position.y) return;
        }

        var boost = rb.GetComponent<PlayerPadBoost>();
        if (boost == null) boost = rb.gameObject.AddComponent<PlayerPadBoost>();

        boost.ApplyPadBoost(
            jumpUpSpeed: jumpUpSpeed,
            slowFallDuration: slowFallDuration,
            maxFallSpeed: maxFallSpeed
        );

        lastFireTime = Time.time;
    }

    private void CarryTryFire(Rigidbody2D rb, Collision2D col = null)
    {
        if (rb == null) return;

        if (playerMask.value != 0)
        {
            if (((1 << rb.gameObject.layer) & playerMask.value) == 0) return;
        }

        if (Time.time < lastFireTime + rehitLockTime) return;

        if (requireLandingFromAbove)
        {
            // 이미 위로 가는 중이면 패스
            if (rb.linearVelocity.y > 0.05f) return;

            // 콜리전인 경우: 상대가 발판보다 위에서 내려온 상황만 허용(대략 판정)
            if (col != null && rb.worldCenterOfMass.y + 0.01f < transform.position.y) return;
        }

        var boost = rb.GetComponent<PlayerPadBoost>();
        if (boost == null) boost = rb.gameObject.AddComponent<PlayerPadBoost>();

        boost.ApplyPadBoost(
            jumpUpSpeed: carryjumpupspeed,
            slowFallDuration: carryslowFallDuration,
            maxFallSpeed: carrymaxFallSpeed
        );

        lastFireTime = Time.time;
    }
}
