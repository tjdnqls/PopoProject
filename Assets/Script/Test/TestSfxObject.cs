// TestSfxObject.cs
using UnityEngine;

[AddComponentMenu("Audio/Test SFX Object (with fallback)")]
public class TestSfxObject : MonoBehaviour
{
    [Header("SoundManager에서 재생할 사운드 이름")]
    public string soundName = "Test";

    [Header("직접 재생(백업) 옵션")]
    [Tooltip("SoundManager 경로가 조용하면 아래 클립을 AudioSource로 직접 재생")]
    public bool alsoPlayDirectly = false;
    public AudioClip directClip;            // 여기에 아무 WAV/MP3 넣어둬도 됨
    public float directVolume = 1f;
    public bool direct2D = true;            // 2D로 강제
    public float directMaxLife = 5f;        // 안전 제거 시간

    [Header("편의")]
    public bool playOnStart = true;
    public KeyCode hotkey = KeyCode.T;

    void Start()
    {
        if (playOnStart) PlayNow();
    }

    void Update()
    {
        if (Input.GetKeyDown(hotkey)) PlayNow();
    }

    [ContextMenu("Play Now (OneShot)")]
    public void PlayNow()
    {
        // 1) SoundManager 경로
        if (SoundManager.Instance != null && !string.IsNullOrEmpty(soundName))
        {
            SoundManager.Play(soundName, transform);
            Debug.Log($"[TestSfxObject] SoundManager.Play('{soundName}') 요청");
        }
        else
        {
            Debug.LogWarning("[TestSfxObject] SoundManager.Instance가 없거나 soundName이 비어 있음");
        }

        // 2) 필요하면 바로 직접 재생(백업)
        if (alsoPlayDirectly && directClip != null)
        {
            var go = new GameObject("TestSfxObject_Direct");
            go.transform.position = transform.position;

            var src = go.AddComponent<AudioSource>();
            src.spatialBlend = direct2D ? 0f : 1f;
            src.ignoreListenerPause = true;
            src.volume = directVolume;

            src.PlayOneShot(directClip, 1f);

            float life = Mathf.Min(directMaxLife, Mathf.Max(0.2f, directClip.length + 0.5f));
            if (Application.isPlaying) Destroy(go, life);
            else DestroyImmediate(go);

            Debug.Log("[TestSfxObject] Direct Play(백업) 실행");
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
        Gizmos.DrawSphere(transform.position, 0.08f);
    }
#endif
}
