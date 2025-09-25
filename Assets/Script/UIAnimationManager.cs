using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
public class UIAnimationManager : MonoBehaviour
{
    // 단일 스프라이트 애니메이션 클립을 정의하는 클래스
    [Serializable]
    public class SpriteAnim
    {
        public string name;
        public List<Sprite> frames = new List<Sprite>();
        [Range(1f, 60f)]
        public float fps = 12f;
        public bool loop = true;

        [Tooltip("이 클립 재생 시 UI 요소를 X축 기준으로 반전시킵니다.")]
        public bool flipX = false;
        [Tooltip("이 클립 재생 시 UI 요소를 Y축 기준으로 반전시킵니다.")]
        public bool flipY = false;

        [Tooltip("이 클립 재생 시 적용할 추가적인 위치 오프셋입니다. (기본 baseLocalPosition에 더해짐)")]
        public Vector2 localPositionOffset = Vector2.zero;

        // localScaleMultiplier를 Vector2로 변경하여 X, Y 스케일을 개별적으로 조절할 수 있도록 함
        [Tooltip("이 클립 재생 시 적용할 X, Y축 스케일 승수입니다. (기본 localScale에 곱해짐)")]
        public Vector2 localScaleMultiplier = new Vector2(1f, 1f);
    }

    // 애니메이션 시퀀스의 한 단계를 정의하는 클래스
    [Serializable]
    public class AnimationSequenceStep
    {
        [Tooltip("시퀀스에서 재생할 클립의 이름입니다.")]
        public string clipName;
        [Tooltip("이 클립을 몇 번 재생할지 (완료될 때까지 기다림). 0이면 무한 루프입니다 (시퀀스 내에서는 다음 스텝으로 넘어가지 않습니다).")]
        [Min(0)] public int playCount = 1;
    }

    [Header("Target UI Image")]
    [Tooltip("애니메이션을 표시할 UI Image 컴포넌트입니다. 자동으로 할당됩니다.")]
    [SerializeField] private Image targetImage;

    [Header("Animation Clips")]
    [Tooltip("이 Manager가 재생할 수 있는 개별 애니메이션 클립들의 목록입니다.")]
    [SerializeField] private List<SpriteAnim> clips = new List<SpriteAnim>();

    [Header("Default Animation Settings")]
    [Tooltip("게임 시작 시 자동으로 재생할 클립의 이름입니다. 'Play Sequence On Start'가 비활성화된 경우에만 적용됩니다. 비어있으면 자동 재생하지 않습니다.")]
    [SerializeField] private string defaultClipName = "";
    [Tooltip("기본 애니메이션이 원샷(한 번 재생)인지 여부입니다. 'Play Sequence On Start'가 비활성화된 경우에만 적용됩니다.")]
    [SerializeField] private bool defaultClipIsOneShot = false;
    [Tooltip("기본 애니메이션이 원샷일 경우, 종료 후 재생할 클립의 이름입니다. 비어있으면 마지막 프레임에 고정됩니다. 'Play Sequence On Start'가 비활성화된 경우에만 적용됩니다.")]
    [SerializeField] private string defaultClipFallbackName = "";


    [Header("Animation Sequence Settings")]
    [Tooltip("게임을 시작할 때 이 시퀀스를 재생할지 여부입니다. 활성화되면 'Default Animation Settings'보다 우선합니다.")]
    [SerializeField] private bool playSequenceOnStart = false;
    [Tooltip("시작 시 재생할 시퀀스의 클립 목록입니다. 각 클립을 지정된 횟수만큼 재생하고 다음으로 넘어갑니다.")]
    [SerializeField] private List<AnimationSequenceStep> sequenceSteps = new List<AnimationSequenceStep>();
    [Tooltip("시퀀스의 모든 스텝이 완료된 후, 처음부터 다시 반복할지 여부입니다.")]
    [SerializeField] private bool loopSequence = false;


    [Header("UI Transform Control (Absolute Positioning)")]
    [Tooltip("이 UI 요소의 기준 로컬 위치입니다. 클립별 오프셋이 여기에 더해집니다. (캔버스 중앙 기준 픽셀 단위)")]
    [SerializeField] private Vector2 baseLocalPosition = Vector2.zero;
    [Tooltip("이 UI 요소의 기준 크기를 인스펙터에서 직접 설정합니다. (단위: 픽셀) 클립별 스케일 승수가 여기에 곱해집니다.")]
    [SerializeField] private Vector2 baseSize = new Vector2(100, 100);
    [Tooltip("이 UI 요소의 로컬 회전을 인스펙터에서 직접 설정합니다. (Z축)")]
    [SerializeField] private float rotationZ = 0f;


