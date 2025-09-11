using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class OneShotMeleeHitbox : MonoBehaviour
{
    [SerializeField] private LayerMask playerMask;
    [SerializeField] private int damage = 1;

    private Transform ownerRoot;
    private Transform onlyTarget; // null이면 누구든 OK
    private readonly HashSet<int> hitRoots = new HashSet<int>();
    private Collider2D col;

    public void Configure(Transform ownerRoot, LayerMask playerMask, int damage, Transform onlyTarget)
    {
        this.ownerRoot = ownerRoot;
        this.playerMask = playerMask;
        this.damage = damage;
        this.onlyTarget = onlyTarget;
    }

    private void Awake()
    {
        col = GetComponent<Collider2D>();
        if (col) col.isTrigger = true;
    }

    private void OnEnable()
    {
        hitRoots.Clear();
        if (!col) return;

        var filter = new ContactFilter2D { useLayerMask = true, layerMask = playerMask, useTriggers = true };
        var buf = new List<Collider2D>(16);
        col.Overlap(filter, buf);
        for (int i = 0; i < buf.Count; i++) TryDamage(buf[i]);
    }

    private void OnTriggerEnter2D(Collider2D other) => TryDamage(other);

    private void TryDamage(Collider2D c)
    {
        if (!c) return;
        if (ownerRoot && c.transform.root == ownerRoot) return;
        if (onlyTarget && c.transform.root != onlyTarget.root) return;
        if (((1 << c.gameObject.layer) & playerMask) == 0) return;

        var root = c.attachedRigidbody ? c.attachedRigidbody.transform.root : c.transform.root;
        if (!root) root = c.transform;

        int id = root.GetInstanceID();
        if (hitRoots.Contains(id)) return;

        var dmgIf = root.GetComponentInChildren<global::IDamageable>() ?? root.GetComponentInParent<global::IDamageable>();
        if (dmgIf != null)
        {
            Vector2 hitPoint = c.bounds.center;
            Vector2 hitNormal = (root.position.x >= transform.position.x) ? Vector2.right : Vector2.left;
            dmgIf.TakeDamage(damage, hitPoint, hitNormal);
        }
        else
        {
            root.SendMessage("TakeDamage", damage, SendMessageOptions.DontRequireReceiver);
            root.SendMessage("OnHit", damage, SendMessageOptions.DontRequireReceiver);
        }

        hitRoots.Add(id);
    }
}
