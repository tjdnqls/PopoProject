using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// - 이 스크립트를 가진 오브젝트가 SavePoint 레이어 오브젝트와 접촉하면 해당 위치를 저장한다.
/// - 씬 로드 시 마지막으로 저장된 위치로 플레이어(또는 Player1 프리팹)를 배치/소환한다.
/// - 저장 데이터는 PlayerPrefs로 영구 저장된다.
/// </summary>
[DisallowMultipleComponent]
public class SavePointer : MonoBehaviour
{
    [Header("SavePoint Layer")]
    [Tooltip("세이브 포인트가 속한 레이어 이름 (기본: SavePoint)")]
    [SerializeField] private string savePointLayerName = "SavePoint";
    private int _savePointLayer = -1;

    [Header("Spawn / Player")]
    [Tooltip("씬 로드 시 저장지점에 플레이어를 이동/소환할지 여부")]
    [SerializeField] private bool spawnOnSceneLoaded = true;

    [Tooltip("플레이어 태그(존재 여부 판단용). 비어있으면 'Player1'을 사용")]
    [SerializeField] private string playerTag = "Player1";

    [Tooltip("플레이어가 없을 때 소환할 Player1 프리팹")]
    [SerializeField] private GameObject player1Prefab;

    [Tooltip("플레이어를 못 찾으면 'Player1' 이름으로도 찾아본다")]
    [SerializeField] private bool alsoFindByName = true;

    [Header("Advanced")]
    [Tooltip("세이브 포인트 콜라이더의 위치 대신, SavePointAnchor 컴포넌트가 있으면 그 위치를 저장/사용")]
    [SerializeField] private bool preferAnchor = true;

    // PlayerPrefs 키
    private const string K_ACTIVE = "SAVE_ACTIVE";
    private const string K_SCENE = "SAVE_SCENE";
    private const string K_X = "SAVE_X";
    private const string K_Y = "SAVE_Y";
    private const string K_Z = "SAVE_Z";

    private void Awake()
    {
        _savePointLayer = LayerMask.NameToLayer(savePointLayerName);
        if (_savePointLayer < 0)
            Debug.LogWarning($"[SavePointer] Layer '{savePointLayerName}' 를 찾을 수 없습니다. 인스펙터에서 레이어명을 확인하세요.", this);
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // --- Trigger/Collision로 세이브 포인트 접촉 감지 ---
    private void OnTriggerEnter2D(Collider2D other) { TrySaveFromCollider(other); }
    private void OnCollisionEnter2D(Collision2D col) { TrySaveFromCollider(col.collider); }

    private void TrySaveFromCollider(Collider2D col)
    {
        if (!col) return;
        // 레이어 일치 확인
        if (_savePointLayer >= 0 && col.gameObject.layer != _savePointLayer) return;

        Vector3 savePos = col.transform.position;
        if (preferAnchor && col.GetComponentInParent<SavePointAnchor>(true) is SavePointAnchor anchor && anchor.spawnPoint)
            savePos = anchor.spawnPoint.position;

        SaveToPrefs(SceneManager.GetActiveScene().name, savePos);
#if UNITY_EDITOR
        Debug.Log($"[SavePointer] Saved at {savePos} (scene='{SceneManager.GetActiveScene().name}')", col);
#endif
    }

    // --- 씬 로드 시 부활 처리 ---
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!spawnOnSceneLoaded) return;

        if (!HasSave()) return; // 저장 없음
        string savedScene = PlayerPrefs.GetString(K_SCENE, "");
        Vector3 savedPos = new Vector3(
            PlayerPrefs.GetFloat(K_X, transform.position.x),
            PlayerPrefs.GetFloat(K_Y, transform.position.y),
            PlayerPrefs.GetFloat(K_Z, transform.position.z)
        );

        // "현재 로드된 씬"이 마지막 저장된 씬과 같을 때만 위치를 적용
        if (!string.IsNullOrEmpty(savedScene) && savedScene == scene.name)
        {
            var player = FindPlayerObject();
            if (player != null)
            {
                // 이미 플레이어가 있다 → 위치만 이동
                player.transform.position = savedPos;
            }
            else
            {
                // 플레이어가 없다 → 프리팹으로 소환
                if (player1Prefab != null)
                {
                    var spawned = Instantiate(player1Prefab, savedPos, Quaternion.identity);
                    // 태그가 비어있고 프리팹에 태그가 없다면 태그 세팅
                    TryAssignTag(spawned);
                }
                else
                {
                    Debug.LogWarning("[SavePointer] Player1 프리팹이 지정되지 않아 소환할 수 없습니다.", this);
                }
            }
        }
        // 씬이 다르면 아무것도 하지 않는다(요구사항: 해당 씬 로드시 마지막 세이브에서 부활)
    }

    // --- 저장/로드 유틸 ---
    private static bool HasSave() => PlayerPrefs.GetInt(K_ACTIVE, 0) == 1;

    private static void SaveToPrefs(string sceneName, Vector3 pos)
    {
        PlayerPrefs.SetInt(K_ACTIVE, 1);
        PlayerPrefs.SetString(K_SCENE, sceneName);
        PlayerPrefs.SetFloat(K_X, pos.x);
        PlayerPrefs.SetFloat(K_Y, pos.y);
        PlayerPrefs.SetFloat(K_Z, pos.z);
        PlayerPrefs.Save();
    }

    public static void ClearSave()
    {
        PlayerPrefs.DeleteKey(K_ACTIVE);
        PlayerPrefs.DeleteKey(K_SCENE);
        PlayerPrefs.DeleteKey(K_X);
        PlayerPrefs.DeleteKey(K_Y);
        PlayerPrefs.DeleteKey(K_Z);
        PlayerPrefs.Save();
    }

    // --- Player 탐색/소환 보조 ---
    private GameObject FindPlayerObject()
    {
        GameObject player = null;

        // 1) 태그 우선
        if (!string.IsNullOrEmpty(playerTag))
            player = GameObject.FindWithTag(playerTag);

        // 2) 이름 보조
        if (player == null && alsoFindByName)
        {
            var go = GameObject.Find("Player1");
            if (go) player = go;
        }

        return player;
    }

    private void TryAssignTag(GameObject go)
    {
        if (!go) return;
        if (string.IsNullOrEmpty(playerTag)) return;
        try
        {
            // 프리팹/오브젝트에 아직 태그가 없다면 부여 (유효한 태그여야 함)
            if (go.CompareTag("Untagged"))
                go.tag = playerTag;
        }
        catch { /* 유효하지 않은 태그면 무시 */ }
    }

    // 디버그용 컨텍스트 메뉴
    [ContextMenu("Force Save Here (Current Scene)")]
    private void Editor_ForceSaveHere()
    {
        SaveToPrefs(SceneManager.GetActiveScene().name, transform.position);
        Debug.Log($"[SavePointer] Force saved at {transform.position}");
    }

    [ContextMenu("Clear Save")]
    private void Editor_ClearSave()
    {
        ClearSave();
        Debug.Log("[SavePointer] Save cleared");
    }
}

/// <summary>
/// (선택) 세이브 포인트에 붙여서 스폰 위치를 지정하고 싶을 때 사용.
/// SavePointer가 preferAnchor=true일 때, 이 컴포넌트를 우선 사용한다.
/// </summary>
public class SavePointAnchor : MonoBehaviour
{
    public Transform spawnPoint;
}