    [Header("Timing")]
    [Tooltip("true면 시간 스케일에 영향을 받지 않습니다 (UI나 메뉴 애니메이션에 유용).")]
    [SerializeField] private bool useUnscaledTime = false;

    [Header("Behavior")]
    [Tooltip("true면 PlayOnce 재생 중 다른 Play 호출을 무시합니다.")]
    public bool respectOneShots = true;

    // 런타임 데이터
    private readonly Dictionary<string, SpriteAnim> _clipMap = new();
    private SpriteAnim _currentClip;
    private int _currentFrameIndex;
    private float _frameAccumulator;
    private bool _isOneShotPlaying;
    private string _fallbackClipNameAfterOneShot;
    private string _currentClipName;

    private RectTransform _rectTransform;

    // 시퀀스 관련 런타임 데이터
    private bool _isSequencePlaying = false;
    private int _currentSequenceStepIndex = -1;
    private int _currentStepPlayCount = 0; // 현재 스텝의 클립을 몇 번 재생했는지

    public bool IsOneShotActive => _isOneShotPlaying;
    public string CurrentAnimationName => _currentClipName;

    void Awake()
    {
        if (targetImage == null)
        {
            targetImage = GetComponent<Image>();
        }
        _rectTransform = GetComponent<RectTransform>();

        BuildClipMap();

        ApplyRectTransformFixedSettings();

        if (playSequenceOnStart && sequenceSteps.Count > 0)
        {
            StartSequence();
        }
        else if (!string.IsNullOrEmpty(defaultClipName) && _clipMap.TryGetValue(defaultClipName, out var defaultClip))
        {
            SetCurrentClip(defaultClip, forceRestart: true, markAsOneShot: defaultClipIsOneShot, fallback: defaultClipFallbackName);
        }
        else if (clips.Count > 0)
        {
            SetCurrentClip(clips[0], forceRestart: true, markAsOneShot: false, fallback: null);
        }
        else
        {
            ApplyCurrentClipSpecificLocalScale();
            ApplyCurrentClipSpecificPosition();
            ApplyCurrentClipSpecificSize();
        }
    }

    void OnValidate()
    {
        if (targetImage == null) targetImage = GetComponent<Image>();
        if (_rectTransform == null) _rectTransform = GetComponent<RectTransform>();

        BuildClipMap();
        ApplyRectTransformFixedSettings();

        if (_currentClip != null)
        {
            ApplyCurrentClipSpecificLocalScale();
            ApplyCurrentClipSpecificPosition();
            ApplyCurrentClipSpecificSize();
        }
        else
        {
            // 클립이 없으면 기본값으로 초기화
            _rectTransform.localScale = Vector3.one;
            _rectTransform.localPosition = new Vector3(baseLocalPosition.x, baseLocalPosition.y, _rectTransform.localPosition.z);
            _rectTransform.sizeDelta = baseSize;
        }
    }

    private void BuildClipMap()
    {
        _clipMap.Clear();
        foreach (var clip in clips)
        {
            if (string.IsNullOrEmpty(clip.name))
            {
                Debug.LogWarning("UIAnimationManager: Clip name cannot be empty.", this);
                continue;
            }
            if (_clipMap.ContainsKey(clip.name))
            {
                Debug.LogWarning($"UIAnimationManager: Duplicate clip name '{clip.name}' found. Only the first one will be used.", this);
                continue;
            }
            _clipMap.Add(clip.name, clip);
        }
    }

    private void ApplyRectTransformFixedSettings()
    {
        if (_rectTransform == null) return;

        _rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        _rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        _rectTransform.pivot = new Vector2(0.5f, 0.5f);

        _rectTransform.localRotation = Quaternion.Euler(0, 0, rotationZ);
    }

    // 현재 클립의 flipX/Y 설정과 localScaleMultiplier에 따라 LocalScale을 적용하는 메서드
    private void ApplyCurrentClipSpecificLocalScale()
    {
        if (_rectTransform == null) return;

        float scaleX = 1f;
        float scaleY = 1f;
        Vector2 multiplier = Vector2.one; // Vector2로 변경

        if (_currentClip != null)
        {
            if (_currentClip.flipX) scaleX = -1f;
            if (_currentClip.flipY) scaleY = -1f;
            multiplier = _currentClip.localScaleMultiplier;
        }

        _rectTransform.localScale = new Vector3(scaleX * multiplier.x, scaleY * multiplier.y, 1f); // multiplier.x, multiplier.y 적용
    }

