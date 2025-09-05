// ParabolaJumpArcMouse.cs (점프 파워 배수 추가)
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public class ParabolaJumpMouse : MonoBehaviour
{
    public Rigidbody2D rb;

    [Header("Jump Arc (AnimationCurve)")]
    // (0,0) (0.2,0.6) (0.55,1.0) (0.85,0.15) (1.0,-0.35) 같은 형태 권장
    public AnimationCurve jumpArc = AnimationCurve.EaseInOut(0, 0, 1, 0);

    [Header("Base Distances")]
    [Tooltip("기본 수평 총 이동거리(미터)")]
    public float horizontalDistance = 7.0f;
    [Tooltip("커브 1.0이 의미하는 기본 높이(미터)")]
    public float verticalHeight = 3.2f;

    [Header("Power Scale")]
    [Tooltip("점프 파워 배수(높이+거리 동시 스케일)")]
    public float powerScale = 1.35f;        //  이 값만 키워도 ‘점프력’이 즉시 커집니다.
    [Tooltip("좌/우 클릭마다 파워 보정(선택)")]
    public float leftExtraScale = 1.00f;
    public float rightExtraScale = 1.00f;

    [Header("Timing")]
    public float arcDuration = 0.32f;       // 길면 느긋, 짧으면 경쾌
    public float jumpCooldown = 0.08f;
    public float coyoteTime = 0.08f;

    [Header("Physics/Control")]
    public bool zeroGravityDuringArc = true;
    public float followTightness = 1.12f;   // 1.0~1.3 권장
    public LayerMask groundLayer;
    public Vector2 groundCheckBoxPadding = new Vector2(0.02f, 0.06f);
    public float groundCheckDistance = 0.05f;
    public bool snapToGroundOnEnd = true;
    public float groundSnapRay = 0.6f;

    private Collider2D col;
    private float lastGroundedTime = -999f, lastJumpTime = -999f;
    private bool isArc = false;
    private float arcTimer = 0f, dirSign = +1f;
    private Vector2 arcStartPos;
    private float originalGravityScale;

    void Reset() { rb = GetComponent<Rigidbody2D>(); col = GetComponent<Collider2D>(); }
    void Awake() { if (!rb) rb = GetComponent<Rigidbody2D>(); col = GetComponent<Collider2D>(); originalGravityScale = rb.gravityScale; }

    void Update()
    {
        if (IsGrounded()) lastGroundedTime = Time.time;
        if (isArc || Time.time - lastJumpTime < jumpCooldown) return;

        if (Input.GetMouseButtonDown(0)) StartArc(-1f, powerScale * leftExtraScale);   // 좌
        else if (Input.GetMouseButtonDown(1)) StartArc(+1f, powerScale * rightExtraScale); // 우
    }

    void FixedUpdate()
    {
        if (!isArc) return;

        arcTimer += Time.fixedDeltaTime;
        float t = Mathf.Clamp01(arcTimer / Mathf.Max(0.0001f, arcDuration));

        // 현재 점프에 사용한 스케일(시작 시 저장해둔 값을 사용)
        float scale = _runScale;
        float x = dirSign * (horizontalDistance * scale) * t;
        float y = (verticalHeight * scale) * jumpArc.Evaluate(t);
        Vector2 target = arcStartPos + new Vector2(x, y);

        Vector2 toTarget = target - rb.position;
        Vector2 desiredVel = (toTarget / Mathf.Max(Time.fixedDeltaTime, 1e-6f)) * Mathf.Max(0.1f, followTightness);
        rb.linearVelocity = desiredVel;

        if (t >= 1f)
        {
            isArc = false;
            if (zeroGravityDuringArc) rb.gravityScale = originalGravityScale;

            if (snapToGroundOnEnd)
            {
                var hit = Physics2D.Raycast(rb.position, Vector2.down, groundSnapRay, groundLayer);
                if (hit.collider) { rb.position = hit.point + Vector2.up * 0.01f; rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f); }
            }
        }
    }

    // — 내부: 이번 점프에 사용될 스케일 저장(좌/우 보정 포함)
    private float _runScale = 1f;

    private void StartArc(float sign, float scale)
    {
        bool canJump = IsGrounded() || (Time.time - lastGroundedTime) <= coyoteTime;
        if (!canJump) return;

        dirSign = Mathf.Sign(sign);
        arcStartPos = rb.position;
        arcTimer = 0f;
        isArc = true;
        lastJumpTime = Time.time;
        _runScale = Mathf.Max(0.1f, scale); // 음수/0 방지

        if (zeroGravityDuringArc) { originalGravityScale = rb.gravityScale; rb.gravityScale = 0f; }

        // 첫 프레임 살짝 출발
        float t0 = Mathf.Min(0.02f / Mathf.Max(arcDuration, 0.02f), 0.08f);
        float x0 = dirSign * (horizontalDistance * _runScale) * t0;
        float y0 = (verticalHeight * _runScale) * jumpArc.Evaluate(t0);
        Vector2 target0 = arcStartPos + new Vector2(x0, y0);
        rb.linearVelocity = (target0 - rb.position) / Mathf.Max(Time.fixedDeltaTime, 1e-6f);
    }

    private bool IsGrounded()
    {
        Bounds b = col.bounds;
        Vector2 size = new Vector2(b.size.x + groundCheckBoxPadding.x, b.size.y + groundCheckBoxPadding.y);
        Vector2 origin = new Vector2(b.center.x, b.min.y);
        return Physics2D.BoxCast(origin, size, 0f, Vector2.down, groundCheckDistance, groundLayer).collider != null;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!col) col = GetComponent<Collider2D>();
        Bounds b = col.bounds;
        Vector2 size = new Vector2(b.size.x + groundCheckBoxPadding.x, b.size.y + groundCheckBoxPadding.y);
        Vector2 origin = new Vector2(b.center.x, b.min.y);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(origin + Vector2.down * groundCheckDistance, size);
    }
#endif
}
