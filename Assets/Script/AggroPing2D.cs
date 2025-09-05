using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class AggroPing2D : MonoBehaviour
{
    private Rigidbody2D rb;
    private Collider2D col;

    private MonsterABPatrolFSM owner;
    private Transform target;
    private float speed = 100f;
    private float life;
    private System.Action onDespawn;

    // 마스크
    public LayerMask groundMask;
    public LayerMask playerMask;
    public LayerMask obstacleMask;

    private Vector2 dir; // 발사 시 고정(필요하면 추적 업데이트로 바꿀 수 있음)
    private float spawnTime;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        rb.gravityScale = 0f;
        col.isTrigger = true; // 트리거 충돌로 처리 권장
    }

    public void Init(
        MonsterABPatrolFSM owner,
        Transform target,
        float speed,
        float lifetime,
        LayerMask groundMask,
        LayerMask playerMask,
        LayerMask obstacleMask,
        System.Action onDespawn)
    {
        this.owner = owner;
        this.target = target;
        this.speed = speed;
        this.life = lifetime;
        this.groundMask = groundMask;
        this.playerMask = playerMask;
        this.obstacleMask = obstacleMask;
        this.onDespawn = onDespawn;

        spawnTime = Time.time;

        Vector2 origin = transform.position;
        Vector2 aim = target ? (Vector2)(target.TryGetComponent<Collider2D>(out var c)
                   ? c.bounds.center : target.position) : origin + Vector2.right;
        dir = (aim - origin).normalized;

        // 즉시 속도 부여
        var v = rb.linearVelocity; v = dir * speed; rb.linearVelocity = v;
    }

    private void FixedUpdate()
    {
        // 계속 직진
        rb.linearVelocity = dir * speed;

        // 수명 종료
        if (Time.time - spawnTime >= life)
        {
            Despawn();
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        int otherLayer = other.gameObject.layer;

        // 1) 그라운드/장애물에 닿으면 사라짐(=라인이 막힌 것으로 간주)
        if (((1 << otherLayer) & (groundMask.value | obstacleMask.value)) != 0)
        {
            Despawn();
            return;
        }

        // 2) 플레이어에 닿으면 해당 플레이어에게 어그로
        if (((1 << otherLayer) & playerMask.value) != 0)
        {
            Transform hitT = other.attachedRigidbody ? other.attachedRigidbody.transform : other.transform;
            owner?.OnAggroPingHit(hitT);
            Despawn();
            return;
        }

        // 그 외는 무시(필요시 레이어 매트릭스로 충돌 차단 권장)
    }

    private void Despawn()
    {
        onDespawn?.Invoke();
        Destroy(gameObject);
    }
}