    // 현재 클립의 localPositionOffset 설정에 따라 위치를 적용하는 메서드
    private void ApplyCurrentClipSpecificPosition()
    {
        if (_rectTransform == null) return;

        Vector2 offset = Vector2.zero;
        if (_currentClip != null)
        {
            offset = _currentClip.localPositionOffset;
        }

        _rectTransform.localPosition = new Vector3(baseLocalPosition.x + offset.x, baseLocalPosition.y + offset.y, _rectTransform.localPosition.z);
    }

    // 현재 클립의 localScaleMultiplier에 따라 Size를 적용하는 메서드
    private void ApplyCurrentClipSpecificSize()
    {
        if (_rectTransform == null) return;

        Vector2 multiplier = Vector2.one; // Vector2로 변경
        if (_currentClip != null)
        {
            multiplier = _currentClip.localScaleMultiplier;
        }

        // baseSize에 multiplier.x와 multiplier.y를 각각 곱하여 최종 크기를 설정
        _rectTransform.sizeDelta = new Vector2(baseSize.x * multiplier.x, baseSize.y * multiplier.y);
    }


    void Update()
    {
        if (_currentClip == null || targetImage == null || _currentClip.frames.Count == 0) return;

        float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

        if (_currentClip.fps <= 0f)
        {
            ApplyCurrentFrameToImage();
            return;
        }

        _frameAccumulator += deltaTime;
        float frameDuration = 1f / _currentClip.fps;

        while (_frameAccumulator >= frameDuration)
        {
            _frameAccumulator -= frameDuration;
            _currentFrameIndex++;

            if (_currentFrameIndex >= _currentClip.frames.Count)
            {
                if (_isSequencePlaying)
                {
                    _currentStepPlayCount++;

                    if (_currentStepPlayCount >= sequenceSteps[_currentSequenceStepIndex].playCount)
                    {
                        _currentSequenceStepIndex++;
                        _currentStepPlayCount = 0;

                        if (_currentSequenceStepIndex >= sequenceSteps.Count)
                        {
                            if (loopSequence)
                            {
                                _currentSequenceStepIndex = 0;
                            }
                            else
                            {
                                StopSequence();
                                return;
                            }
                        }
                        StartSequenceStep(_currentSequenceStepIndex);
                        return;
                    }
                    else
                    {
                        _currentFrameIndex = 0;
                    }
                }
                else
                {
                    if (_currentClip.loop && !_isOneShotPlaying)
                    {
                        _currentFrameIndex = 0;
                    }
                    else
                    {
                        _currentFrameIndex = _currentClip.frames.Count - 1;
                        ApplyCurrentFrameToImage();

                        if (_isOneShotPlaying)
                        {
                            string fallback = _fallbackClipNameAfterOneShot;
                            _isOneShotPlaying = false;
                            _fallbackClipNameAfterOneShot = null;

                            if (!string.IsNullOrEmpty(fallback) && _clipMap.TryGetValue(fallback, out var fallbackClip))
                            {
                                SetCurrentClip(fallbackClip, forceRestart: true, markAsOneShot: false, fallback: null);
                            }
                        }
                        return;
                    }
                }
            }
        }

        ApplyCurrentFrameToImage();
    }

    private void ApplyCurrentFrameToImage()
    {
        if (_currentClip == null || targetImage == null || _currentClip.frames.Count == 0) return;

        int actualIndex = Mathf.Clamp(_currentFrameIndex, 0, _currentClip.frames.Count - 1);
        targetImage.sprite = _currentClip.frames[actualIndex];
    }

    private void SetCurrentClip(SpriteAnim clip, bool forceRestart, bool markAsOneShot, string fallback)
    {
        if (clip == null) return;

        bool isSameClip = (clip == _currentClip);
        if (!forceRestart && isSameClip) return;

        _currentClip = clip;
        _currentClipName = clip.name;
        _isOneShotPlaying = markAsOneShot;
        _fallbackClipNameAfterOneShot = markAsOneShot ? fallback : null;

        _currentFrameIndex = 0;
        _frameAccumulator = 0f;

        ApplyCurrentClipSpecificLocalScale();
        ApplyCurrentClipSpecificPosition();
        ApplyCurrentClipSpecificSize();

        ApplyCurrentFrameToImage();
    }

    // =================================== Public API (Sequence) ===================================

    /// <summary>
    /// 인스펙터에 설정된 애니메이션 시퀀스를 시작합니다.
    /// </summary>
    public void StartSequence()
    {
        if (sequenceSteps.Count == 0)
        {
            Debug.LogWarning("UIAnimationManager: No sequence steps defined.", this);
            return;
        }

        _isSequencePlaying = true;
        _currentSequenceStepIndex = 0;
        _currentStepPlayCount = 0;
        StartSequenceStep(_currentSequenceStepIndex);
    }

