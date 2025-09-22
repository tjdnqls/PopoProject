using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
[RequireComponent(typeof(Rigidbody2D))]
public class Laser2D : MonoBehaviour
{
    [Header("Laser Shape")]
    [Min(0.05f)] public float maxLength = 8f;       // 로컬 +X 최대 길이
    [Min(0.01f)] public float width = 0.15f;        // 시각/판정 두께
    [Tooltip("자기 자신을 피하려 레이 시작점을 앞으로 오프셋")]
    [Min(0f)] public float startSkin = 0.02f;       // 권장: width*0.5f + 0.01f

    [Header("Masks")]
    [Tooltip("플레이어 제외 장애물 (여기서 Laser 자신의 레이어는 반드시 제외!)")]
    public LayerMask obstacleMask;                  // 예: Everything - Player - Laser
    [Tooltip("플레이어(또는 히트박스) 레이어")]
    public LayerMask playerMask;

    [Header("Damage")]
    public int damage = 1;
    public float cooldownSeconds = 2f;              // 같은 레이저 재피해 쿨타임(공유)

    [Header("Visual (SpriteRenderer)")]
    public Sprite sprite;
    public string sortingLayerName = "Default";
    public int orderInLayer = 100;

    // ---- Runtime ----
    private BoxCollider2D box;
    private Rigidbody2D rb2d;
    private GameObject beamGO;
    private SpriteRenderer sr;
    private float currentLength;
    private float _nextDamageTime = 0f;

    private Player1HP p1;
    private Player2HP p2;

    private static readonly RaycastHit2D[] _hits = new RaycastHit2D[8];

    void Reset()
    {
        var col = GetComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = new Vector2(1f, 0.15f);
        col.offset = new Vector2(0.5f, 0f);

        var rb = GetComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.simulated = true;
        rb.gravityScale = 0f;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
    }

    void Awake()
    {
        box = GetComponent<BoxCollider2D>();
        rb2d = GetComponent<Rigidbody2D>();
        rb2d.bodyType = RigidbodyType2D.Kinematic;
        rb2d.simulated = true;
        rb2d.gravityScale = 0f;
        rb2d.constraints = RigidbodyConstraints2D.FreezeRotation;
        box.isTrigger = true;

        // 시각용 자식
        beamGO = new GameObject("BeamSprite");
        beamGO.transform.SetParent(transform, false);
        sr = beamGO.AddComponent<SpriteRenderer>();
        sr.drawMode = SpriteDrawMode.Tiled;
        sr.sortingLayerName = sortingLayerName;
        sr.sortingOrder = orderInLayer;
        sr.sprite = sprite != null ? sprite : MakeFallbackWhiteSprite();

        CachePlayers();
        UpdateLaserGeometry(); // 초기화
    }

    void FixedUpdate() => UpdateLaserGeometry();

    void UpdateLaserGeometry()
    {
        Vector2 dir = (Vector2)transform.right;             // 로컬 +X
        float len = Mathf.Max(0.01f, maxLength);

        // 자기 자신 피하기 위한 시작점 보정
        float skin = Mathf.Max(startSkin, (width * 0.5f) + 0.01f);
        Vector2 origin = (Vector2)transform.position + dir * skin;
        float castDist = Mathf.Max(0.001f, len - skin);

        var filter = new ContactFilter2D { useLayerMask = true, layerMask = obstacleMask, useTriggers = true };
        int count = Physics2D.Raycast(origin, dir, filter, _hits, castDist);

        float hitDist = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            var h = _hits[i];
            if (!h.collider) continue;
            if (h.collider == box) continue;                 // 자기 자신 무시
            hitDist = h.distance;                            // 첫 유효 히트
            break;
        }

        if (!float.IsInfinity(hitDist))
            len = skin + Mathf.Min(castDist, hitDist);

        currentLength = len;

        // 콜라이더/스프라이트 길이 동기화 (로컬 +X로 뻗음)
        box.size = new Vector2(len, width);
        box.offset = new Vector2(len * 0.5f, 0f);

        sr.size = new Vector2(len, width);
        sr.transform.localPosition = new Vector3(len * 0.5f, 0f, 0f);
    }

    void CachePlayers()
    {
#if UNITY_2023_1_OR_NEWER
        if (!p1) p1 = Object.FindFirstObjectByType<Player1HP>(FindObjectsInactive.Include);
        if (!p2) p2 = Object.FindFirstObjectByType<Player2HP>(FindObjectsInactive.Include);
#else
        if (!p1) p1 = FindObjectOfType<Player1HP>(true);
        if (!p2) p2 = FindObjectOfType<Player2HP>(true);
#endif
    }

    void OnTriggerEnter2D(Collider2D other) { TryHit(other); }
    void OnTriggerStay2D(Collider2D other) { TryHit(other); }

    void TryHit(Collider2D other)
    {
        if (Time.time < _nextDamageTime) return;

        // 마스크/컴포넌트 폴백 중 하나라도 맞으면 처리
        bool layerPass = (playerMask.value & (1 << other.gameObject.layer)) != 0;
        bool compPass = other.GetComponentInParent<Player1HP>() != null
                      || other.GetComponentInParent<Player2HP>() != null
                      || other.GetComponentInParent<global::IDamageable>() != null;
        if (!layerPass && !compPass) return;

        CachePlayers();

        bool any = false;
        // 요구사항: 한 명이 닿아도 P1/P2 **각자** 1 데미지
        if (p1 && p1.gameObject.activeInHierarchy) { DealDamageLikeYourCode(p1.transform, damage); any = true; }
        if (p2 && p2.gameObject.activeInHierarchy) { DealDamageLikeYourCode(p2.transform, damage); any = true; }

        if (any) _nextDamageTime = Time.time + Mathf.Max(0f, cooldownSeconds);
    }

    // ==== 여기서 "주인님이 준 패턴" 그대로 적용 ====
    void DealDamageLikeYourCode(Transform t, int dmg)
    {
        if (!t) return;

        var dmgIf = t.GetComponentInParent<global::IDamageable>();
        if (dmgIf != null)
        {
            // hitPoint/Normal: 레이저 중심과 진행방향으로 생성
            Vector2 hitPoint = box ? (Vector2)box.bounds.center : (Vector2)transform.position;
            Vector2 hitNormal = (Vector2)transform.right; // +X 방향
            dmgIf.TakeDamage(dmg, hitPoint, hitNormal);
            return;
        }

        var p2hp = t.GetComponentInParent<Player2HP>();
        if (p2hp != null) { p2hp.TakeDamage(dmg); return; }

        t.SendMessageUpwards("TakeDamage", dmg, SendMessageOptions.DontRequireReceiver);
        t.SendMessageUpwards("OnHit", dmg, SendMessageOptions.DontRequireReceiver);
    }
    // ===============================================

    // 스프라이트 없을 때 안전장치용 1x1 흰색
    Sprite MakeFallbackWhiteSprite()
    {
        var t = new Texture2D(1, 1, TextureFormat.ARGB32, false, true);
        t.SetPixel(0, 0, Color.white);
        t.filterMode = FilterMode.Bilinear;
        t.wrapMode = TextureWrapMode.Repeat;
        t.Apply(false, false);
        return Sprite.Create(t, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Vector3 o = transform.position;
        Vector3 d = transform.right;
        float len = Application.isPlaying ? currentLength : maxLength;
        Gizmos.DrawLine(o, o + d * len);
        Gizmos.DrawWireCube(o + d * (len * 0.5f), new Vector3(len, width, 0.01f));
    }
}
