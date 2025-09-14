using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class SpriteAnimationManager : MonoBehaviour
{
    [Serializable]
    public class SpriteAnim
    {
        public string name;
        public List<Sprite> frames = new List<Sprite>();
        public float fps = 12f;
        public bool loop = true;
    }

    [Header("Target")]
    [SerializeField] private SpriteRenderer target;

    [Header("Clips")]
    [SerializeField] private List<SpriteAnim> clips = new List<SpriteAnim>();

    [Header("Timing")]
    [SerializeField] private bool useUnscaledTime = false;

    [Header("Behavior")]
    [Tooltip("true면 PlayOnce 재생 중 다른 Play 호출을 무시합니다.")]
    public bool respectOneShots = true;

    // ===== 앵커 정규화(프레임마다 시각 오프셋 보정) =====
    public enum AnchorBasis { Center, BottomCenter }

    [Header("Anchor Normalization")]
    [Tooltip("프레임마다 피벗/트림 차이로 흔들리는 것을 보정합니다.")]
    [SerializeField] private bool normalizeAnchors = false;

    [Tooltip("Center는 중앙, BottomCenter는 발(하단 중앙)을 기준점으로 고정합니다.")]
    [SerializeField] private AnchorBasis anchorBasis = AnchorBasis.BottomCenter;

    [Tooltip("SpriteRenderer가 붙은 자식 트랜스폼(권장). 지정 없으면 target.transform을 사용.")]
    [SerializeField] private Transform visualRoot;

    // runtime
    private readonly Dictionary<string, SpriteAnim> _map = new();
    private SpriteAnim _current;
    private int _frameIndex;
    private float _accum;
    private bool _isOneShot;
    private string _fallbackAfterOnce;
    private string _currentName;

    // anchor runtime
    private Vector3 _visualBaseLocalPos;
    private bool _anchorReady;
    private Vector2 _baselineAnchor; // 해당 클립의 첫 프레임 기준 앵커

    public bool IsOneShotActive => _isOneShot;
    public string Current => _currentName;

    void Awake()
    {
        if (!target) target = GetComponentInChildren<SpriteRenderer>();
        if (!visualRoot) visualRoot = target ? target.transform : transform;
        _visualBaseLocalPos = visualRoot ? visualRoot.localPosition : Vector3.zero;

        BuildMap();

        // 초기화: 첫 클립이 있으면 그걸로 지정
        if (clips.Count > 0) SetClip(clips[0], forceRestart: true, markOnce: false, fallback: null);
    }

    void Reset()
    {
        target = GetComponentInChildren<SpriteRenderer>();
        if (!visualRoot) visualRoot = target ? target.transform : transform;
    }

    void BuildMap()
    {
        _map.Clear();
        foreach (var c in clips)
        {
            if (string.IsNullOrEmpty(c.name)) continue;
            if (!_map.ContainsKey(c.name)) _map.Add(c.name, c);
        }
    }

    void Update()
    {
        if (_current == null || target == null) return;
        var frames = _current.frames;
        if (frames == null || frames.Count == 0) return;

        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        if (_current.fps <= 0f)
        {
            ApplyFrame();
            return;
        }

        _accum += dt;
        float frameDur = 1f / _current.fps;

        while (_accum >= frameDur)
        {
            _accum -= frameDur;
            _frameIndex++;

            if (_frameIndex >= frames.Count)
            {
                if (_current.loop && !_isOneShot)
                {
                    _frameIndex = 0;
                }
                else
                {
                    _frameIndex = frames.Count - 1; // 마지막 프레임 고정
                    ApplyFrame();

                    if (_isOneShot)
                    {
                        string fb = _fallbackAfterOnce;
                        _isOneShot = false;
                        _fallbackAfterOnce = null;
                        if (!string.IsNullOrEmpty(fb) && _map.TryGetValue(fb, out var fbClip))
                        {
                            SetClip(fbClip, forceRestart: true, markOnce: false, fallback: null);
                        }
                    }
                    return;
                }
            }
        }

        ApplyFrame();
    }

    private void ApplyFrame()
    {
        var frames = _current.frames;
        if (frames == null || frames.Count == 0 || target == null) return;

        int idx = Mathf.Clamp(_frameIndex, 0, frames.Count - 1);
        var sprite = frames[idx];
        target.sprite = sprite;

        if (normalizeAnchors && visualRoot != null && sprite != null)
        {
            EnsureBaseline(frames[0]);                 // 해당 클립의 첫 프레임을 기준
            Vector2 cur = GetAnchorLocal(sprite);
            Vector2 delta = _baselineAnchor - cur;     // 기준 - 현재 = 보정량(로컬좌표)
            visualRoot.localPosition = _visualBaseLocalPos + (Vector3)delta;
        }
    }

    private void SetClip(SpriteAnim clip, bool forceRestart, bool markOnce, string fallback)
    {
        if (clip == null) return;

        bool same = (clip == _current);
        if (!forceRestart && same) return;

        _current = clip;
        _currentName = clip.name;
        _isOneShot = markOnce;
        _fallbackAfterOnce = markOnce ? fallback : null;

        _frameIndex = 0;
        _accum = 0f;

        // 새로운 클립 시작 시 앵커 기준 리셋(첫 프레임을 새 기준으로)
        _anchorReady = false;

        ApplyFrame();
    }

    // ============ Public API ============

    /// <summary>루프 애니 플레이. 1회재생 중이면 respectOneShots=true일 때 무시.</summary>
    public void Play(string name, bool forceRestart = false, bool interruptOneShot = false)
    {
        if (string.IsNullOrEmpty(name) || !_map.TryGetValue(name, out var clip)) return;
        if (_isOneShot && respectOneShots && !interruptOneShot) return;
        SetClip(clip, forceRestart, markOnce: false, fallback: null);
    }

    /// <summary>1회 재생 후 fallback으로 넘어감. (fallback이 null/빈문자면 마지막 프레임 고정)</summary>
    public void PlayOnce(string name, string fallback = null, bool forceRestart = true)
    {
        if (string.IsNullOrEmpty(name) || !_map.TryGetValue(name, out var clip)) return;
        SetClip(clip, forceRestart, markOnce: true, fallback: fallback);
    }

    /// <summary>현재 애니메이션이 name과 같은지.</summary>
    public bool IsPlaying(string name) => !string.IsNullOrEmpty(name) && _currentName == name;

    /// <summary>클립 유효성 (디버깅용)</summary>
    public bool HasClip(string name) => !string.IsNullOrEmpty(name) && _map.ContainsKey(name);

    // ===== 앵커 유틸 =====

    private void EnsureBaseline(Sprite firstFrame)
    {
        if (_anchorReady || firstFrame == null) return;
        _baselineAnchor = GetAnchorLocal(firstFrame);
        _anchorReady = true;
    }

    private Vector2 GetAnchorLocal(Sprite s)
    {
        // Sprite.bounds: 스프라이트 로컬좌표 기준 AABB
        Bounds b = s.bounds;
        return anchorBasis switch
        {
            AnchorBasis.Center => b.center,
            AnchorBasis.BottomCenter => new Vector2(b.center.x, b.min.y),
            _ => b.center
        };
    }
}
