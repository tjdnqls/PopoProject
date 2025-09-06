using UnityEngine;

[DisallowMultipleComponent]
public class MeleeHitboxOnce : MonoBehaviour
{
    [Header("Damage / Behavior")]
    [SerializeField] private int damage = 1;
    [SerializeField] private bool disableOnHit = true;
    [SerializeField] private bool doInitialSweep = true;   // 활성 직후 이미 겹친 대상이 있으면 즉시 판정

    private bool _armed;
    private bool _hasHit;
    private Transform _owner;      // 공격자 엔티티의 루트(=자기 자신만 제외). ※ .root 사용하지 않음!
    private Collider2D _col;

    private static readonly Collider2D[] _overlaps = new Collider2D[8];

    private void Awake()
    {
        _col = GetComponent<Collider2D>();
        if (_col == null)
            Debug.LogError("[MeleeHitboxOnce] Collider2D가 필요합니다.");
    }

    /// <summary>히트박스 무장 (활성) </summary>
    public void Arm(int dmg, Transform ownerRoot)
    {
        damage = dmg;
        _owner = ownerRoot ? ownerRoot : transform; // ★ 최상위 root 금지: 전달받은 실제 소유자 트랜스폼
        _hasHit = false;
        _armed = true;

        gameObject.SetActive(true);

        // 활성 직후 겹침 검사(이미 맞닿아 시작하는 경우)
        if (doInitialSweep) InitialOverlapSweep();
        if (_hasHit && disableOnHit) Disarm();
    }

    /// <summary>히트박스 비무장 (비활성)</summary>
    public void Disarm()
    {
        _armed = false;
        gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        _hasHit = false;
        if (_owner == null) _owner = transform;
    }

    // ---------- 내부 유틸 ----------

    /// <summary>
    /// 콜라이더에서 '대상 엔티티' GameObject 뽑기.
    /// - Rigidbody2D가 있으면 그 게임오브젝트(보통 캐릭터 루트)를 사용
    /// - 없으면 콜라이더 자신의 게임오브젝트
    /// </summary>
    private static GameObject PickEntityObject(Collider2D c)
    {
        if (!c) return null;
        return c.attachedRigidbody ? c.attachedRigidbody.gameObject : c.gameObject;
    }

    /// <summary>같은 엔티티(자기 자신)인지 검사</summary>
    private bool IsSelf(GameObject otherGO)
    {
        if (_owner == null || otherGO == null) return false;
        var t = otherGO.transform;
        return t == _owner || t.IsChildOf(_owner); // ★ 오직 내 엔티티 하위만 자기 자신으로 간주
    }

    /// <summary>한 번만 데미지 적용</summary>
    private void TryHit(GameObject otherGO)
    {
        if (!_armed || _hasHit || otherGO == null) return;
        if (IsSelf(otherGO)) return; // 자기 자신/자기 하위 제외

        // HP는 종종 상위에 붙어 있으므로 부모까지 탐색
        var p1 = otherGO.GetComponentInParent<Player1HP>();
        if (p1 != null)
        {
            p1.TakeDamage(damage);
            _hasHit = true;
#if UNITY_EDITOR
            Debug.Log("[MeleeHitboxOnce] Player1에게 타격됨", otherGO);
#endif
        }
        else
        {
            var p2 = otherGO.GetComponentInParent<Player2HP>();
            if (p2 != null)
            {
                p2.TakeDamage(damage);
                _hasHit = true;
#if UNITY_EDITOR
                Debug.Log("[MeleeHitboxOnce] Player2에게 타격됨", otherGO);
#endif
            }
        }

        if (_hasHit && disableOnHit) Disarm();
    }

    /// <summary>활성 직후 겹침 검사(이미 포개져 있는 대상 즉시 처리)</summary>
    private void InitialOverlapSweep()
    {
        if (_col == null) return;

        // 레이어 필터가 필요하면 여기에서 설정
        ContactFilter2D filter = new ContactFilter2D
        {
            useTriggers = true,
            useLayerMask = false
        };

#if UNITY_2021_2_OR_NEWER
        int count = _col.Overlap(filter, _overlaps); // Unity 2021.2~ 에서 제공
#else
        int count = Physics2D.OverlapCollider(_col, filter, _overlaps);
#endif

        for (int i = 0; i < count; i++)
        {
            var c = _overlaps[i];
            if (!c) continue;

            var go = PickEntityObject(c);
            TryHit(go);
            if (_hasHit) break; // 한 번만
        }
    }

    // ---------- 이벤트 ----------

    private void OnTriggerEnter2D(Collider2D other)
    {
        var go = PickEntityObject(other);
        TryHit(go);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        var go = PickEntityObject(other);
        TryHit(go);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        var go = PickEntityObject(collision.collider);
        TryHit(go);
    }
}
