using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerPadBoost : MonoBehaviour
{
    [Header("Ground Check")]
    [Tooltip("접지 판정을 위한 레이어(프로젝트의 Ground 레이어 지정).")]
    public LayerMask groundLayer;
    [Tooltip("레이 시작 오프셋(플레이어 무게중심 기준 아래쪽 권장).")]
    public Vector2 groundCheckOffset = new Vector2(0f, -0.5f);
    [Tooltip("접지 레이 길이.")]
    public float groundCheckDistance = 0.15f;

    private Rigidbody2D rb;

    // 버프 상태
    private bool padActive = false;
    private float padEndTime = 0f;
    private float padMaxFallSpeed = 6f;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    /// <summary>
    /// 점프 패드 버프 적용: 초기 위속 + 느린 낙하
    /// </summary>
    public void ApplyPadBoost(float jumpUpSpeed, float slowFallDuration, float maxFallSpeed)
    {
        // 1) 즉시 위로 초기 속도 세팅(던지는 느낌을 일정하게 만들기 위해 고정 대입)
        Vector2 v = rb.linearVelocity;
        v.y = jumpUpSpeed;
        rb.linearVelocity = v;

        // 2) 낙하 속도 클램프를 위한 상태값 세팅
        padActive = true;
        padEndTime = Time.time + Mathf.Max(0f, slowFallDuration);
        padMaxFallSpeed = Mathf.Max(0.1f, maxFallSpeed);
    }

    void FixedUpdate()
    {
        if (!padActive) return;

        // 시간이 끝났거나 접지되면 버프 종료
        if (Time.time >= padEndTime || IsGrounded())
        {
            padActive = false;
            return;
        }

        // 느린 낙하: 하강 중이면 최대 낙하속도 제한
        Vector2 v = rb.linearVelocity;
        if (v.y < -padMaxFallSpeed)
        {
            v.y = -padMaxFallSpeed;
            rb.linearVelocity = v;
        }
    }

    private bool IsGrounded()
    {
        Vector2 origin = (Vector2)transform.position + groundCheckOffset;
        RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, groundCheckDistance, groundLayer);
        Debug.DrawRay(origin, Vector2.down * groundCheckDistance, hit.collider ? Color.green : Color.red);
        return hit.collider != null;
    }
}
