using UnityEngine;

[DisallowMultipleComponent]
public class MeleeHitboxOnce : MonoBehaviour
{
    [SerializeField] private int damage = 1;
    [SerializeField] private bool disableOnHit = true;
    [SerializeField] private bool doInitialSweep = true;   // �� �߰�: Ȱ�� ���� ��ħ ��ĵ

    private bool _armed;
    private bool _hasHit;
    private Transform _ownerRoot;
    private Collider2D _col;                               // �� �߰�

    void Awake()
    {
        _col = GetComponent<Collider2D>();
        if (_col == null) Debug.LogError("[MeleeHitboxOnce] Collider2D�� �ʿ��մϴ�.");
    }

    public void Arm(int dmg, Transform ownerRoot)
    {
        damage = dmg;
        _ownerRoot = ownerRoot ? ownerRoot.root : transform.root;
        _hasHit = false;
        _armed = true;

        gameObject.SetActive(true);

        // �� Ȱ�� ����, �̹� ���� �ִ� ��� �ٷ� ����
        if (doInitialSweep) InitialOverlapSweep();
        if (_hasHit && disableOnHit) Disarm();
    }

    public void Disarm()
    {
        _armed = false;
        gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        // ���� �ʱ�ȭ
        _hasHit = false;
        if (_ownerRoot == null) _ownerRoot = transform.root;
    }

    private void TryHit(GameObject otherGO)
    {
        if (!_armed || _hasHit || otherGO == null) return;
        if (_ownerRoot && otherGO.transform.root == _ownerRoot) return; // �ڱ� �ڽ� ����

        if (otherGO.TryGetComponent<Player1HP>(out var p1))
        {
            p1.TakeDamage(damage);
            _hasHit = true;
            Debug.Log("�÷��̾�1���� ���� ����");
        }
        else if (otherGO.TryGetComponent<Player2HP>(out var p2))
        {
            p2.TakeDamage(damage);
            _hasHit = true;
            Debug.Log("�÷��̾�2���� ���� ����");
        }

        if (_hasHit && disableOnHit) Disarm();
    }

    // �� �߰�: Ȱ�� ���� ��ħ üũ(�̹� ���� �ִ� ��뵵 ��� ��Ʈ)
    private static readonly Collider2D[] _overlaps = new Collider2D[8];
    private void InitialOverlapSweep()
    {
        if (_col == null) return;

        // ���̾� ���Ͱ� �ʿ��ϸ� ���⼭ �����ϼ���.
        ContactFilter2D filter = new ContactFilter2D { useTriggers = true, useLayerMask = false };

        int count = _col.Overlap(filter, _overlaps);
        for (int i = 0; i < count; i++)
        {
            var c = _overlaps[i];
            if (!c) continue;
            var go = c.attachedRigidbody ? c.attachedRigidbody.gameObject : c.gameObject;
            TryHit(go);
            if (_hasHit) break;
        }
    }

    // Trigger �̺�Ʈ
    private void OnTriggerEnter2D(Collider2D other)
    {
        TryHit(other.gameObject);
    }

    // �� ����: ��ģ ���¿��� Enter�� �� �ߴ� �����ӿ�
    private void OnTriggerStay2D(Collider2D other)
    {
        TryHit(other.gameObject);
    }

    // �浹 �ݶ��̴��� ���� ��쵵 ����
    private void OnCollisionEnter2D(Collision2D collision)
    {
        TryHit(collision.collider.gameObject);
    }
}
