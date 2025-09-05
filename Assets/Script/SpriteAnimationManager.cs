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

    // runtime
    private readonly Dictionary<string, SpriteAnim> _map = new();
    private SpriteAnim _current;
    private int _frameIndex;
    private float _accum;
    private bool _isOneShot;
    private string _fallbackAfterOnce;
    private string _currentName;

    public bool IsOneShotActive => _isOneShot;
    public string Current => _currentName;

    void Awake()
    {
        if (!target) target = GetComponentInChildren<SpriteRenderer>();
        BuildMap();
        // 초기화: 첫 클립이 있으면 그걸로 지정
        if (clips.Count > 0) SetClip(clips[0], forceRestart: true, markOnce: false, fallback: null);
    }

    void Reset()
    {
        target = GetComponentInChildren<SpriteRenderer>();
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
        { // fps 0이면 현재 프레임 유지
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
                    // once 종료 지점
                    _frameIndex = frames.Count - 1; // 마지막 프레임 고정
                    ApplyFrame();

                    if (_isOneShot)
                    {
                        // fallback으로 전환
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
        if (frames == null || frames.Count == 0) return;
        int idx = Mathf.Clamp(_frameIndex, 0, frames.Count - 1);
        target.sprite = frames[idx];
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
        ApplyFrame();
    }

    // ============ Public API ============

    /// <summary>
    /// 루프 애니 플레이. 1회재생 중이면 respectOneShots=true일 때 무시.
    /// </summary>
    public void Play(string name, bool forceRestart = false, bool interruptOneShot = false)
    {
        if (string.IsNullOrEmpty(name) || !_map.TryGetValue(name, out var clip)) return;

        if (_isOneShot && respectOneShots && !interruptOneShot)
            return; // 1회 재생 보호

        // Play는 loop 애니로 간주 (clip.loop 설정을 그대로 사용)
        SetClip(clip, forceRestart, markOnce: false, fallback: null);
    }

    /// <summary>
    /// 1회 재생 후 fallback으로 넘어감. (fallback이 null/빈문자면 마지막 프레임 고정)
    /// </summary>
    public void PlayOnce(string name, string fallback = null, bool forceRestart = true)
    {
        if (string.IsNullOrEmpty(name) || !_map.TryGetValue(name, out var clip)) return;

        // clip.loop 값과 상관없이 1회로 취급
        SetClip(clip, forceRestart, markOnce: true, fallback: fallback);
    }

    /// <summary>
    /// 현재 애니메이션이 name과 같은지.
    /// </summary>
    public bool IsPlaying(string name) => !string.IsNullOrEmpty(name) && _currentName == name;

    /// <summary>
    /// 클립 유효성 검사 (디버깅용)
    /// </summary>
    public bool HasClip(string name) => !string.IsNullOrEmpty(name) && _map.ContainsKey(name);
}
