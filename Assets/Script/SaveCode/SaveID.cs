using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[ExecuteAlways]
[DisallowMultipleComponent]
public class SaveID : MonoBehaviour
{
    [SerializeField] private string id;
    public string Id => id;

    // 같은 씬 내에서만 중복 감지(로그 전용)
    private static readonly HashSet<string> usedInScene = new();

    // ★ 씬이 바뀔 때마다 중복 캐시 초기화(도메인 리로드 꺼도 동작)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void HookScene()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }
    private static void OnSceneLoaded(Scene s, LoadSceneMode m) => usedInScene.Clear();

#if UNITY_EDITOR
    // 에디터에서만 비어있으면 GUID 부여(영구 저장). 런타임에는 절대 변경 X
    void OnValidate()
    {
        if (!Application.isPlaying && string.IsNullOrEmpty(id))
        {
            id = Guid.NewGuid().ToString("N");
            UnityEditor.EditorUtility.SetDirty(this);
        }
    }

    [ContextMenu("Regenerate ID (Editor Only)")]
    void RegenerateInEditor()
    {
        id = Guid.NewGuid().ToString("N");
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif

    void Awake()
    {
        // 런타임에 비어있으면 마지막 안전장치(가능하면 에디터에서 생성돼 있어야 함)
        if (string.IsNullOrEmpty(id))
        {
#if UNITY_EDITOR
            Debug.LogWarning("[SaveID] Empty ID at runtime. Generated once, but please save the scene.", this);
#endif
            id = Guid.NewGuid().ToString("N");
        }

        // 같은 씬에서 실수로 중복이면 '로그만' 띄우고 절대 바꾸지 않음 (매칭 파괴 금지)
        if (!usedInScene.Add(id))
        {
#if UNITY_EDITOR
            Debug.LogWarning($"[SaveID] Duplicate ID in scene: {id}. Fix in editor to avoid ambiguous loads.", this);
#endif
        }
    }
}
