using UnityEngine;

public class DraggingBullet : MonoBehaviour
{
    public float lifeTime = 10f;
    public LayerMask wallLayer;
    public float knockbackForce = 30f;  // 밀어낼 힘 크기
    public Vector2 forceDirection = Vector2.right; // 기본 방향 (불릿 방향 기반으로 바꿀 수도 있음)

    private Rigidbody2D rb;
    private bool hasHitPlayer = false;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    public void Launch(Vector2 direction, float speed)
    {
        rb.linearVelocity = direction.normalized * speed;
        forceDirection = direction.normalized; // 불릿 방향 기억
    }

    void Start()
    {
        Destroy(gameObject, lifeTime);
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (hasHitPlayer) return;

        if (collision.collider.CompareTag("Player"))
        {
            Rigidbody2D playerRb = collision.collider.GetComponent<Rigidbody2D>();
            PlayerMouseMovement playerScript = collision.collider.GetComponent<PlayerMouseMovement>();

            if (playerRb != null)
            {
                // 이전 속도 제거하고 한 번 강하게 힘을 줌
                playerRb.linearVelocity = Vector2.zero;
                playerRb.AddForce(forceDirection * knockbackForce, ForceMode2D.Impulse);
            }

            hasHitPlayer = true;

            Destroy(gameObject, 0.1f); // 즉시 제거 (or 약간의 딜레이)
        }

        // 벽에 부딪히면 그냥 파괴
        if (((1 << collision.gameObject.layer) & wallLayer) != 0)
        {
            Destroy(gameObject);
        }
    }
}
