using UnityEngine;
using UnityEngine.Tilemaps;

[RequireComponent(typeof(TilemapRenderer))]
[DisallowMultipleComponent]
public class DestructibleTilemapMask : MonoBehaviour
{
    [Header("Mask Texture (pixels)")]
    [SerializeField] private int texWidth = 1024;
    [SerializeField] private int texHeight = 512;
    [SerializeField] private float boundsPadding = 0.5f; // 월드 경계 여유

    [Header("Shader/Material")]
    [SerializeField] private string shaderName = "2D/AlphaCutMasked"; // 아래 셰이더 이름과 동일
    [SerializeField, Range(0, 1)] private float cutoff = 0.5f;

    private TilemapRenderer tr;
    private Texture2D maskTex;
    private Color32[] pixels;
    private Rect worldRect; // 마스크의 월드 영역
    private Material mat;

    void Awake()
    {
        tr = GetComponent<TilemapRenderer>();

        // 월드 경계 계산
        var b = tr.bounds;
        worldRect = new Rect(
            b.min.x - boundsPadding,
            b.min.y - boundsPadding,
            b.size.x + boundsPadding * 2f,
            b.size.y + boundsPadding * 2f
        );

        // 마스크 텍스처 준비(검정=유지, 흰색=구멍)
        maskTex = new Texture2D(texWidth, texHeight, TextureFormat.R8, false);
        maskTex.filterMode = FilterMode.Point;
        maskTex.wrapMode = TextureWrapMode.Clamp;

        pixels = new Color32[texWidth * texHeight];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(0, 0, 0, 255);
        maskTex.SetPixels32(pixels);
        maskTex.Apply(false, false);

        // 머티리얼 적용
        var shader = Shader.Find(shaderName);
        mat = new Material(shader);
        mat.SetTexture("_MaskTex", maskTex);
        mat.SetFloat("_Cutoff", cutoff);

        // 월드→마스크 UV 변환 파라미터 세팅
        // UV = (world.xy - min) / size
        Vector4 worldToMask = new Vector4(
            1f / worldRect.width, 1f / worldRect.height,
            -worldRect.xMin / worldRect.width, -worldRect.yMin / worldRect.height
        );
        mat.SetVector("_WorldToMask", worldToMask);

        tr.material = mat; // 인스턴스 머티리얼
    }

    // 원형으로 지우기(월드 기준 반지름)
    public void PaintHole(Vector2 worldPos, float radiusWorld)
    {
        int cx, cy, rPx;
        if (!WorldToPixel(worldPos, out cx, out cy)) return;
        rPx = Mathf.RoundToInt(radiusWorld * texWidth / worldRect.width);

        int x0 = Mathf.Max(0, cx - rPx);
        int x1 = Mathf.Min(texWidth - 1, cx + rPx);
        int y0 = Mathf.Max(0, cy - rPx);
        int y1 = Mathf.Min(texHeight - 1, cy + rPx);

        int rr = rPx * rPx;
        for (int y = y0; y <= y1; y++)
        {
            int dy = y - cy;
            int dy2 = dy * dy;
            int row = y * texWidth;
            for (int x = x0; x <= x1; x++)
            {
                int dx = x - cx;
                if (dx * dx + dy2 <= rr)
                {
                    int idx = row + x;
                    pixels[idx] = new Color32(255, 0, 0, 255); // 흰색=구멍
                }
            }
        }
        maskTex.SetPixels32(pixels);
        maskTex.Apply(false, false);
    }

    bool WorldToPixel(Vector2 world, out int px, out int py)
    {
        float u = (world.x - worldRect.xMin) / worldRect.width;
        float v = (world.y - worldRect.yMin) / worldRect.height;
        px = Mathf.RoundToInt(u * (texWidth - 1));
        py = Mathf.RoundToInt(v * (texHeight - 1));
        return (u >= 0f && u <= 1f && v >= 0f && v <= 1f);
    }
}
