using UnityEngine;

public class PlatformMotionReporter : MonoBehaviour
{
    public Vector2 DeltaPosition { get; private set; }
    public Vector2 FixedVelocity { get; private set; }

    Vector3 _lastFixedPos;

    void Start()
    {
        _lastFixedPos = transform.position;
    }

    // 플랫폼이 Update/LateUpdate에서 Transform으로 움직여도,
    // Fixed 때 '그 사이 총 이동량'을 샘합니다.
    void FixedUpdate()
    {
        Vector3 now = transform.position;
        Vector2 delta = (Vector2)(now - _lastFixedPos);
        DeltaPosition = delta;
        FixedVelocity = delta / Mathf.Max(Time.fixedDeltaTime, 1e-6f);
        _lastFixedPos = now;
    }
}
