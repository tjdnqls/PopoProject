using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class SpiralBoxWipe : MonoBehaviour
{
    public static SpiralBoxWipe Instance { get; private set; }
    public static bool IsBusy { get; private set; }

    private SmartCameraFollowByWall swap;

    [Header("Grid")]
    [SerializeField] private int rows = 12;
    [SerializeField] private int cols = 20;
    [SerializeField] private float fillInterval = 0.001f;   // 채우기 기준(최소) 간격
    [SerializeField] private float unfillInterval = 0.0001f; // 비우기 기준(최소) 간격
    [SerializeField] private Color boxColor = Color.black;

    [Header("Spiral Start")]
    [Tooltip("나선을 화면 밖에서 시작할 바깥 패딩(블록 단위). 1이면 한 블록 바깥에서 시작")]
    [SerializeField] private int outsideStartPadding = 1;

    [Header("Pixel Snapping")]
    [Tooltip("서브픽셀 정렬 이슈 최소화용. 해상도/배율 따라 1로 두는 것을 권장")]
    [SerializeField] private bool pixelPerfect = true;

    [Header("Timing")]
    [SerializeField] private float delayBeforeUnfill = 1.0f; // 씬 로드 후 비우기 시작까지 대기(초)

    [Header("Speed Ramp")]
    [Tooltip("채우기 진행 말미에 기준 딜레이에 곱해질 최대 배수(클수록 더 느려짐)")]
    [SerializeField] private float fillSlowdownMultiplier = 1f;
    [Tooltip("비우기 시작 시 기준 딜레이에 곱해질 초기 배수(클수록 더 느리게 시작)")]
    [SerializeField] private float unfillSpeedupMultiplier = 30f;

    Canvas _canvas;
    RectTransform _grid;
    GridLayoutGroup _gridLayout;
    Image[,] _tiles;

    // 스파이럴 경로들
    List<Vector2Int> _spiralInsideTL;       // 내부(0..rows-1, 0..cols-1), 좌상 시작
    List<Vector2Int> _spiralInsideBR;       // 내부, 우하 시작(좌상 미러)
    List<Vector2Int> _spiralPadTL;          // 패딩 포함, 좌상 시작
    List<Vector2Int> _spiralPadBR;          // 패딩 포함, 우하 시작(좌상 미러)

    Vector2Int _lastScreenSize = new Vector2Int(-1, -1);

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        BuildCanvasAndGrid();
        BuildTiles();
        BuildSpiralOrders();
        HideAllTiles();

        gameObject.name = "SpiralBoxWipe(DontDestroy)";
    }

    void Start()
    {
        StartCoroutine(InitAfterFirstLayout());
    }

    IEnumerator InitAfterFirstLayout()
    {
        yield return null;
        Canvas.ForceUpdateCanvases();
        RecomputeGridCellSize();
        HideAllTiles();
    }

    void Update()
    {
        if (Screen.width != _lastScreenSize.x || Screen.height != _lastScreenSize.y)
        {
            _lastScreenSize = new Vector2Int(Screen.width, Screen.height);
            RecomputeGridCellSize();
        }
    }

    void OnRectTransformDimensionsChange()
    {
        if (_grid != null) RecomputeGridCellSize();
    }

    void BuildCanvasAndGrid()
    {
        var cgo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        cgo.transform.SetParent(transform, false);
        _canvas = cgo.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 32760;
        _canvas.pixelPerfect = pixelPerfect;

        var scaler = cgo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var gridGO = new GameObject("Grid", typeof(RectTransform), typeof(GridLayoutGroup));
        gridGO.transform.SetParent(_canvas.transform, false);
        _grid = gridGO.GetComponent<RectTransform>();
        _grid.anchorMin = Vector2.zero; _grid.anchorMax = Vector2.one;
        _grid.pivot = new Vector2(0.5f, 0.5f);
        _grid.offsetMin = Vector2.zero; _grid.offsetMax = Vector2.zero;

        _gridLayout = gridGO.GetComponent<GridLayoutGroup>();
        _gridLayout.spacing = Vector2.zero;
        _gridLayout.padding = new RectOffset(0, 0, 0, 0);
        _gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        _gridLayout.constraintCount = cols;
        _gridLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;
        _gridLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
        _gridLayout.childAlignment = TextAnchor.UpperLeft;
    }

    void BuildTiles()
    {
        _tiles = new Image[rows, cols];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var cell = new GameObject($"T_{r}_{c}", typeof(RectTransform), typeof(Image));
                cell.transform.SetParent(_grid, false);
                var img = cell.GetComponent<Image>();
                img.color = boxColor;
                img.raycastTarget = false;
                _tiles[r, c] = img;
            }
    }

    void BuildSpiralOrders()
    {
        // 내부: 좌상 시작(기준)
        _spiralInsideTL = BuildSpiralOrder(rows, cols, 0, 0);
        // 내부: 우하 시작(좌상 미러)
        _spiralInsideBR = BuildSpiralOrderBottomRight(rows, cols, 0, 0);

        // 패딩 포함: 좌상/우하
        int p = Mathf.Max(1, outsideStartPadding);
        _spiralPadTL = BuildSpiralOrder(rows + 2 * p, cols + 2 * p, -p, -p);
        _spiralPadBR = BuildSpiralOrderBottomRight(rows + 2 * p, cols + 2 * p, -p, -p);
    }

    // 기본: 좌상 시작, 시계방향(외곽→내부)
    List<Vector2Int> BuildSpiralOrder(int rCount, int cCount, int offsetR, int offsetC)
    {
        var list = new List<Vector2Int>(rCount * cCount);
        int top = 0, bottom = rCount - 1, left = 0, right = cCount - 1;

        while (left <= right && top <= bottom)
        {
            for (int c = left; c <= right; c++) list.Add(new Vector2Int(top + offsetR, c + offsetC));
            top++;
            for (int r = top; r <= bottom; r++) list.Add(new Vector2Int(r + offsetR, right + offsetC));
            right--;
            if (top <= bottom)
            {
                for (int c = right; c >= left; c--) list.Add(new Vector2Int(bottom + offsetR, c + offsetC));
                bottom--;
            }
            if (left <= right)
            {
                for (int r = bottom; r >= top; r--) list.Add(new Vector2Int(r + offsetR, left + offsetC));
                left++;
            }
        }
        return list;
    }

    // 좌상 시작 스파이럴을 '우하 시작'으로 미러링(시계방향 유지)
    List<Vector2Int> BuildSpiralOrderBottomRight(int rCount, int cCount, int offsetR, int offsetC)
    {
        var baseOrder = BuildSpiralOrder(rCount, cCount, 0, 0);
        var list = new List<Vector2Int>(baseOrder.Count);
        for (int i = 0; i < baseOrder.Count; i++)
        {
            int rr = (rCount - 1 - baseOrder[i].x) + offsetR;
            int cc = (cCount - 1 - baseOrder[i].y) + offsetC;
            list.Add(new Vector2Int(rr, cc));
        }
        return list;
    }

    void RecomputeGridCellSize()
    {
        if (_gridLayout == null || _grid == null) return;

        Canvas.ForceUpdateCanvases();

        var r = _grid.rect;
        float w = r.width / Mathf.Max(1, cols);
        float h = r.height / Mathf.Max(1, rows);

        // 서브픽셀 오차 방지: 올림
        _gridLayout.cellSize = new Vector2(Mathf.Ceil(w), Mathf.Ceil(h));

        LayoutRebuilder.ForceRebuildLayoutImmediate(_grid);
    }

    static bool InBounds(int r, int c, int rows, int cols)
        => (uint)r < (uint)rows && (uint)c < (uint)cols;

    void HideAllTiles()
    {
        if (_tiles == null) return;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                _tiles[r, c].enabled = false;
    }

    // t in [0,1] → t^2 (부드러운 가속/감속)
    static float EaseInQuad(float t) { return t * t; }

    IEnumerator FillClockwiseFromEdges()
    {
        IsBusy = true;
        if (swap != null) swap.swapsup = false;

        var A = _spiralPadTL;
        var B = _spiralPadBR;
        int count = Mathf.Max(A.Count, B.Count);

        for (int i = 0; i < count; i++)
        {
            if (i < A.Count)
            {
                var p = A[i];
                if (InBounds(p.x, p.y, rows, cols))
                    _tiles[p.x, p.y].enabled = true;
            }
            if (i < B.Count)
            {
                var q = B[i];
                if (InBounds(q.x, q.y, rows, cols))
                    _tiles[q.x, q.y].enabled = true;
            }

            if (fillInterval > 0f)
            {
                float t = (count <= 1) ? 1f : (float)i / (count - 1);
                float factor = Mathf.Lerp(1f, Mathf.Max(1f, fillSlowdownMultiplier), EaseInQuad(t));
                float delay = fillInterval * factor;
                yield return new WaitForSecondsRealtime(delay);
            }
        }
    }

    IEnumerator UnfillFromInsideCounterClockwise()
    {
        var A = _spiralInsideTL;
        var B = _spiralInsideBR;
        int count = Mathf.Max(A.Count, B.Count);

        for (int i = 0; i < count; i++)
        {
            int ia = A.Count - 1 - i;
            if (ia >= 0)
            {
                var p = A[ia];
                if (InBounds(p.x, p.y, rows, cols))
                    _tiles[p.x, p.y].enabled = false;
            }

            int ib = B.Count - 1 - i;
            if (ib >= 0)
            {
                var q = B[ib];
                if (InBounds(q.x, q.y, rows, cols))
                    _tiles[q.x, q.y].enabled = false;
            }

            if (unfillInterval > 0f)
            {
                float t = (count <= 1) ? 1f : (float)i / (count - 1);
                float factor = Mathf.Lerp(Mathf.Max(1f, unfillSpeedupMultiplier), 1f, EaseInQuad(t));
                float delay = unfillInterval * factor;
                yield return new WaitForSecondsRealtime(delay);
            }
        }

        IsBusy = false;
    }

    public static void Run(string sceneName)
    {
        Ensure();
        Instance.StopAllCoroutines();
        Instance.StartCoroutine(Instance.ReloadSceneRoutine(sceneName));
    }

    public static void Ensure()
    {
        if (Instance) return;
        var go = new GameObject("SpiralBoxWipe");
        go.AddComponent<SpiralBoxWipe>();
    }

    IEnumerator ReloadSceneRoutine(string sceneName)
    {
        // 1) 채우기 — 두 나선 동시(좌상/우하, 시계방향), 패딩 포함
        yield return StartCoroutine(FillClockwiseFromEdges());

        // 2) 씬 로드
        var op = SceneManager.LoadSceneAsync(sceneName);
        while (!op.isDone) yield return null;

        // 3) 로드 완료 후 대기
        if (delayBeforeUnfill > 0f)
            yield return new WaitForSecondsRealtime(delayBeforeUnfill);

        // 4) 비우기 — 두 나선 동시(내부→외부, 역순)
        yield return StartCoroutine(UnfillFromInsideCounterClockwise());
    }
}
