using UnityEngine;
using System.Collections;

public class ColorFadeRespawn : MonoBehaviour
{
    public float fadeDuration = 1.5f;
    public float respawnDelay = 3f;
    public Color targetColor = Color.red;

    private SpriteRenderer sr;
    private Collider2D col;
    private Color originalColor;
    private bool isBreaking = false;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        col = GetComponent<Collider2D>();
        originalColor = sr.color;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!isBreaking && (collision.collider.CompareTag("Player")|| collision.collider.CompareTag("Bullet")))
        {
            StartCoroutine(FadeAndRespawn());
        }
    }

    private IEnumerator FadeAndRespawn()
    {
        isBreaking = true;
        float timer = 0f;

        while (timer < fadeDuration)
        {
            float t = timer / fadeDuration;
            sr.color = Color.Lerp(originalColor, targetColor, t);
            sr.color = new Color(sr.color.r, sr.color.g, sr.color.b, 1f - t); // 투명도 낮추기
            timer += Time.deltaTime;
            yield return null;
        }

        sr.color = new Color(targetColor.r, targetColor.g, targetColor.b, 0f);
        col.enabled = false;

        yield return new WaitForSeconds(respawnDelay);

        sr.color = originalColor;
        col.enabled = true;
        isBreaking = false;
    }
}
