using UnityEngine;
using UnityEngine.SceneManagement;

public class TrapProjectile : MonoBehaviour
{
    public LayerMask wallLayer;
    public LayerMask groundLayer;
    public LayerMask eventLayer;

    public float lifeTime = 10f;
    private Rigidbody2D rb;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        Destroy(gameObject, lifeTime); // 일정 시간 지나면 자동 제거
    }

    public void Launch(Vector2 direction, float speed)
    {
        if (TryGetComponent(out Rigidbody2D rb))
            rb.linearVelocity = direction.normalized * speed;
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        int hitLayer = collision.gameObject.layer;

        bool isWall = ((1 << hitLayer) & wallLayer) != 0;
        bool isGround = ((1 << hitLayer) & groundLayer) != 0;
        bool isEvent = ((1 << hitLayer) & eventLayer) != 0;

        if (isWall || isGround || isEvent)
        {
            Destroy(gameObject); // 벽, 바닥, 이벤트 그라운드에 부딪히면 파괴
        }
        else if (collision.collider.CompareTag("Player"))
        {
            Debug.Log("플레이어 즉사!");
            var other = collision.collider.gameObject;
            var p1 = other.GetComponent<Player1HP>();
            if (p1) { p1.TakeDamage(1); return; }

            var p2 = other.GetComponent<Player2HP>();
            if (p2) { p2.TakeDamage(1); return; }
            Destroy(gameObject);
        }
    }

}
