using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class ZiplineHandle : MonoBehaviour
{
    public ZiplinePath path;
    [Tooltip("초당 진행 속도(경로를 따라 m/s 느낌)")]
    public float travelSpeed = 6f;

    [Tooltip("양 끝에서 반사(왕복)할지, t를 0 또는 1로 고정(단방향)할지")]
    public bool pingPong = true;

    [Tooltip("시작 t(0=시작점, 1=끝점)")]
    [Range(0f, 1f)] public float startT = 0f;

    [Tooltip("초기 진행 방향(+1 전진, -1 역진)")]
    public int direction = +1;

    [Header("디버그")]
    [SerializeField] private float t; // 현재 파라미터
    [SerializeField] private Vector2 estLinearVelocity; // 추정 선형속도

    private Vector3 prevPos;

    void Reset()
    {
        var col = GetComponent<Collider2D>();
        col.isTrigger = true; // 플레이어가 '잡는' 용도로 트리거 추천
    }

    void Start()
    {
        t = Mathf.Clamp01(startT);
        if (path != null && path.IsValid)
        {
            transform.position = path.GetPoint(t);
            prevPos = transform.position;
        }
    }

    void Update()
    {
        if (path == null || !path.IsValid) return;

        float dt = Mathf.Max(Time.deltaTime, 1e-6f);
        float delta = (travelSpeed / Mathf.Max(path.totalLength, 1e-6f)) * direction * dt;
        t += delta;

        if (pingPong)
        {
            if (t > 1f) { t = 1f; direction = -1; }
            else if (t < 0f) { t = 0f; direction = +1; }
        }
        else
        {
            t = Mathf.Clamp01(t);
            // 단방향이면 끝에서 멈춤
        }

        Vector3 newPos = path.GetPoint(t);
        estLinearVelocity = (newPos - prevPos) / dt;
        transform.position = newPos;
        prevPos = newPos;
    }

    // 현재 진행 속도 벡터(월드)
    public Vector2 CurrentLinearVelocity => estLinearVelocity;

    // 현재 진행 방향(접선)
    public Vector2 CurrentTangent()
    {
        if (path == null) return Vector2.right;
        return path.GetTangent(t);
    }

    public float CurrentT => t;
}
