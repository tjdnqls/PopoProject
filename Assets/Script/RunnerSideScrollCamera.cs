using UnityEngine;

[RequireComponent(typeof(Camera))]
[DisallowMultipleComponent]
public class RunnerSideScrollCamera : MonoBehaviour
{
    [Header("Move")]
    [SerializeField] private float moveSpeedX = 3.0f; // 천천히 오른쪽
    [SerializeField] private bool freezeYAtBegin = true;
    [SerializeField] private float fixedY = 0f;

    [Header("Start From")]
    [SerializeField] private bool useCurrentPositionOnEnable = true;

    private bool _active;
    private float _z;

    void Awake()
    {
        _z = transform.position.z;
        if (useCurrentPositionOnEnable) fixedY = transform.position.y;
        gameObject.SetActive(false); // 기본은 비활성화, 매니저가 켬
    }

    public void Begin()
    {
        if (freezeYAtBegin) fixedY = transform.position.y;
        _active = true;
    }

    public void End()
    {
        _active = false;
    }

    void LateUpdate()
    {
        if (!_active) return;

        var p = transform.position;
        p.x += moveSpeedX * Time.deltaTime;
        p.y = fixedY;
        p.z = _z; // 깊이 유지
        transform.position = p;
    }
}
