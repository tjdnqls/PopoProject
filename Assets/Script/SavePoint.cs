using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class SavePoint : MonoBehaviour
{
    // ---------- Refs ----------
    [Header("Refs")]
    [SerializeField] private Player1HP p1Hp;               // P1 HP
    [SerializeField] private Animator p1Animator;          // P1 Animator(비우면 자동)
    [SerializeField] private SwapController swap;          // 선택/카메라 제어자(비우면 자동)

    // ---------- Trigger Filter ----------
    [Header("Filter")]
    [SerializeField] private LayerMask playerLayers;       // Player/Hitbox 등
    [SerializeField] private bool onlyP2 = true;           // P2만 발동

    // ---------- Activation ----------
    [Header("Activation")]
    [SerializeField] private bool useKeyToActivate = true; // 키로 작동 / 자동 작동
    [SerializeField] private KeyCode interactKey = KeyCode.E;
    [SerializeField] private Vector2 spawnOffset = new(0f, 0.5f);

    // ---------- Camera / UI ----------
    [Header("Camera/UI")]
    [SerializeField] private bool preserveCameraFocusOnRevive = true; // 부활해도 P2 포커스 유지

    // ---------- Visuals & Cooldown ----------
    [Header("Visuals & Cooldown")]
    [SerializeField, Min(0.1f)] private float cooldownSeconds = 3f;
    [SerializeField] private Color activatedColor = Color.green;
    [SerializeField] private Color readyColor = Color.white;
    [SerializeField] private SpriteRenderer sprite;
    [SerializeField] private UnityEngine.UI.Graphic uiGraphic;
    [SerializeField] private Renderer meshRenderer;

    // ---------- Runtime ----------
    private bool _inside;
    private PlayerMouseMovement _insideMover;
    private float _cooldownUntil = 0f;
    private Coroutine _cooldownCo;

    // ---------- Setup ----------
    void Reset()
    {
        var col = GetComponent<Collider2D>();
        col.isTrigger = true;
        if (playerLayers.value == 0)
            playerLayers = LayerMask.GetMask("Player", "PlayerHitbox");
    }

    void Awake()
    {
        if (!p1Hp)
        {
#if UNITY_2023_1_OR_NEWER
            p1Hp = Object.FindFirstObjectByType<Player1HP>(FindObjectsInactive.Exclude)
                ?? Object.FindAnyObjectByType<Player1HP>(FindObjectsInactive.Include);
#else
            p1Hp = FindObjectOfType<Player1HP>();
#endif
        }
        if (!p1Animator && p1Hp) p1Animator = p1Hp.GetComponent<Animator>();

        if (!swap)
        {
#if UNITY_2023_1_OR_NEWER
            swap = Object.FindFirstObjectByType<SwapController>(FindObjectsInactive.Exclude)
                ?? Object.FindAnyObjectByType<SwapController>(FindObjectsInactive.Include);
#else
            swap = FindObjectOfType<SwapController>();
#endif
        }

        if (!sprite) sprite = GetComponentInChildren<SpriteRenderer>(true);
        if (!meshRenderer) meshRenderer = GetComponentInChildren<Renderer>(true);

        SetTint(readyColor);
    }

    // ---------- Trigger Filter ----------
    bool IsP2(Collider2D col, out PlayerMouseMovement mover)
    {
        mover = null;

        // 레이어 필터
        if ((playerLayers.value & (1 << col.gameObject.layer)) == 0) return false;

        // 주인 PlayerMouseMovement 찾기(자식 히트박스 대응)
        var rb2d = col.attachedRigidbody;
        mover = rb2d ? rb2d.GetComponent<PlayerMouseMovement>()
                     : col.GetComponentInParent<PlayerMouseMovement>();
        if (!mover) return false;

        // P2만 허용 옵션
        if (onlyP2 && mover.playerID != SwapController.PlayerChar.P2) return false;

        return true;
    }

    // ---------- Trigger ----------
    void OnTriggerEnter2D(Collider2D other)
    {
        if (!IsP2(other, out var move)) return;
        _inside = true;
        _insideMover = move;
        if (!useKeyToActivate) TryActivate();
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (!IsP2(other, out var move)) return;
        if (move == _insideMover)
        {
            _inside = false;
            _insideMover = null;
        }
    }

    // ---------- Update ----------
    void Update()
    {
        if (!_inside || !useKeyToActivate) return;
        if (Input.GetKeyDown(interactKey)) TryActivate();
    }

    // ---------- Activate ----------
    void TryActivate()
    {
        // 쿨타임
        if (Time.unscaledTime < _cooldownUntil) return;

        // 리스폰 포지션(세이브 포인트 위치 + 오프셋)
        Vector3 spawnPos = transform.position + (Vector3)spawnOffset;

        // P1 이동 컴포넌트에 체크포인트 저장(선택)
        var p1Move = p1Hp ? p1Hp.GetComponent<PlayerMouseMovement>() : null;
        if (p1Move) p1Move.SetCheckpoint(spawnPos);

        // P1이 죽어있다면 부활(카메라/선택은 유지)
        if (p1Hp && p1Hp.IsDead)
        {
            var prevTarget = SwapController.PlayerChar.P2;
            if (preserveCameraFocusOnRevive && swap) prevTarget = swap.charSelect;

            p1Hp.ForceReviveAt(spawnPos);

            // 애니메이션 초기화(죽음/히트 잔상 제거)
            if (p1Animator) AnimatorUtils.ResetToDefaults(p1Animator, resetParams: true);

            if (preserveCameraFocusOnRevive && swap)
                swap.charSelect = prevTarget;
        }

        // 쿨타임 시작 + 색상 페이드
        _cooldownUntil = Time.unscaledTime + cooldownSeconds;
        if (_cooldownCo != null) StopCoroutine(_cooldownCo);
        _cooldownCo = StartCoroutine(CooldownTintRoutine());

        // ★ 체크포인트 저장 + 전체 세이브
        if (SaveManager.Instance == null) SaveManager.Ensure();
        SaveManager.Instance.SaveCheckpointNow(spawnPos);
    }

    // ---------- Visual Cooldown ----------
    IEnumerator CooldownTintRoutine()
    {
        SetTint(activatedColor);  // 즉시 초록
        SoundManager.Play("SaveSound", transform);
        float t = 0f;
        while (t < cooldownSeconds)
        {
            float a = Mathf.Clamp01(t / Mathf.Max(0.0001f, cooldownSeconds));
            SetTint(Color.Lerp(activatedColor, readyColor, a)); // 초록→하양
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        SetTint(readyColor);
        _cooldownCo = null;
    }

    // ---------- Helpers ----------
    void SetTint(Color c)
    {
        if (sprite) sprite.color = c;
        if (uiGraphic) uiGraphic.color = c;
        if (meshRenderer)
        {
            var mat = meshRenderer.material; // 단일 머티리얼 가정
            if (mat && mat.HasProperty("_Color")) mat.color = c;
        }
    }
}
