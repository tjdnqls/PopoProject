using System.Collections;
using System.Linq;
using UnityEngine;

[DisallowMultipleComponent]
public class AutoEffect2D : MonoBehaviour
{
    public enum SourceType { AutoDetect, SpriteAnimationManager, Animator, ParticleSystem, Manual }

    [Header("Source")]
    [Tooltip("재생 소스 타입. AutoDetect면 우선순위: SpriteAnimationManager > Animator > ParticleSystem")]
    public SourceType sourceType = SourceType.AutoDetect;

    [Tooltip("SpriteAnimationManager(선택). 없으면 무시")]
    public Component spriteAnimationManager; // 타입 이름 고정 피하려고 Component로 받음(프로젝트마다 다름)
    [Tooltip("Animator(선택). 없으면 무시")]
    public Animator animatorRef;

    [Tooltip("이펙트에 포함된 ParticleSystem들(선택). 값이 비어있으면 GetComponentsInChildren로 자동 수집")]
    public ParticleSystem[] particles;

    [Header("Lifetime (끝나면 자동 파괴)")]
    [Tooltip("수명 계산 우선순위: ManualDuration> (SAM fps/frames) > Animator Clip > ParticleSystem IsAlive")]
    public float manualDuration = -1f;

    [Tooltip("SpriteAnimationManager를 쓰는데 길이를 계산할 수 없을 때 사용할 FPS")]
    public float fallbackFps = 12f;

    [Tooltip("SpriteAnimationManager를 쓰는데 길이를 계산할 수 없을 때 사용할 총 프레임 수")]
    public int fallbackFrameCount = 4;

    [Tooltip("수명 끝난 뒤 추가로 더 남길 꼬리 시간(초)")]
    public float extraTailSeconds = 0.0f;

    [Header("Follow Parent Facing")]
    [Tooltip("부모의 좌우 방향(Flip)을 실시간 복사할까요?")]
    public bool followParentFlip = true;

    [Tooltip("부모의 SpriteRenderer.flipX를 우선 복사, 없으면 부모의 lossyScale.x 부호를 따릅니다.")]
    public bool preferSpriteRendererFlip = true;

    [Tooltip("이 이펙트의 SpriteRenderer가 있으면 flipX를 바꾸고, 없으면 localScale.x 부호를 바꿉니다.")]
    public SpriteRenderer selfRenderer;

    [Header("Attach / Offset")]
    [Tooltip("소환 시 부모에 자식으로 붙일지 여부")]
    public bool attachToParent = true;

    [Tooltip("부모 기준 로컬 오프셋")]
    public Vector3 spawnLocalOffset = Vector3.zero;

    [Header("Sorting Copy")]
    [Tooltip("부모 SpriteRenderer의 Sorting Layer/Order를 복사")]
    public bool copyParentSorting = true;

    [Tooltip("부모 Sorting Order에 더할 오프셋")]
    public int sortingOrderOffset = 0;

    Transform _parent;
    float _calculatedLife = -1f;
    bool _started;

    void Awake()
    {
        // Auto collect particles if empty
        if (particles == null || particles.Length == 0)
            particles = GetComponentsInChildren<ParticleSystem>(true);
    }

    void OnEnable()
    {
        if (_started) return;
        _started = true;

        _parent = transform.parent;

        // 위치/부모 설정
        if (_parent != null)
        {
            if (attachToParent) transform.SetParent(_parent, worldPositionStays: false);
            transform.localPosition = spawnLocalOffset;
        }

        // 소팅 계층 복사
        if (copyParentSorting && _parent != null)
        {
            var pSR = _parent.GetComponentInChildren<SpriteRenderer>();
            if (pSR != null)
            {
                var mySRs = GetComponentsInChildren<SpriteRenderer>(true);
                foreach (var sr in mySRs)
                {
                    sr.sortingLayerID = pSR.sortingLayerID;
                    sr.sortingOrder = pSR.sortingOrder + sortingOrderOffset;
                }
            }
        }

        // 소스 타입 자동 판정
        if (sourceType == SourceType.AutoDetect)
        {
            if (spriteAnimationManager != null) sourceType = SourceType.SpriteAnimationManager;
            else if (animatorRef != null) sourceType = SourceType.Animator;
            else if ((particles?.Length ?? 0) > 0) sourceType = SourceType.ParticleSystem;
            else sourceType = SourceType.Manual;
        }

        // 수명 계산
        _calculatedLife = CalcLifetimeSeconds();
        StartCoroutine(Co_Lifetime());
    }

    void Update()
    {
        if (!followParentFlip || _parent == null) return;

        bool parentFlip = false;
        if (preferSpriteRendererFlip)
        {
            var pSR = _parent.GetComponentInChildren<SpriteRenderer>();
            if (pSR != null) parentFlip = pSR.flipX;
            else parentFlip = (_parent.lossyScale.x < 0f);
        }
        else
        {
            parentFlip = (_parent.lossyScale.x < 0f);
        }

        ApplySelfFlip(parentFlip);
    }

    float CalcLifetimeSeconds()
    {
        // 1) 수동 지정 우선
        if (manualDuration > 0f) return manualDuration + extraTailSeconds;

        // 2) SpriteAnimationManager 추정(프로젝트별 API 상이 → fps/frames로 계산)
        if (sourceType == SourceType.SpriteAnimationManager)
        {
            float fps = Mathf.Max(0.0001f, fallbackFps);
            int frames = Mathf.Max(1, fallbackFrameCount);
            return (frames / fps) + extraTailSeconds;
        }

        // 3) Animator 클립 길이
        if (sourceType == SourceType.Animator && animatorRef != null && animatorRef.runtimeAnimatorController != null)
        {
            var clips = animatorRef.runtimeAnimatorController.animationClips;
            if (clips != null && clips.Length > 0)
            {
                // 가장 긴 클립 기준
                float maxLen = clips.Max(c => c.length / Mathf.Max(0.0001f, animatorRef.speed));
                if (maxLen > 0f) return maxLen + extraTailSeconds;
            }
        }

        // 4) 파티클: 명시 길이 없으면 일단 0(코루틴에서 IsAlive 체크)
        if (sourceType == SourceType.ParticleSystem && (particles?.Length ?? 0) > 0)
            return 0f;

        // 5) 정말 아무 정보가 없으면 1초 기본
        return 1f + extraTailSeconds;
    }

    IEnumerator Co_Lifetime()
    {
        // 파티클이면 다 꺼질 때까지 대기
        if (sourceType == SourceType.ParticleSystem && (particles?.Length ?? 0) > 0)
        {
            // 파티클을 재생 상태로 보장
            foreach (var ps in particles) if (ps != null) ps.Play();

            // 전부 소멸할 때까지 대기
            while (particles.Any(p => p != null && p.IsAlive(true)))
                yield return null;

            Destroy(gameObject);
            yield break;
        }

        // 그 외: 계산된 수명만큼 대기
        if (_calculatedLife > 0f)
            yield return new WaitForSeconds(_calculatedLife);

        Destroy(gameObject);
    }

    void ApplySelfFlip(bool flipX)
    {
        if (selfRenderer != null)
        {
            selfRenderer.flipX = flipX;
        }
        else
        {
            var s = transform.localScale;
            s.x = Mathf.Abs(s.x) * (flipX ? -1f : 1f);
            transform.localScale = s;
        }
    }
}
