//using UnityEngine;

//public class WarriorHit : MonoBehaviour
//{
//    [SerializeField] private float damage = 10f;
//    [SerializeField] private LayerMask playerMask;
//    private WarriorAI owner;

//    public void Init(WarriorAI o, float dmg, LayerMask pm)
//    {
//        owner = o;
//        damage = dmg;
//        playerMask = pm;
//    }

//    private void OnTriggerEnter2D(Collider2D other)
//    {
//        if (!enabled || !gameObject.activeInHierarchy) return;
//        if (((1 << other.gameObject.layer) & playerMask) == 0) return;

//        // 자기 자신과의 충돌 무시
//        if (owner && other.transform.root == owner.transform.root) return;

//        ApplyDamage(other);
//    }

//    private void OnCollisionEnter2D(Collision2D other)
//    {
//        if (((1 << other.gameObject.layer) & playerMask) == 0) return;
//        if (owner && other.transform.root == owner.transform.root) return;
//        ApplyDamage(other.collider);
//    }

//    private void ApplyDamage(Collider2D col)
//    {
//        // IDamageable 우선
//        var dmg = col.GetComponentInParent<IDamageable>();
//        if (dmg != null)
//        {
//            dmg.TakeDamage(damage);
//            return;
//        }
//        // 없으면 SendMessage 백업
//        col.SendMessage("TakeDamage", damage, SendMessageOptions.DontRequireReceiver);
//    }
//}
