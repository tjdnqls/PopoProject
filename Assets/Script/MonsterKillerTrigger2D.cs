using UnityEngine;

[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(Rigidbody2D))]
public class MonsterKillerTrigger2D : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private string monsterLayerName = "Monster"; // 감지 대상 레이어
    [SerializeField] private float killDelay = 3.0f;              // 감지 후 제거 딜레이(초)
    [SerializeField] private bool targetRootWithRigidbody = true; // Rigidbody 루트로 판정할지
    [Header("Animation")]
    public SpriteAnimationManager anim; // Idle / Run / AttackStart / Attack / Hit / Death
    private int monsterLayer = -1;
    private Rigidbody2D rb;
    private Collider2D col;

    void Awake()
    {
        monsterLayer = LayerMask.NameToLayer(monsterLayerName);

        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();

        // 트리거 세팅(권장)
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        col.isTrigger = true;

        if (monsterLayer < 0)
            Debug.LogWarning($"[MonsterKillerTrigger2D] Layer '{monsterLayerName}' not found.");
    }
    private void PlayAnim(string key, bool forceRestart = false)
    {
        if (anim == null || string.IsNullOrEmpty(key)) return;
        if (anim.IsOneShotActive) return;         // ★ 1회 재생 보호
        anim.Play(key, forceRestart);
    }
    private void PlayOnce(string key, string fallback = null, bool forceRestart = true)
    {
        if (anim == null || string.IsNullOrEmpty(key)) return;
        anim.PlayOnce(key, fallback, forceRestart);
    }
    void OnEnable()
    {
        rb?.WakeUp();
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (monsterLayer < 0) return;

        // Rigidbody 루트를 대상으로 할지 여부
        GameObject hit = (targetRootWithRigidbody && other.attachedRigidbody)
            ? other.attachedRigidbody.gameObject
            : other.gameObject;

        if (hit.layer != monsterLayer) return;
        PlayOnce("Hit", "Death");
        // 중복 스케줄 방지용: DelayedDestroy가 있으면 기존 예약 유지
        var dd = hit.GetComponent<DelayedDestroy>();
        if (!dd) dd = hit.gameObject.AddComponent<DelayedDestroy>();
        dd.Schedule(killDelay);
    }
}
