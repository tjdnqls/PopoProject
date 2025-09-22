using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance { get; private set; }

    // --- Gate: 의도된 리로드 때만 로드 ---
    private static bool _loadOnNextScene = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { _loadOnNextScene = false; }

    /// <summary>다음 씬 로드시 로드를 허용(무장)합니다. (씬 로드 전에 호출 필수)</summary>
    public static void RequestLoadOnNextScene() => _loadOnNextScene = true;

    /// <summary>현재 씬을 저장 후 리로드 + 복원까지 한 번에 처리하는 편의 함수</summary>
    public static void ReloadActiveScene(bool saveBefore = true)
    {
        Ensure();
        if (saveBefore) Instance.SaveNow();
        _loadOnNextScene = true;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // ---------- Serialization ----------
    [Serializable] private class Record { public string id; public string type; public string json; }
    [Serializable] private class CheckpointData { public bool has; public string sceneName; public Vector2 spawnPos; }
    [Serializable]
    private class SceneSave
    {
        public string sceneName;
        public List<Record> records = new();
        public CheckpointData checkpoint = new();
    }

    // ---------- Paths / Cache ----------
    private string SavePath => Path.Combine(Application.persistentDataPath, "save_autosave.json");
    private SceneSave cached;
    private CheckpointData lastCheckpoint = new();

    [Header("Load Settings")]
    [SerializeField, Min(0.1f)] private float restoreWindowSeconds = 1.0f; // 씬 로드 후 이 시간 동안 계속 복원 시도

    // ---------- Lifecycle ----------
    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        if (Instance == this) SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    public static void Ensure()
    {
        if (!Instance) new GameObject("SaveManager").AddComponent<SaveManager>();
    }

    // ---------- Public API ----------
    public void SaveNow()
    {
        var scene = SceneManager.GetActiveScene().name;
        var data = new SceneSave { sceneName = scene };

        var all = GetAllMonoBehavioursIncludeInactive();
        foreach (var mb in all)
        {
            if (!(mb is ISaveable saveable)) continue;
            if (!mb.TryGetComponent<SaveID>(out var sid)) continue;

            var state = saveable.CaptureState();
            if (state == null) continue;

            data.records.Add(new Record
            {
                id = sid.Id,
                type = state.GetType().AssemblyQualifiedName,
                json = JsonUtility.ToJson(state)
            });
        }

        // 체크포인트 포함
        if (lastCheckpoint != null && lastCheckpoint.has && lastCheckpoint.sceneName == scene)
        {
            data.checkpoint = new CheckpointData { has = true, sceneName = scene, spawnPos = lastCheckpoint.spawnPos };
        }

        try
        {
            File.WriteAllText(SavePath, JsonUtility.ToJson(data));
#if UNITY_EDITOR
            Debug.Log($"[SaveManager] Saved {data.records.Count} records → {SavePath}");
#endif
            cached = data;
        }
        catch (Exception e) { Debug.LogError($"[SaveManager] Save failed: {e}"); }
    }

    public void SaveCheckpointNow(Vector2 pos)
    {
        lastCheckpoint.has = true;
        lastCheckpoint.sceneName = SceneManager.GetActiveScene().name;
        lastCheckpoint.spawnPos = pos;
        SaveNow();
    }

    public void Clear()
    {
        try { if (File.Exists(SavePath)) File.Delete(SavePath); } catch (Exception e) { Debug.LogError(e); }
        cached = null;
        lastCheckpoint = new CheckpointData();
    }

    // ---------- Scene Load ----------
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // ★ 플레이 시작 첫 씬 포함, 모든 자동 로드를 차단
        if (!_loadOnNextScene) return;

        // 의도된 리로드인 경우에만 복원
        _loadOnNextScene = false;
        StartCoroutine(RestoreAfterLoad(scene.name));
    }

    private System.Collections.IEnumerator RestoreAfterLoad(string sceneName)
    {
        yield return null; // 1프레임 대기 (생성 타이밍 보정)
        LoadForScene(sceneName);
    }

    public void LoadForScene(string sceneName)
    {
        // 캐시/파일에서 데이터 읽기
        SceneSave data = null;
        if (cached != null && cached.sceneName == sceneName) data = cached;

        if (data == null && File.Exists(SavePath))
        {
            try
            {
                data = JsonUtility.FromJson<SceneSave>(File.ReadAllText(SavePath));
#if UNITY_EDITOR
                Debug.Log($"[SaveManager] Loaded file with {data.records.Count} records for scene {data.sceneName}");
#endif
            }
            catch (Exception e) { Debug.LogError($"[SaveManager] Load failed: {e}"); }
        }

        if (data == null || data.sceneName != sceneName) return;

        var records = new Dictionary<string, Record>(data.records.Count);
        foreach (var r in data.records) records[r.id] = r;

        StartCoroutine(CoApplyRestoresOverWindow(records, data.checkpoint));
    }

    // ---------- Restore Window ----------
    private System.Collections.IEnumerator CoApplyRestoresOverWindow(
        Dictionary<string, Record> records, CheckpointData cp)
    {
        var applied = new HashSet<string>();
        float end = Time.unscaledTime + Mathf.Max(0.1f, restoreWindowSeconds);

        while (Time.unscaledTime < end && applied.Count < records.Count)
        {
            var all = GetAllMonoBehavioursIncludeInactive();
            foreach (var mb in all)
            {
                if (!(mb is ISaveable saveable)) continue;
                if (!mb.TryGetComponent<SaveID>(out var sid)) continue;
                if (applied.Contains(sid.Id)) continue;
                if (!records.TryGetValue(sid.Id, out var rec)) continue;

                var type = ResolveType(rec.type);
                object stateObj = null;

                if (type != null) stateObj = JsonUtility.FromJson(rec.json, type);
                else
                {
                    var probe = saveable.CaptureState();
                    var fallback = probe?.GetType();
                    if (fallback != null) stateObj = JsonUtility.FromJson(rec.json, fallback);
                }

                if (stateObj == null) continue;

                saveable.RestoreState(stateObj);
                applied.Add(sid.Id);
            }
            yield return null;
        }

#if UNITY_EDITOR
        Debug.Log($"[SaveManager] Restored {applied.Count}/{records.Count} objects.");
#endif

        if (cp != null && cp.has && cp.sceneName == SceneManager.GetActiveScene().name)
            StartCoroutine(ApplyCheckpointAfterLoad(cp.spawnPos));
    }

    // ---------- Helpers ----------
    private static System.Type ResolveType(string asmQualifiedName)
    {
        var t = System.Type.GetType(asmQualifiedName, false);
        if (t != null) return t;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            t = asm.GetType(asmQualifiedName, false)
                ?? asm.GetType(asmQualifiedName.Split(',')[0], false);
            if (t != null) return t;
        }
        return null;
    }

    private static MonoBehaviour[] GetAllMonoBehavioursIncludeInactive()
    {
#if UNITY_2023_1_OR_NEWER
        return UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        return UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true);
#endif
    }

    private System.Collections.IEnumerator ApplyCheckpointAfterLoad(Vector2 pos)
    {
        yield return null; // 생성 타이밍 보정
        var players = UnityEngine.Object.FindObjectsByType<PlayerMouseMovement>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (var p in players)
        {
            var rb = p.GetComponent<Rigidbody2D>();
            if (rb != null) { rb.position = pos; rb.linearVelocity = Vector2.zero; }
            else p.transform.position = pos;
        }
    }
}
