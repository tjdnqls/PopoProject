using UnityEngine;
using System.Collections;

[DisallowMultipleComponent]
public class ColorFadeRespawn : MonoBehaviour
{
    [Header("Timing")]
    public float fadeDuration = 1.5f;
    public float respawnDelay = 3f;

    [Header("Colors")]
    public Color targetColor = Color.red;
    public bool keepOriginalAlpha = false; // true면 알파는 유지하고 색만 변환

    [Header("Hit Detection")]
    public string[] triggerTags = new[] { "Player", "Bullet" };
    public LayerMask triggerLayers = ~0;   // 레이어 추가 필터(필요 없으면 그대로 두세요)
    public bool acceptTriggerColliders = true;

    private SpriteRenderer sr;
    private Collider2D col;
    private Color originalColor;
    private bool isBreaking;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>() ?? GetComponentInChildren<SpriteRenderer>(true);
        col = GetComponent<Collider2D>();
        if (sr) originalColor = sr.color;
    }

    void OnValidate()
    {
        if (!sr) sr = GetComponent<SpriteRenderer>() ?? GetComponentInChildren<SpriteRenderer>(true);
        if (!col) col = GetComponent<Collider2D>();
    }

    bool PassesFilters(Collider2D other)
    {
        if (((1 << other.gameObject.layer) & triggerLayers) == 0) return false;
        if (triggerTags != null && triggerTags.Length > 0)
        {
            for (int i = 0; i < triggerTags.Length; i++)
                if (other.CompareTag(triggerTags[i])) return true;
            return false;
        }
        return true;
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (!isBreaking && PassesFilters(collision.collider))
        {
            isBreaking = true; // 중복 방지 락을 선행
            StartCoroutine(FadeAndRespawn());
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!acceptTriggerColliders) return;
        if (!isBreaking && PassesFilters(other))
        {
            isBreaking = true; // 중복 방지 락을 선행
            StartCoroutine(FadeAndRespawn());
        }
    }

    IEnumerator FadeAndRespawn()
    {
        if (!sr || !col) yield break;

        float timer = 0f;
        Color start = sr.color;
        Color end = targetColor;
        end.a = keepOriginalAlpha ? start.a : 0f;

        while (timer < fadeDuration)
        {
            float t = Mathf.Clamp01(timer / Mathf.Max(0.0001f, fadeDuration));
            var c = Color.Lerp(start, end, t);
            if (!keepOriginalAlpha) c.a = 1f - t; // 색 변화 + 페이드아웃 동시
            sr.color = c;
            timer += Time.deltaTime;
            yield return null;
        }

        var final = end;
        if (!keepOriginalAlpha) final.a = 0f;
        sr.color = final;

        col.enabled = false;

        yield return new WaitForSeconds(respawnDelay);

        sr.color = originalColor;
        col.enabled = true;
        isBreaking = false;
    }
}
