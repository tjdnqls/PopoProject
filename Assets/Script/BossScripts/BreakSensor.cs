using UnityEngine;

/// <summary>
/// 자식 트리거 콜라이더가 Ground 등과 닿을 때 부모 컨트롤러에 알려줍니다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public class BreakSensor : MonoBehaviour
{
    private BossRunBreakController _owner;
    private Collider2D _col;
    private LayerMask _targetMask;

    public void Setup(BossRunBreakController owner, LayerMask targetMask)
    {
        _owner = owner;
        _targetMask = targetMask;
    }

    void Awake()
    {
        _col = GetComponent<Collider2D>();
        _col.isTrigger = true;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (_owner == null) return;
        if (((1 << other.gameObject.layer) & _targetMask) == 0) return;

        // 충돌 지점 근사
        Vector2 hit = other.ClosestPoint(transform.position);
        _owner.HandleBreakHit(other, hit);
    }
}
