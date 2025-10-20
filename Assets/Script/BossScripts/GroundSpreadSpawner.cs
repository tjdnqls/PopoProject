using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class GroundSpreadSpawner : MonoBehaviour
{
    [Header("Target / Trigger")]
    [SerializeField] private Transform origin;                 // 기준 오브젝트(없으면 this)
    [SerializeField] private KeyCode triggerKey = KeyCode.Keypad7;

    [Header("Ground (Layer Names, comma-separated)")]
    [Tooltip("예: \"Ground\" 또는 \"Ground, EventGround, OneWayGround\"")]
    [SerializeField] private string groundLayerNames = "Ground";

    [Header("Placement")]
    [SerializeField] private bool includeCenter = true;        // 중심 지점도 배치
    [Min(0.1f)][SerializeField] private float spacing = 2f;   // 간격(가로, center-to-center)
    [SerializeField] private Vector2 boxSize = new Vector2(2f, 2f);
    [Min(1)][SerializeField] private int maxStepsPerSide = 20;
    [SerializeField] private float scanTopOffset = 10f;        // 위에서 아래로 레이 시작 높이
    [SerializeField] private float scanDownDistance = 30f;     // 아래로 레이 길이

    [Header("Lifetimes")]
    [SerializeField] private float redBoxLifetime = 2f;
    [SerializeField] private float fireLifetime = 2f;

    [Header("Prefabs (optional)")]
    [Tooltip("미지정 시 런타임 사각 SpriteRenderer로 대체")]
    [SerializeField] private GameObject redBoxPrefab;
    [Tooltip("미지정 시 주황색 사각 SpriteRenderer로 대체")]
    [SerializeField] private GameObject firePrefab;

    [Header("Sorting")]
    [SerializeField] private int redBoxSortingOrder = 20;
    [SerializeField] private int fireSortingOrder = 21;

    [Header("Visuals")]
    [Range(0f, 1f)]
    [SerializeField] private float redBoxAlpha = 0.45f; // 경고 박스 투명도(기본 절반)

    public enum FireSizeMode { MatchBoxSize, MatchSpacingWidth }
    [Header("Fire Size")]
    [SerializeField] private FireSizeMode fireSizeMode = FireSizeMode.MatchBoxSize;
    [Tooltip("최종 크기에 곱해지는 패딩 스케일(1=정확히 맞춤, 0.9=10% 작게)")]
    [Range(0.5f, 1.5f)]
    [SerializeField] private float fireFitPadding = 1.0f;

    [Header("Fire Spawn Offset")]
    [Tooltip("불 이펙트의 초기 스폰 위치를 세로(Y)로 올리거나 내립니다. +면 위로.")]
    [SerializeField] private float fireSpawnYOffset = 0.15f;

    // runtime
    private bool _busy;
    private LayerMask _groundMask;

    // pooled runtime sprite (fallback)
    private static Sprite _unitSquareSprite; // 1x1
    private static Sprite GetUnitSquareSprite()
    {
        if (_unitSquareSprite != null) return _unitSquareSprite;
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.SetPixels(new Color[] { Color.white, Color.white, Color.white, Color.white });
        tex.Apply();
        _unitSquareSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                                          new Vector2(0.5f, 0.5f), 1f);
        return _unitSquareSprite;
    }

    void Awake()
    {
        if (!origin) origin = transform;
        _groundMask = NamesToMask(groundLayerNames);
    }

    void OnValidate()
    {
        if (spacing < 0.1f) spacing = 0.1f;
        if (boxSize.x < 0.1f) boxSize.x = 0.1f;
        if (boxSize.y < 0.1f) boxSize.y = 0.1f;
        if (fireFitPadding < 0.5f) fireFitPadding = 0.5f;
    }

    void Update()
    {
        if (Input.GetKeyDown(triggerKey))
        {
            TryRunSequence();
        }
    }

    public void TryRunSequence()
    {
        if (_busy) return;
        StartCoroutine(RunSequence());
    }

    private IEnumerator RunSequence()
    {
        _busy = true;

        // 1) 배치 좌표 수집(중심에서 좌우로 스캔, '지면' 히트 시에만 배치)
        var positions = CollectPositions();

        // 2) 빨간 박스 생성
        var redBoxes = SpawnMany(positions, isFire: false);

        // 3) redBoxLifetime 후 박스 제거 + 같은 위치에 불 생성
        yield return new WaitForSeconds(redBoxLifetime);
        DestroyMany(redBoxes);
        var fires = SpawnMany(positions, isFire: true);

        // 4) fireLifetime 후 불 제거
        yield return new WaitForSeconds(fireLifetime);
        DestroyMany(fires);

        _busy = false;
    }

    private List<Vector3> CollectPositions()
    {
        var result = new List<Vector3>(maxStepsPerSide * 2 + 1);
        var originX = origin.position.x;

        if (includeCenter)
        {
            if (TrySampleGround(originX, out var centerPos))
                result.Add(centerPos);
        }

        for (int step = 1; step <= maxStepsPerSide; step++)
        {
            float dx = spacing * step;

            // 오른쪽
            if (TrySampleGround(originX + dx, out var posR))
                result.Add(posR);
            else
                break;

            // 왼쪽
            if (TrySampleGround(originX - dx, out var posL))
                result.Add(posL);
            else
                break;
        }

        return result;
    }

    private bool TrySampleGround(float sampleX, out Vector3 worldCenter)
    {
        Vector2 start = new Vector2(sampleX, origin.position.y + scanTopOffset);
        Vector2 dir = Vector2.down;

        var hit = Physics2D.Raycast(start, dir, scanDownDistance, _groundMask);
        if (hit.collider != null)
        {
            float centerY = hit.point.y + boxSize.y * 0.5f;
            worldCenter = new Vector3(sampleX, centerY, 0f);
            return true;
        }

        worldCenter = default;
        return false;
    }

    private List<GameObject> SpawnMany(List<Vector3> centers, bool isFire)
    {
        var list = new List<GameObject>(centers.Count);
        foreach (var c in centers)
        {
            var go = SpawnOne(c, isFire);
            if (go) list.Add(go);
        }
        return list;
    }

    private GameObject SpawnOne(Vector3 center, bool isFire)
    {
        // 불은 스폰 시 Y 오프셋 적용
        Vector3 spawnPos = center;
        if (isFire) spawnPos.y += fireSpawnYOffset;

        GameObject prefab = isFire ? firePrefab : redBoxPrefab;
        GameObject go;

        if (prefab != null)
        {
            go = Instantiate(prefab, spawnPos, Quaternion.identity);
            go.transform.localScale = Vector3.one;

            // 프리팁이어도 경고 박스면 알파 보정
            if (!isFire)
                SetAlphaRecursive(go, redBoxAlpha);
        }
        else
        {
            // 기본 사각 SpriteRenderer 생성
            go = new GameObject(isFire ? "Fire(Generated)" : "RedBox(Generated)");
            go.layer = LayerMask.NameToLayer("Ignore Raycast");

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = GetUnitSquareSprite();
            sr.color = isFire ? new Color(1f, 0.5f, 0.15f, 1f)
                              : new Color(1f, 0f, 0f, redBoxAlpha);
            sr.sortingOrder = isFire ? fireSortingOrder : redBoxSortingOrder;

            go.transform.position = spawnPos;
        }

        // 위치(프리팹일 때도 보정)
        go.transform.position = spawnPos;

        // 크기 맞추기
        if (isFire)
        {
            Vector2 target = GetFireTargetSize();
            FitToWorldSize(go, target, fireSortingOrder);
        }
        else
        {
            FitToWorldSize(go, boxSize, redBoxSortingOrder);
        }

        return go;
    }

    private Vector2 GetFireTargetSize()
    {
        // 불 크기를 "박스 크기"에 맞출지, "간격 너비"에 맞출지 선택
        Vector2 target;
        switch (fireSizeMode)
        {
            case FireSizeMode.MatchSpacingWidth:
                target = new Vector2(spacing, boxSize.y);
                break;
            default:
                target = boxSize;
                break;
        }
        return target * fireFitPadding;
    }

    private void FitToWorldSize(GameObject go, Vector2 desiredSize, int sortingOrderIfSingle)
    {
        // SpriteRenderer들의 월드 바운즈를 수집해 현재 크기를 얻고,
        // 루트 localScale을 축별 비율로 조정하여 정확히 desiredSize에 맞춤
        if (!TryGetWorldBounds(go, out var b))
        {
            // SR이 없으면 루트 스케일로 맞추기(대체)
            go.transform.localScale = new Vector3(desiredSize.x, desiredSize.y, 1f);
            return;
        }

        float curW = Mathf.Max(0.0001f, b.size.x);
        float curH = Mathf.Max(0.0001f, b.size.y);

        var tf = go.transform;
        Vector3 baseScale = tf.localScale;
        float sx = (desiredSize.x / curW) * baseScale.x;
        float sy = (desiredSize.y / curH) * baseScale.y;
        tf.localScale = new Vector3(sx, sy, baseScale.z);

        // 선택적으로 루트에 단일 SR이 있다면 정렬순서 보정
        if (go.TryGetComponent<SpriteRenderer>(out var rootSr))
            rootSr.sortingOrder = sortingOrderIfSingle;
    }

    private bool TryGetWorldBounds(GameObject go, out Bounds worldBounds)
    {
        var srs = go.GetComponentsInChildren<SpriteRenderer>(true);
        if (srs == null || srs.Length == 0)
        {
            worldBounds = default;
            return false;
        }
        worldBounds = srs[0].bounds;
        for (int i = 1; i < srs.Length; i++)
            worldBounds.Encapsulate(srs[i].bounds);
        return true;
    }

    private void DestroyMany(List<GameObject> objs)
    {
        if (objs == null) return;
        for (int i = 0; i < objs.Count; i++)
        {
            if (objs[i]) Destroy(objs[i]);
        }
        objs.Clear();
    }

    private static void SetAlphaRecursive(GameObject go, float alpha)
    {
        if (!go) return;
        var srs = go.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < srs.Length; i++)
        {
            var c = srs[i].color;
            c.a = alpha;
            srs[i].color = c;
        }
    }

    private static LayerMask NamesToMask(string namesCsv)
    {
        if (string.IsNullOrWhiteSpace(namesCsv)) return 0;
        int mask = 0;
        var parts = namesCsv.Split(',');
        foreach (var p in parts)
        {
            var name = p.Trim();
            if (string.IsNullOrEmpty(name)) continue;
            int layer = LayerMask.NameToLayer(name);
            if (layer >= 0) mask |= (1 << layer);
        }
        return mask;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!origin) origin = transform;
        Gizmos.color = new Color(1f, 0f, 0f, 0.25f);

        // 미리보기 라인(스캔 레이)
        float ox = origin.position.x;
        Vector3 top = new Vector3(ox, origin.position.y + scanTopOffset, 0f);
        Vector3 bot = top + Vector3.down * scanDownDistance;
        Gizmos.DrawLine(top, bot);

        // 샘플 포인트 미리보기
        int steps = Mathf.Max(1, maxStepsPerSide);
        for (int step = includeCenter ? 0 : 1; step <= steps; step++)
        {
            float dx = spacing * step;
            Gizmos.DrawLine(new Vector3(ox + dx, top.y, 0f), new Vector3(ox + dx, top.y - scanDownDistance, 0f));
            Gizmos.DrawLine(new Vector3(ox - dx, top.y, 0f), new Vector3(ox - dx, top.y - scanDownDistance, 0f));
        }
    }
#endif
}
