// ===================== Player2HP.cs (Safe Animator + Wipe Reference + Suicide Hotkey) =====================
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class Player2HP : MonoBehaviour, global::IDamageable
{
    [Header("HP")]
    [SerializeField] private int maxHP = 1;
    public int CurrentHP { get; private set; }
    public bool IsDead { get; private set; }

    [Header("Anim (optional)")]
    [SerializeField] private Animator rb2;                 // Inspector로 할당 가능
    [SerializeField] private string deathBoolName = "death";

    [Header("Game Over Wipe (optional)")]
    [Tooltip("씬에 배치한 SpiralBoxWipe를 참조하세요. 비워두면 존재하는 Instance를 쓰고, 없으면 자동 생성합니다.")]
    [SerializeField] private SpiralBoxWipe wipeRef;

    // ---- 테스트용 자살 커맨드 ----
    [Header("Suicide Hotkey (Test)")]
    [Tooltip("테스트용 자살 커맨드 활성화")]
    [SerializeField] private bool enableSuicideHotkey = true;
    [Tooltip("자살 키 (기본 K)")]
    [SerializeField] private KeyCode suicideKey = KeyCode.K;
    [Tooltip("Shift를 함께 눌러야만 발동하게 할지")]
    [SerializeField] private bool requireShift = false;
    [Tooltip("이 시간(초) 동안 키를 누르고 있으면 발동. 0이면 탭 즉시 발동")]
    [SerializeField, Min(0f)] private float suicideHoldSeconds = 0f;
    [Tooltip("에디터/디벨롭먼트 빌드에서만 동작하도록 제한")]
    [SerializeField] private bool onlyInEditorOrDevelopment = true;

    private float _holdTimer = 0f;
    private bool _sceneReloading = false;

    void OnValidate()
    {
        if (maxHP < 1) maxHP = 1;
        AutoWireAnimator();
        AutoWireWipeRef();
    }

    void Reset()
    {
        AutoWireAnimator();
        AutoWireWipeRef();
    }

    void Awake()
    {
        CurrentHP = maxHP;
        AutoWireAnimator();
        AutoWireWipeRef();
    }

    void OnEnable() { _holdTimer = 0f; }

    private void AutoWireAnimator()
    {
        if (rb2 != null) return;
        rb2 = GetComponent<Animator>();
        if (rb2 == null)
            rb2 = GetComponentInChildren<Animator>(true); // 루트에 없고 자식에 있을 때 커버
    }

    private void AutoWireWipeRef()
    {
        if (wipeRef != null) return;

#if UNITY_2023_1_OR_NEWER
        wipeRef = FindFirstObjectByType<SpiralBoxWipe>(FindObjectsInactive.Include);
#else
        wipeRef = FindObjectOfType<SpiralBoxWipe>(includeInactive: true);
#endif

        if (wipeRef == null && SpiralBoxWipe.Instance != null)
            wipeRef = SpiralBoxWipe.Instance;
    }

    // ===== Suicide Hotkey 체크 =====
    void Update()
    {
        if (!enableSuicideHotkey || IsDead) return;

        // 빌드 안전장치
        bool buildOk = !onlyInEditorOrDevelopment || Application.isEditor || Debug.isDebugBuild;
        if (!buildOk) return;

        // Shift 조합 요구 시 체크
        bool modOk = !requireShift || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (suicideHoldSeconds <= 0f)
        {
            // 탭 즉시 발동
            if (modOk && Input.GetKeyDown(suicideKey))
                KillNow();
        }
        else
        {
            // 홀드 방식
            if (modOk && Input.GetKey(suicideKey))
            {
                _holdTimer += Time.unscaledDeltaTime;
                if (_holdTimer >= suicideHoldSeconds)
                    KillNow();
            }
            else
            {
                if (Input.GetKeyUp(suicideKey) || !modOk)
                    _holdTimer = 0f;
            }
        }
    }

    [ContextMenu("Suicide (Test)")]
    public void KillNow()
    {
        if (IsDead) return;
        CurrentHP = 0;
        Die();
    }

    // ====== 표준 인터페이스(3파라미터) ======
    void global::IDamageable.TakeDamage(int dmg, Vector2 hitPoint, Vector2 hitNormal)
    {
        TakeDamage(dmg);
    }

    // 단순 버전
    public void TakeDamage(int dmg = 1)
    {
        if (IsDead) return;
        int amount = Mathf.Max(1, dmg);
        CurrentHP = Mathf.Max(0, CurrentHP - amount);
        if (CurrentHP == 0) Die();
    }

    public void Heal(int amount)
    {
        if (IsDead || amount <= 0) return;
        CurrentHP = Mathf.Min(maxHP, CurrentHP + amount);
    }

    private void Die()
    {
        if (IsDead) return;
        IsDead = true;

        // Animator가 있으면 파라미터 세팅(있으면 쓰고, 없어도 그냥 진행)
        if (rb2 != null)
        {
            try { rb2.SetBool(deathBoolName, true); }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Player2HP] Animator param set skipped: {e.Message}");
            }
        }

        if (_sceneReloading) return;
        _sceneReloading = true;

        // ---- Wipe를 "레퍼런스 우선"으로 확보 ----
        EnsureWipeReady();

        // 씬 리로드 연출 실행(Instance 사용 → 인스펙터 튜닝값 반영)
        SpiralBoxWipe.RunActiveScene();
    }

    /// <summary>
    /// 1) 인스펙터로 지정된 wipeRef가 있으면 그걸 활성화하여 사용
    /// 2) 없으면 기존 SpiralBoxWipe.Instance 사용
    /// 3) 그래도 없으면 새로 생성
    /// </summary>
    private void EnsureWipeReady()
    {
        // 1) 인스펙터 참조 우선
        if (wipeRef != null)
        {
            if (!wipeRef.gameObject.activeInHierarchy)
                wipeRef.gameObject.SetActive(true);
            // SpiralBoxWipe는 Awake에서 자신을 Instance로 등록함
            return;
        }

        // 2) 이미 존재하는 싱글톤이 있으면 그대로 사용
        if (SpiralBoxWipe.Instance != null)
            return;

        // 3) 아무 것도 없으면 자동 생성(런타임용 기본값)
        var go = new GameObject("YouDiedWipe");
        wipeRef = go.AddComponent<SpiralBoxWipe>(); // Awake에서 Instance 설정 + DontDestroyOnLoad
    }
}
