using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerPlatformRider2D : MonoBehaviour
{
    [Tooltip("법선.y가 이 값 이상이면 '위에서 밟았다'로 간주")]
    public float upNormalThreshold = 0.5f;
    [Tooltip("플랫폼과의 미세한 틈을 메우는 아래쪽 스냅(미터)")]
    public float snapDownMax = 0.05f;

    Rigidbody2D _rb;
    PlatformMotionReporter _platform;  // 현재 밟고 있는 플랫폼
    bool _onTop;                       // 위에서 밟는 중인지

    void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
    }

    void FixedUpdate()
    {
        if (_platform == null || !_onTop) return;

        // 1) 플랫폼의 고정프레임 Δpos만큼 플레이어를 같이 MovePosition
        Vector2 carryDelta = _platform.DeltaPosition;
        if (carryDelta.sqrMagnitude > 0f)
        {
            _rb.MovePosition(_rb.position + carryDelta);
        }

        // 2) 수직 속도는 플랫폼보다 아래로 끌려가지 않도록 보정
        //    (점프 중 위로 날아갈 자유는 유지)
        Vector2 v = _rb.linearVelocity;
        if (v.y < _platform.FixedVelocity.y)
            v.y = _platform.FixedVelocity.y;
        _rb.linearVelocity = v;

        // 3) 미세한 틈이 생기면 살짝 아래로 스냅(통통 튐 방지)
        //    (플레이어 바닥에서 아주 작은 Ray로 체크)
        Vector2 origin = _rb.position + Vector2.down * 0.01f;
        RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, snapDownMax, ~0);
        if (hit && hit.collider.GetComponent<PlatformMotionReporter>() == _platform)
        {
            float gap = hit.distance;
            if (gap > 1e-4f)
                _rb.MovePosition(_rb.position + Vector2.down * gap);
        }
    }

    void OnCollisionEnter2D(Collision2D col) { TryAttach(col); }
    void OnCollisionStay2D(Collision2D col) { TryAttach(col); }
    void OnCollisionExit2D(Collision2D col)
    {
        if (_platform != null && col.collider.GetComponent<PlatformMotionReporter>() == _platform)
        {
            _platform = null;
            _onTop = false;
        }
    }

    void TryAttach(Collision2D col)
    {
        var rep = col.collider.GetComponent<PlatformMotionReporter>();
        if (rep == null) return;

        // '위에서' 밟은 접촉만 인정
        bool top = false;
        foreach (var c in col.contacts)
        {
            if (c.normal.y >= upNormalThreshold) { top = true; break; }
        }

        if (top)
        {
            _platform = rep;
            _onTop = true;
        }
    }
}
