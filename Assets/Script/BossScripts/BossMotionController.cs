using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteAnimationManager))]
public class BossMotionController : MonoBehaviour
{
    [Header("Anim Names")]
    [SerializeField] private string idleName = "idle";
    [SerializeField] private string moveName = "Move";

    [Header("Input")]
    [SerializeField] private KeyCode triggerKey = KeyCode.Keypad7;

    [Header("Behavior")]
    [Tooltip("게임 시작 시 Idle을 강제로 시작합니다.")]
    [SerializeField] private bool playIdleOnStart = true;

    private SpriteAnimationManager _anim;

    void Awake()
    {
        _anim = GetComponent<SpriteAnimationManager>();
        // 원샷 중에는 다른 Play 명령 무시하도록(안전)
        _anim.respectOneShots = true;
    }

    void Start()
    {
        if (playIdleOnStart && _anim.HasClip(idleName))
            _anim.Play(idleName, forceRestart: true);
    }

    void Update()
    {
        // 숫자패드 7 트리거
        if (Input.GetKeyDown(triggerKey))
            TryPlayMoveOnce();
    }

    public void TryPlayMoveOnce()
    {
        if (_anim == null) return;

        // 현재 원샷(1회 재생) 진행 중이면 무시해 중복 재시작을 방지
        if (_anim.IsOneShotActive) return;

        // Move 1회 재생 후 Idle로 자동 복귀
        if (_anim.HasClip(moveName))
            _anim.PlayOnce(moveName, fallback: idleName, forceRestart: true);
    }

    // 필요 시 외부 이벤트(패턴 AI 등)에서 직접 호출 가능
    public void ForceIdle()
    {
        if (_anim == null) return;
        if (_anim.HasClip(idleName))
            _anim.Play(idleName, forceRestart: true, interruptOneShot: true);
    }
}
