using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class MovingPlatform2D : MonoBehaviour
{
    public float moveDistance = 2f;
    public float moveSpeed = 2f;
    public bool moveUp = true;
    public bool loop = true;      // true면 왕복
    public bool activeOnStart = false;

    public Vector2 CurrentVelocity { get; private set; }

    Vector2 _startPos;
    Vector2 _endPos;
    Rigidbody2D _rb;
    float _t;           // 0~1 구간 왕복용
    int _dir = 1;       // 진행 방향
    bool _active;

    void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();

        // 이 부분 삭제
        // _rb.bodyType = Rigidbody2D.Kinematic;

        _rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        _startPos = transform.position;
        Vector2 delta = (moveUp ? Vector2.up : Vector2.down) * moveDistance;
        _endPos = _startPos + delta;
        _active = activeOnStart;
    }


    public void SetActive(bool on) => _active = on;

    void FixedUpdate()
    {
        if (!_active) { CurrentVelocity = Vector2.zero; return; }

        // 왕복 보간
        float step = (moveSpeed / Mathf.Max(0.0001f, moveDistance)) * Time.fixedDeltaTime * _dir;
        float prevT = _t;
        _t = Mathf.Clamp01(_t + step);

        Vector2 from = (_dir > 0) ? _startPos : _endPos;
        Vector2 to = (_dir > 0) ? _endPos : _startPos;

        Vector2 newPos = Vector2.Lerp(from, to, _t);
        Vector2 prevPos = _rb.position;
        _rb.MovePosition(newPos);

        // 이번 Fixed 프레임의 플랫폼 속도
        CurrentVelocity = (newPos - prevPos) / Time.fixedDeltaTime;

        // 끝점 도달 시 방향 전환(루프일 때)
        if (loop && (_t <= 0f || _t >= 1f))
        {
            _dir *= -1;
        }
    }
}
