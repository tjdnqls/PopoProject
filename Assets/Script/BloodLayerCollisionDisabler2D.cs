// BloodLayerCollisionDisabler2D.cs
using UnityEngine;

/// <summary>
/// 씬 시작 시 Physics2D 레이어 충돌 매트릭스에서
/// Blood 레이어와의 충돌을 전부 끊어줍니다(옵션으로 원복 가능).
/// - 기본: Blood <-> 모든 레이어 충돌 무시 (Blood끼리도 무시)
/// - 특정 레이어만 선택해 무시할 수도 있음
/// </summary>
[DisallowMultipleComponent]
public class BloodLayerCollisionDisabler2D : MonoBehaviour
{
    [Header("Layer Names")]
    [SerializeField] private string bloodLayerName = "Blood";
    [SerializeField] private string[] allowedLayerNames = { "Default", "Ground" };

    [Header("Mode")]
    [Tooltip("true면 Blood와 모든 레이어의 충돌을 전부 끊습니다.")]
    [SerializeField] private bool ignoreWithAllLayers = false;

    [Tooltip("ignoreWithAllLayers=false일 때만 사용: 이 배열의 레이어들과 Blood 충돌을 끊습니다.")]
    [SerializeField] private string[] onlyTheseLayerNames = new string[0];



    [Tooltip("Blood 레이어끼리의 충돌도 끊을지 여부")]
    [SerializeField] private bool alsoIgnoreBloodWithBlood = true;

    [Header("Lifecycle")]
    [Tooltip("Disable/Destroy 시 원래 충돌 설정으로 되돌립니다.")]
    [SerializeField] private bool revertOnDisable = false;

    private int bloodLayer = -1;
    private bool[] prevStates = new bool[32]; // 이전 Ignore 상태 저장용
    private bool capturedPrev = false;

    private void Awake()
    {
        bloodLayer = LayerMask.NameToLayer(bloodLayerName);
        if (bloodLayer < 0)
        {
            Debug.LogWarning($"[BloodLayerCollisionDisabler2D] Layer '{bloodLayerName}' not found.");
            enabled = false;
            return;
        }

        ApplyIgnores();
    }

    private void Start()
    {
        int bloodLayer = LayerMask.NameToLayer("Blood");
        int playerLayer = LayerMask.NameToLayer("Player");

        if (bloodLayer >= 0 && playerLayer >= 0)
        {
            Physics2D.IgnoreLayerCollision(bloodLayer, playerLayer, true);
        }
    }

    private void OnEnable()
    {
        // 재활성화 시에도 한번 더 보정(씬 리로드/스크립트 토글 대비)
        if (bloodLayer >= 0) ApplyIgnores();
    }

    private void OnDisable()
    {
        if (revertOnDisable && bloodLayer >= 0)
            RevertIgnores();
    }

    private void OnDestroy()
    {
        if (revertOnDisable && bloodLayer >= 0)
            RevertIgnores();
    }

    private void ApplyIgnores()
    {
        // 이전 상태 스냅샷(한 번만 저장)
        if (!capturedPrev)
        {
            for (int l = 0; l < 32; l++)
                prevStates[l] = Physics2D.GetIgnoreLayerCollision(bloodLayer, l);
            capturedPrev = true;
        }

        if (ignoreWithAllLayers)
        {
            for (int l = 0; l < 32; l++)
            {
                if (l == bloodLayer && !alsoIgnoreBloodWithBlood) continue;
                Physics2D.IgnoreLayerCollision(bloodLayer, l, true);
            }
        }
        else
        {
            // 지정된 레이어들과만 무시
            foreach (var name in onlyTheseLayerNames)
            {
                if (string.IsNullOrEmpty(name)) continue;
                int l = LayerMask.NameToLayer(name);
                if (l >= 0) Physics2D.IgnoreLayerCollision(bloodLayer, l, true);
                else Debug.LogWarning($"[BloodLayerCollisionDisabler2D] Layer '{name}' not found.");
            }
            // 옵션: Blood끼리도 무시
            if (alsoIgnoreBloodWithBlood)
                Physics2D.IgnoreLayerCollision(bloodLayer, bloodLayer, true);
        }
    }
    private void RevertIgnores()
    {
        if (!capturedPrev) return;

        for (int l = 0; l < 32; l++)
            Physics2D.IgnoreLayerCollision(bloodLayer, l, prevStates[l]);

#if UNITY_EDITOR
        Debug.Log($"[BloodLayerCollisionDisabler2D] Reverted ignore matrix for '{bloodLayerName}'.", this);
#endif
    }
}
