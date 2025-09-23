using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
[RequireComponent(typeof(Rigidbody2D))]
public class Laser2D : MonoBehaviour
{
    public enum Targeting { P1Only, P2Only, BothPlayers }
    public enum CooldownMode { Shared, PerTarget }

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
    [Tooltip("같은 레이저의 재피해 쿨타임")]
    public float cooldownSeconds = 2f;
    public CooldownMode cooldownMode = CooldownMode.PerTarget;

    [Header("Targeting")]
    public Targeting targeting = Targeting.BothPlayers;

    [Header("Visual (SpriteRenderer)")]
    public Sprite sprite;
    public string sortingLayerName = "Default";
    public int orderInLayer = 100;

    [Tooltip("타겟팅에 따라 자동 색상 적용")]
    public bool autoTintByTarget = true;
    public Color tintP1Only = new Color(0.1f, 0.01f, 0.01f, 1f);   // 회색(=P1)
    public Color tintP2Only = new Color(0.25f, 0.55f, 1f, 1f);      // 파랑(=P2)
    public Color tintBoth = new Color(1f, 0.92f, 0.25f, 1f);      // 노랑(=둘 다)

    // ---- Runtime ----
    private BoxCollider2D box;
    private Rigidbody2D rb2d;
    private GameObject beamGO;
    private SpriteRenderer sr;
    private float currentLength;

    // 쿨다운
    private float _nextDamageTimeShared = 0f;
    private readonly Dictionary<int, float> _perTargetNextDamage = new Dictionary<int, float>();

    // 캐싱(디버그/편의용)
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

        ApplyAutoTint();
        CachePlayers();
        UpdateLaserGeometry(); // 초기화
    }

    void OnValidate()
    {
        if (sr != null) ApplyAutoTint();
        if (box != null)
        {
            box.isTrigger = true;
            box.size = new Vector2(Mathf.Max(0.01f, maxLength), Mathf.Max(0.01f, width));
            box.offset = new Vector2(box.size.x * 0.5f, 0f);
        }
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
        // 레이어/컴포넌트 필터
        bool layerPass = (playerMask.value & (1 << other.gameObject.layer)) != 0;
        var hp1 = other.GetComponentInParent<Player1HP>();
        var hp2 = other.GetComponentInParent<Player2HP>();
        if (!layerPass && hp1 == null && hp2 == null) return;

        // 타겟 규칙 적용
        switch (targeting)
        {
            case Targeting.P1Only:
                if (hp1 != null && hp1.gameObject.activeInHierarchy)
                    TryDealDamage(hp1.gameObject.GetInstanceID(), () => hp1.TakeDamage(damage));
                break;

            case Targeting.P2Only:
                if (hp2 != null && hp2.gameObject.activeInHierarchy)
                    TryDealDamage(hp2.gameObject.GetInstanceID(), () => hp2.TakeDamage(damage));
                break;

            case Targeting.BothPlayers:
                if (hp1 != null && hp1.gameObject.activeInHierarchy)
                    TryDealDamage(hp1.gameObject.GetInstanceID(), () => hp1.TakeDamage(damage));
                if (hp2 != null && hp2.gameObject.activeInHierarchy)
                    TryDealDamage(hp2.gameObject.GetInstanceID(), () => hp2.TakeDamage(damage));
                break;
        }
    }

    bool TryDealDamage(int targetId, System.Action applyDamage)
    {
        float now = Time.time;

        if (cooldownMode == CooldownMode.Shared)
        {
            if (now < _nextDamageTimeShared) return false;
            applyDamage();
            _nextDamageTimeShared = now + Mathf.Max(0f, cooldownSeconds);
            return true;
        }
        else // PerTarget
        {
            if (_perTargetNextDamage.TryGetValue(targetId, out float next) && now < next)
                return false;

            applyDamage();
            _perTargetNextDamage[targetId] = now + Mathf.Max(0f, cooldownSeconds);
            return true;
        }
    }

    void ApplyAutoTint()
    {
        if (!autoTintByTarget || sr == null) return;

        switch (targeting)
        {
            case Targeting.P1Only: sr.color = tintP1Only; break; // 회색
            case Targeting.P2Only: sr.color = tintP2Only; break; // 파랑
            case Targeting.BothPlayers: sr.color = tintBoth; break; // 노랑
        }
    }

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
