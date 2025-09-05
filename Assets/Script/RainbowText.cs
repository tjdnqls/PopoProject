using UnityEngine;
using TMPro; // TextMeshPro 사용 시
using UnityEngine.UI; // 일반 Text 사용 시는 주석 해제

public class RainbowText : MonoBehaviour
{
    public float colorSpeed = 1f; // 속도 조절용
    public bool useTextMeshPro = true;

    private TextMeshProUGUI tmpText;
    private Text uiText;
    private float t;

    void Start()
    {
        if (useTextMeshPro)
            tmpText = GetComponent<TextMeshProUGUI>();
        else
            uiText = GetComponent<Text>();
    }

    void Update()
    {
        t += Time.deltaTime * colorSpeed;
        float r = Mathf.Sin(t + 0f) * 0.5f + 0.5f;
        float g = Mathf.Sin(t + 2f) * 0.5f + 0.5f;
        float b = Mathf.Sin(t + 4f) * 0.5f + 0.5f;
        Color rainbow = new Color(r, g, b, 1f);

        if (useTextMeshPro && tmpText != null)
            tmpText.color = rainbow;
        else if (uiText != null)
            uiText.color = rainbow;
    }
}
