using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
[DisallowMultipleComponent]
public class BossWalker : MonoBehaviour
{
    [Header("Move")]
    [SerializeField] private float speed = 1.5f;       // 초당 이동 속도(+X)
    [SerializeField] private float accel = 0f;         // 0이면 즉시 목표속도, >0이면 서서히 접근
    [SerializeField] private bool keepYVelocity = true;// 중력/점프 등 Y속도 유지 여부
    [SerializeField] private bool autoStart = true;    // 시작 시 자동 이동

    [Header("Physics")]
    [SerializeField] private bool zeroGravity = false; // 보스가 떠다니면 켜기
    [SerializeField] private bool freezeRotation = true;

    private Rigidbody2D rb;
    private bool active;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        if (zeroGravity) rb.gravityScale = 0f;
        if (freezeRotation) rb.freezeRotation = true;
    }

    void OnEnable()
    {
        if (autoStart) Begin();
    }

    public void Begin() { active = true; }
    public void Pause() { active = false; }
    public void Resume() { active = true; }
    public void Stop()
    {
        active = false;
        var v = rb.linearVelocity;
        v.x = 0f;
        if (!keepYVelocity) v.y = 0f;
        rb.linearVelocity = v;
    }

    void FixedUpdate()
    {
        if (!active) return;

        float targetX = speed; // 오른쪽(+X) 고정
        var v = rb.linearVelocity;

        if (accel > 0f)
        {
            v.x = Mathf.MoveTowards(v.x, targetX, accel * Time.fixedDeltaTime);
        }
        else
        {
            v.x = targetX;
        }

        if (!keepYVelocity) v.y = 0f;
        rb.linearVelocity = v;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        // 진행 방향 표시
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.8f);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.right * 1.0f);
    }
#endif
}