    /// <summary>
    /// 현재 재생 중인 애니메이션 시퀀스를 중지하고, 기본 클립(defaultClipName)이 설정되어 있다면 해당 클립으로 전환합니다.
    /// </summary>
    public void StopSequence()
    {
        if (!_isSequencePlaying) return;

        _isSequencePlaying = false;
        _currentSequenceStepIndex = -1;
        _currentStepPlayCount = 0;

        if (!string.IsNullOrEmpty(defaultClipName) && _clipMap.TryGetValue(defaultClipName, out var defaultClip))
        {
            SetCurrentClip(defaultClip, forceRestart: true, markAsOneShot: defaultClipIsOneShot, fallback: defaultClipFallbackName);
        }
        else
        {
            _currentClip = null;
            targetImage.sprite = null;

            _rectTransform.localScale = Vector3.one;
            _rectTransform.localPosition = new Vector3(baseLocalPosition.x, baseLocalPosition.y, _rectTransform.localPosition.z);
            _rectTransform.sizeDelta = baseSize;
        }
    }


    // 시퀀스의 특정 스텝을 시작합니다.
    private void StartSequenceStep(int stepIndex)
    {
        if (stepIndex < 0 || stepIndex >= sequenceSteps.Count)
        {
            Debug.LogError($"UIAnimationManager: Invalid sequence step index {stepIndex}", this);
            StopSequence();
            return;
        }

        var step = sequenceSteps[stepIndex];
        if (!_clipMap.TryGetValue(step.clipName, out var clipToPlay))
        {
            Debug.LogWarning($"UIAnimationManager: Clip '{step.clipName}' not found for sequence step {stepIndex}.", this);
            StopSequence();
            return;
        }

        SetCurrentClip(clipToPlay, forceRestart: true, markAsOneShot: true, fallback: null);
        _isOneShotPlaying = true;
    }

    // =================================== Public API (Legacy Play / PlayOnce) ===================================

    /// <summary>
    /// 지정된 이름의 애니메이션을 루프 재생합니다. 시퀀스 재생 중에는 시퀀스 로직이 우선하며 이 호출은 무시됩니다.
    /// </summary>
    public void Play(string clipName, bool forceRestart = false, bool interruptOneShot = false)
    {
        if (_isSequencePlaying)
        {
            Debug.LogWarning($"UIAnimationManager: Cannot Play '{clipName}'. Sequence is currently playing. Use StopSequence() first.", this);
            return;
        }

        if (string.IsNullOrEmpty(clipName) || !_clipMap.TryGetValue(clipName, out var clip))
        {
            Debug.LogWarning($"UIAnimationManager: Clip '{clipName}' not found.", this);
            return;
        }

        if (_isOneShotPlaying && respectOneShots && !interruptOneShot)
        {
            return;
        }

        SetCurrentClip(clip, forceRestart, markAsOneShot: false, fallback: null);
    }

    /// <summary>
    /// 지정된 이름의 애니메이션을 한 번 재생한 후, 선택적으로 fallback 클립으로 전환합니다. 시퀀스 재생 중에는 시퀀스 로직이 우선하며 이 호출은 무시됩니다.
    /// </summary>
    public void PlayOnce(string clipName, string fallbackClipName = null, bool forceRestart = true)
    {
        if (_isSequencePlaying)
        {
            Debug.LogWarning($"UIAnimationManager: Cannot PlayOnce '{clipName}'. Sequence is currently playing. Use StopSequence() first.", this);
            return;
        }

        if (string.IsNullOrEmpty(clipName) || !_clipMap.TryGetValue(clipName, out var clip))
        {
            Debug.LogWarning($"UIAnimationManager: Clip '{clipName}' not found.", this);
            return;
        }

        SetCurrentClip(clip, forceRestart, markAsOneShot: true, fallback: fallbackClipName);
    }

    /// <summary>
    /// 현재 지정된 이름의 애니메이션이 재생 중인지 확인합니다.
    /// </summary>
    public bool IsPlaying(string clipName)
    {
        return !string.IsNullOrEmpty(clipName) && _currentClipName == clipName;
    }

    /// <summary>
    /// 해당 이름의 애니메이션 클립이 존재하는지 확인합니다 (디버깅용).
    /// </summary>
    public bool HasClip(string clipName)
    {
        return !string.IsNullOrEmpty(clipName) && _clipMap.ContainsKey(clipName);
    }
}