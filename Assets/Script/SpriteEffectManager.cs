using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class SpriteEffectManager : MonoBehaviour
{
    [Serializable]
    public class EffectClip
    {
        public string name;
        public List<Sprite> frames = new List<Sprite>();
        [Min(1f)] public float fps = 12f;  // 기본 재생 속도(프레임/초)
        public bool loop = false;          // 기본은 1회성
        [Min(1)] public int repeat = 1;    // loop=false일 때 몇 번 반복할지
    }

    public static SpriteEffectManager Instance { get; private set; }

    [Header("Clips")]
    [SerializeField] private List<EffectClip> clips = new();

    [Header("Render Defaults (optional)")]
    [Tooltip("여기 지정하면 신규 이펙트 인스턴스의 초기 색/레이어/오더를 이 렌더러에서 복사합니다.")]
    [SerializeField] private SpriteRenderer styleSource;

    [Header("Timing")]
    [SerializeField] private bool useUnscaledTime = false;

    private readonly Dictionary<string, EffectClip> _map = new(64);

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildMap();
    }

    void OnValidate() => BuildMap();

    private void BuildMap()
    {
        _map.Clear();
        foreach (var c in clips)
        {
            if (c == null || string.IsNullOrWhiteSpace(c.name)) continue;
            if (c.fps < 1f) c.fps = 1f;
            if (c.repeat < 1) c.repeat = 1;
            if (!_map.ContainsKey(c.name)) _map.Add(c.name, c);
        }
    }

    // -------- Public API --------
    public void Play(string name, Transform at) => Play(name, at.position);

    public void Play(string name, Vector3 position) =>
        Spawn(name, position, Vector2.right, 0f, align: false, seconds: null, parent: null);

    public void PlayDir(string name, Vector3 position, Vector2 dir, float speed = 0f, bool align = false, Transform parent = null) =>
        Spawn(name, position, dir, speed, align, seconds: null, parent: parent);

    public void PlayFor(string name, Vector3 position, float seconds, Transform parent = null) =>
        Spawn(name, position, Vector2.right, 0f, align: false, seconds: Mathf.Max(0.01f, seconds), parent: parent);

    // === 추가: 크기 조절 API ===
    /// <summary>스케일 배수로 크기 조절</summary>
    public void PlayScaled(string name, Vector3 position, float scale, Transform parent = null) =>
        SpawnSized(name, position, Vector2.right, 0f, align: false, seconds: null, parent: parent, scaleMul: Mathf.Max(0.0001f, scale), fitHeight: null);

    /// <summary>월드 높이를 정확히 맞춰 크기 조절</summary>
    public void PlayHeight(string name, Vector3 position, float height, Transform parent = null) =>
        SpawnSized(name, position, Vector2.right, 0f, align: false, seconds: null, parent: parent, scaleMul: 1f, fitHeight: Mathf.Max(0.0001f, height));

    // -------- 내부 스폰 (기존) --------
    private void Spawn(string name, Vector3 pos, Vector2 dir, float speed, bool align, float? seconds, Transform parent)
        => SpawnSized(name, pos, dir, speed, align, seconds, parent, scaleMul: 1f, fitHeight: null);

    // -------- 내부 스폰 (크기 지원) --------
    private void SpawnSized(string name, Vector3 pos, Vector2 dir, float speed, bool align, float? seconds, Transform parent, float scaleMul, float? fitHeight)
    {
        if (!_map.TryGetValue(name, out var clip) || clip.frames == null || clip.frames.Count == 0)
        {
            Debug.LogWarning($"[SpriteEffectManager] Clip not found/empty: {name}");
            return;
        }

        var go = new GameObject($"FX_{name}", typeof(SpriteRenderer), typeof(Runner));
        var t = go.transform;
        t.SetParent(parent, worldPositionStays: true);
        t.position = pos;

        var sr = go.GetComponent<SpriteRenderer>();
        if (styleSource != null)
        {
            sr.sortingLayerID = styleSource.sortingLayerID;
            sr.sortingOrder = styleSource.sortingOrder + 1;
            sr.color = styleSource.color;
            sr.flipX = styleSource.flipX;
            sr.flipY = styleSource.flipY;
        }

        var r = go.GetComponent<Runner>();
        r.Init(sr, clip, dir, speed, align, seconds, useUnscaledTime);

        // ---- 크기 적용 ----
        // 1) 스케일 배수
        t.localScale = new Vector3(scaleMul, scaleMul, 1f);

        // 2) 월드 높이로 맞추기(선택): 첫 프레임 기준으로 계산
        if (fitHeight.HasValue && sr && sr.sprite)
        {
            float currentWorldH = sr.bounds.size.y; // 현재 스케일에서의 월드 높이
            if (currentWorldH > 0.00001f)
            {
                float mul = fitHeight.Value / currentWorldH;
                t.localScale *= mul;
            }
        }
    }

    // -------- 이펙트 1개 실행기(간단 애니+이동) --------
    private class Runner : MonoBehaviour
    {
        private SpriteRenderer _sr;
        private EffectClip _clip;

        private int _frame;
        private float _accum;
        private float _fps;
        private bool _loop;
        private int _targetRepeat;
        private int _playedCount; // 완료한 회수

        private bool _useUnscaled;
        private Vector2 _vel;
        private bool _align;

        public void Init(SpriteRenderer sr, EffectClip clip, Vector2 dir, float speed, bool align, float? seconds, bool useUnscaled)
        {
            _sr = sr;
            _clip = clip;
            _useUnscaled = useUnscaled;

            // FPS 결정: seconds가 주어지면 해당 시간에 프레임 전부 소모되도록 재계산
            _fps = (seconds.HasValue && seconds.Value > 0.01f)
                 ? Mathf.Max(1f, clip.frames.Count / seconds.Value)
                 : Mathf.Max(1f, clip.fps);

            _loop = clip.loop;
            _targetRepeat = Mathf.Max(1, clip.repeat);
            _playedCount = 0;

            _frame = 0;
            _accum = 0f;

            _vel = (dir.sqrMagnitude > 0f) ? dir.normalized * Mathf.Max(0f, speed) : Vector2.zero;
            _align = align && dir.sqrMagnitude > 0f;

            _sr.sprite = clip.frames[0];

            if (_align)
            {
                float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                transform.rotation = Quaternion.Euler(0f, 0f, angle);
            }
        }

        void Update()
        {
            if (_clip == null || _sr == null) { Destroy(gameObject); return; }

            float dt = _useUnscaled ? Time.unscaledDeltaTime : Time.deltaTime;

            if (_vel.sqrMagnitude > 0f)
                transform.position += (Vector3)(_vel * dt);

            // 프레임 진행
            _accum += dt;
            float frameDur = 1f / _fps;

            while (_accum >= frameDur)
            {
                _accum -= frameDur;
                _frame++;

                if (_frame >= _clip.frames.Count)
                {
                    if (_loop)
                    {
                        _frame = 0;
                    }
                    else
                    {
                        _playedCount++;
                        if (_playedCount >= _targetRepeat)
                        {
                            Destroy(gameObject);
                            return;
                        }
                        _frame = 0;
                    }
                }
                _sr.sprite = _clip.frames[_frame];
            }
        }
    }
}

// --------- 한 줄 접근 Facade ---------
public static class FX
{
    public static void Play(string name, Vector3 at) =>
        SpriteEffectManager.Instance?.Play(name, at);

    public static void Play(string name, Transform at) =>
        SpriteEffectManager.Instance?.Play(name, at);

    public static void PlayDir(string name, Vector3 at, Vector2 dir, float speed = 0f, bool align = false, Transform parent = null) =>
        SpriteEffectManager.Instance?.PlayDir(name, at, dir, speed, align, parent);

    public static void PlayFor(string name, Vector3 at, float seconds, Transform parent = null) =>
        SpriteEffectManager.Instance?.PlayFor(name, at, seconds, parent);

    // === 추가: 크기 조절용 ===
    public static void Play(string name, Vector3 at, float scale) =>
        SpriteEffectManager.Instance?.PlayScaled(name, at, scale);

    public static void PlayHeight(string name, Vector3 at, float height) =>
        SpriteEffectManager.Instance?.PlayHeight(name, at, height);
}
