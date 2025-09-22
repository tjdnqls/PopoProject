using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

[DisallowMultipleComponent]
public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    [Header("Edit Mode")]
    [Tooltip("에디터 편집 모드에서도 사운드를 들을 수 있게 허용합니다. (권장: 꺼두기)")]
    public bool allowEditModePlayback = false;

    // ============ 데이터 모델 ============

    public enum LoopMode { None, Continuous, RetriggerWithCooldown }
    public enum SubMode { None, Alternate, Simultaneous }

    [Serializable]
    public class SoundDef
    {
        [Header("Identity")]
        public string name;

        [Header("Clips")]
        public List<AudioClip> mainClips = new List<AudioClip>();
        [Tooltip("서브 사운드 사용")]
        public bool enableSub = false;
        public List<AudioClip> subClips = new List<AudioClip>();

        [Header("Play Window (sec)")]
        [Tooltip("이 시점부터 재생(0이면 처음부터)")]
        [Min(0f)] public float startAt = 0f;
        [Tooltip("이 시점까지만 재생(0이거나 startAt보다 작/같으면 전체 길이)")]
        [Min(0f)] public float endAt = 0f;

        [Header("Volume/Pitch")]
        [Range(0f, 1f)] public float volume = 1f;
        [Range(0f, 3f)] public float pitch = 1f;
        [Tooltip("볼륨 랜덤 ±범위(0~1). 예: 0.1 → 0.9~1.1 배")]
        [Range(0f, 1f)] public float volumeRandom = 0f;
        [Tooltip("피치 랜덤 ±범위(0~1). 예: 0.05 → 0.95~1.05 배")]
        [Range(0f, 1f)] public float pitchRandom = 0f;

        [Header("Space / Follow")]
        [Tooltip("0=2D, 1=3D(입체)")]
        [Range(0f, 1f)] public float spatialBlend = 0f;
        [Tooltip("재생 중 위치 트랜스폼을 추적할지")]
        public bool followTarget = true;
        [Tooltip("3D 거리 감쇠")]
        public AudioRolloffMode rolloff = AudioRolloffMode.Logarithmic;
        public float minDistance = 1f;
        public float maxDistance = 20f;
        [Range(0f, 5f)] public float dopplerLevel = 0f;
        [Range(0f, 360f)] public float spread = 0f;

        [Header("Loop / Cooldown")]
        public LoopMode loopMode = LoopMode.None;
        [Tooltip("Continuous 모드에선 반복 간격, Retrigger 모드에선 최소 재트리거 간격")]
        [Min(0f)] public float cooldown = 0.2f;
        [Tooltip("StopLoop 시 현재 재생 중인 보이스는 끝까지 두고 종료")]
        public bool gracefulStopLoop = true;

        [Header("Sub Sound Mode")]
        public SubMode subMode = SubMode.None;
        [Tooltip("교차 모드일 때 첫 재생에 서브를 먼저 시작")]
        public bool startWithSub = false;
        [Tooltip("Simultaneous 또는 Alternate에서 서로 간 지연(초)")]
        [Min(0f)] public float subDelay = 0f;

        [Header("Polyphony / Mixer")]
        [Tooltip("동시 재생 가능 수")]
        [Min(1)] public int maxVoices = 8;
        [Tooltip("가득 찼을 때 가장 오래된 보이스를 제거하고 재생")]
        public bool stealOldestOnLimit = true;
        public AudioMixerGroup outputMixerGroup;
        [Range(0, 256)] public int priority = 128; // 낮을수록 우선

        [Header("Advanced")]
        [Tooltip("이 사운드의 기본 위치(비워두면 호출 시 전달 Transform/좌표 사용)")]
        public Transform defaultAnchor;
    }

    // ============ 풀 & 런타임 ============

    private class Voice
    {
        public AudioSource src;
        public Transform anchor;
        public bool follow;
        public string soundName;
        public double scheduledEnd;  // dspTime
        public bool inUse;
    }

    private class SoundRuntime
    {
        public bool looping;
        public bool nextIsSub;      // Alternate용
        public double lastTriggerDsp;
        public Coroutine loopCo;
    }

    [Header("Library")]
    [SerializeField] private List<SoundDef> sounds = new List<SoundDef>();

    [Header("Pool")]
    [SerializeField, Min(0)] private int prewarmVoices = 8;

    private readonly Dictionary<string, SoundDef> _map = new();
    private readonly Dictionary<string, SoundRuntime> _run = new();

    private readonly List<Voice> _pool = new();
    private readonly List<Voice> _tempToRelease = new();

    // ============ 라이프사이클 ============

    private void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // 매니저만 영속화 (보이스는 항상 매니저의 자식으로 유지)
        DontDestroyOnLoad(gameObject);

        BuildMap();

        for (int i = 0; i < prewarmVoices; i++)
            _pool.Add(CreateVoice());
    }

    private void LateUpdate()
    {
        // 팔로우 & 정리
        double now = AudioSettings.dspTime;

        for (int i = 0; i < _pool.Count; i++)
        {
            var v = _pool[i];
            if (!v.inUse) continue;

            if (v.follow && v.anchor)
                v.src.transform.position = v.anchor.position;

            // isPlaying이 false 여도 scheduledEnd가 남아 있을 수 있음 → dsp 기준으로 종료 판단
            if (!v.src.isPlaying && now >= v.scheduledEnd - 0.001)
                ReleaseVoice(v);
        }
    }

    private void OnDestroy()
    {
        // 코루틴 정리
        foreach (var kv in _run)
        {
            var rt = kv.Value;
            if (rt.loopCo != null) StopCoroutine(rt.loopCo);
        }
        _run.Clear();
    }

    // ============ 맵/보이스 풀 ============

    private void BuildMap()
    {
        _map.Clear();
        foreach (var s in sounds)
        {
            if (string.IsNullOrEmpty(s.name)) continue;
            if (!_map.ContainsKey(s.name)) _map.Add(s.name, s);
            if (!_run.ContainsKey(s.name)) _run.Add(s.name, new SoundRuntime { nextIsSub = s.startWithSub });
        }
    }

    private Voice CreateVoice()
    {
        var go = new GameObject("SFXVoice");
        // 🔵 항상 매니저의 자식으로 두고, 씬 저장을 방해하는 HideFlags는 쓰지 않는다.
        go.transform.SetParent(transform, false);
        go.hideFlags = HideFlags.None;

        var src = go.AddComponent<AudioSource>();
        src.hideFlags = HideFlags.None;
        src.playOnAwake = false;
        src.loop = false;
        src.spatialBlend = 0f;
        src.rolloffMode = AudioRolloffMode.Logarithmic;
        src.minDistance = 1f;
        src.maxDistance = 20f;
        src.dopplerLevel = 0f;
        src.spread = 0f;
        src.priority = 128;

        return new Voice
        {
            src = src,
            inUse = false,
            follow = false,
            soundName = null,
            anchor = null,
            scheduledEnd = 0
        };
    }

    private Voice AcquireVoice(SoundDef def, string name)
    {
        // 동시 재생 수 체크
        int activeCount = 0;
        for (int i = 0; i < _pool.Count; i++)
            if (_pool[i].inUse && _pool[i].soundName == name) activeCount++;

        if (activeCount >= def.maxVoices)
        {
            if (!def.stealOldestOnLimit) return null;

            // 가장 먼저 끝나는(=scheduledEnd가 가장 이른) 보이스를 해제
            Voice oldest = null;
            double oldestEnd = double.MaxValue;
            for (int i = 0; i < _pool.Count; i++)
            {
                var v = _pool[i];
                if (!v.inUse || v.soundName != name) continue;
                if (v.scheduledEnd < oldestEnd) { oldestEnd = v.scheduledEnd; oldest = v; }
            }
            if (oldest != null) ReleaseVoice(oldest);
        }

        // 빈 보이스 사용
        for (int i = 0; i < _pool.Count; i++)
            if (!_pool[i].inUse)
            {
                var v = _pool[i];
                v.inUse = true;
                v.soundName = name;
                return v;
            }

        // 부족하면 새로 생성
        var nv = CreateVoice();
        nv.inUse = true;
        nv.soundName = name;
        _pool.Add(nv);
        return nv;
    }

    private void ReleaseVoice(Voice v)
    {
        if (!v.inUse) return;
        v.inUse = false;

        if (v.src)
        {
            v.src.Stop();
            v.src.clip = null;
        }

        v.anchor = null;
        v.follow = false;
        v.soundName = null;
        v.scheduledEnd = 0;
    }

    // ============ 퍼블릭 API (간편 호출) ============

    // 편집 모드 보호
    private bool CanPlayNow()
    {
        return Application.isPlaying || allowEditModePlayback;
    }

    public static void Play(string name, Transform at = null)
    {
        if (Instance == null) return;
        if (!Instance.CanPlayNow()) return;
        Instance.PlayOneShot(name, at, null);
    }

    public static void PlayAt(string name, Vector3 worldPos)
    {
        if (Instance == null) return;
        if (!Instance.CanPlayNow()) return;
        Instance.PlayOneShot(name, null, worldPos);
    }

    public static void StartLoop(string name, Transform at = null, bool restartIfRunning = false)
    {
        if (Instance == null) return;
        if (!Instance.CanPlayNow()) return;
        Instance.BeginLoop(name, at, null, restartIfRunning);
    }

    public static void StartLoopAt(string name, Vector3 worldPos, bool restartIfRunning = false)
    {
        if (Instance == null) return;
        if (!Instance.CanPlayNow()) return;
        Instance.BeginLoop(name, null, worldPos, restartIfRunning);
    }

    public static void StopLoop(string name, bool graceful = true)
    {
        if (Instance == null) return;
        Instance.EndLoop(name, graceful);
    }

    public static void StopAll(string name)
    {
        if (Instance == null) return;
        Instance.StopAllVoices(name);
    }

    // ============ 본체 구현 ============

    public void PlayOneShot(string name, Transform at, Vector3? worldPosOverride)
    {
        if (!CanPlayNow()) return;
        if (string.IsNullOrEmpty(name) || !_map.TryGetValue(name, out var def)) return;

        var rt = _run[name];
        double now = AudioSettings.dspTime;

        // RetriggerWithCooldown: 트리거 쿨다운
        if (def.loopMode == LoopMode.RetriggerWithCooldown && (now - rt.lastTriggerDsp) < def.cooldown)
            return;

        // 어떤 세트를 쓸지(서브 모드)
        if (def.subMode == SubMode.Simultaneous && def.enableSub && def.subClips.Count > 0)
        {
            // 동시에
            PlayClipOnce(def, useSub: false, when: now, at: at, worldPosOverride: worldPosOverride);
            double when2 = def.subDelay > 0f ? now + def.subDelay : now;
            PlayClipOnce(def, useSub: true, when: when2, at: at, worldPosOverride: worldPosOverride);
            _run[name].lastTriggerDsp = now;
            return;
        }

        bool useSub = false;
        double when = now;

        if (def.subMode == SubMode.Alternate && def.enableSub && def.subClips.Count > 0)
        {
            useSub = _run[name].nextIsSub;
            _run[name].nextIsSub = !useSub;
            if (def.subDelay > 0f) when += (useSub ? 0f : def.subDelay);
        }

        PlayClipOnce(def, useSub, when, at, worldPosOverride);
        _run[name].lastTriggerDsp = now;
    }

    public void BeginLoop(string name, Transform at, Vector3? worldPosOverride, bool restartIfRunning)
    {
        if (!CanPlayNow()) return;
        if (string.IsNullOrEmpty(name) || !_map.TryGetValue(name, out var def)) return;
        if (!_run.TryGetValue(name, out var rt)) return;

        if (rt.looping && !restartIfRunning) return;
        if (rt.loopCo != null) { StopCoroutine(rt.loopCo); rt.loopCo = null; }

        rt.looping = true;
        rt.loopCo = StartCoroutine(CoLoop(def, name, at, worldPosOverride));
    }

    public void EndLoop(string name, bool graceful)
    {
        if (string.IsNullOrEmpty(name) || !_map.TryGetValue(name, out var def)) return;
        if (!_run.TryGetValue(name, out var rt)) return;

        rt.looping = false;
        if (rt.loopCo != null) { StopCoroutine(rt.loopCo); rt.loopCo = null; }

        if (!def.gracefulStopLoop)
            graceful = false;

        if (!graceful)
            StopAllVoices(name); // 즉시 강제 정지
        // graceful 이면 현재 보이스는 끝까지 둔다(자동 해제)
    }

    public void StopAllVoices(string name)
    {
        for (int i = 0; i < _pool.Count; i++)
        {
            var v = _pool[i];
            if (!v.inUse || v.soundName != name) continue;
            ReleaseVoice(v);
        }
    }

    // ============ 내부 동작 ============

    private System.Collections.IEnumerator CoLoop(SoundDef def, string name, Transform at, Vector3? worldPosOverride)
    {
        var rt = _run[name];

        while (rt.looping)
        {
            double now = AudioSettings.dspTime;

            if (def.subMode == SubMode.Simultaneous && def.enableSub && def.subClips.Count > 0)
            {
                // 동시에
                PlayClipOnce(def, useSub: false, when: now, at: at, worldPosOverride: worldPosOverride);
                double when2 = def.subDelay > 0f ? now + def.subDelay : now;
                PlayClipOnce(def, useSub: true, when: when2, at: at, worldPosOverride: worldPosOverride);
            }
            else
            {
                // 교차/없음
                bool useSub = (def.subMode == SubMode.Alternate && def.enableSub && def.subClips.Count > 0)
                              ? rt.nextIsSub : false;
                PlayClipOnce(def, useSub, now, at, worldPosOverride);
                if (def.subMode == SubMode.Alternate && def.enableSub && def.subClips.Count > 0)
                    rt.nextIsSub = !rt.nextIsSub;
            }

            // 다음 트리거까지 대기 (쿨다운 간격)
            float wait = Mathf.Max(0.01f, def.cooldown);
            if (def.loopMode == LoopMode.Continuous)
            {
                // 재생 구간 길이만큼은 최소 보장
                var dur = GetPlayDuration(def);
                wait = Mathf.Max(wait, dur);
            }

            float t = 0f;
            while (rt.looping && t < wait) { t += Time.unscaledDeltaTime; yield return null; }
        }
    }

    private void PlayClipOnce(SoundDef def, bool useSub, double when, Transform at, Vector3? worldPosOverride)
    {

        var clips = (!useSub) ? def.mainClips : def.subClips;
        if (clips == null || clips.Count == 0) return;

        var clip = clips[UnityEngine.Random.Range(0, clips.Count)];
        if (!clip) return;

        if (clips == null || clips.Count == 0)
        {
            Debug.LogWarning($"[SFX] '{def.name}'에 재생할 클립이 없습니다.");
            return;
        }

        float start = Mathf.Clamp(def.startAt, 0f, Mathf.Max(0f, clip.length - 0.0001f));
        float end = (def.endAt > start + 0.0001f) ? Mathf.Min(def.endAt, clip.length) : clip.length;
        float playDur = Mathf.Max(0.0001f, end - start);

        float pitch = Mathf.Max(0.001f, def.pitch + UnityEngine.Random.Range(-def.pitchRandom, def.pitchRandom));
        float volume = Mathf.Clamp01(def.volume + UnityEngine.Random.Range(-def.volumeRandom, def.volumeRandom));

        var v = AcquireVoice(def, def.name);
        if (v == null) return;

        // 위치/팔로우 (부모로 붙이지 않고 팔로우만 한다)
        v.anchor = at ? at : def.defaultAnchor;
        v.follow = def.followTarget && (v.anchor != null);
        var pos = worldPosOverride ?? (v.anchor ? v.anchor.position : Vector3.zero);
        v.src.transform.position = pos;

        // 오디오 소스 파라미터
        var s = v.src;
        s.outputAudioMixerGroup = def.outputMixerGroup;
        s.clip = clip;
        s.priority = def.priority;
        s.volume = volume;
        s.pitch = pitch;
        s.spatialBlend = def.spatialBlend;
        s.rolloffMode = def.rolloff;
        s.minDistance = def.minDistance;
        s.maxDistance = def.maxDistance;
        s.dopplerLevel = def.dopplerLevel;
        s.spread = def.spread;
        s.loop = false;

        // 시작/종료 스케줄 (DSP)
        s.time = start; // 시작 오프셋
        double now = AudioSettings.dspTime;
        if (when > now + 0.0005) s.PlayScheduled(when);
        else s.Play(); // 즉시
                       // SoundManager.PlayClipOnce(...)의 디버그용 계산
        var lp = AudioRuntime.Listener
            ? AudioRuntime.Listener.position
            : (Camera.main ? Camera.main.transform.position : Vector3.zero);

        float dist = Vector3.Distance(lp, s.transform.position);
        Debug.Log($"[SFX] {def.name} '{s.clip?.name}' vol={s.volume:0.##}, spatial={s.spatialBlend:0.##}, min={s.minDistance}, max={s.maxDistance}, dist={dist:0.0}, mixer={(s.outputAudioMixerGroup ? s.outputAudioMixerGroup.name : "None")}");


        double startDsp = (when > 0 ? when : now);
        double endDsp = startDsp + (playDur / Mathf.Max(0.001f, pitch));
        s.SetScheduledEndTime(endDsp);

        v.scheduledEnd = endDsp;
    }

    private float GetPlayDuration(SoundDef def)
    {
        // 평균 길이 대신 첫 클립 기준으로 대충 계산
        AudioClip c = null;
        if (def.mainClips != null && def.mainClips.Count > 0) c = def.mainClips[0];
        if (!c) return def.cooldown;

        float start = Mathf.Clamp(def.startAt, 0f, Mathf.Max(0f, c.length - 0.0001f));
        float end = (def.endAt > start + 0.0001f) ? Mathf.Min(def.endAt, c.length) : c.length;
        float dur = Mathf.Max(0.0001f, end - start);
        float pitch = Mathf.Max(0.001f, def.pitch);
        return dur / pitch;
    }

    // ============ 에디터/런타임 유틸 ============

    [ContextMenu("Rebuild Map")]
    private void RebuildMapContext() => BuildMap();

    // (옵션) 런타임에 사운드 등록/수정이 필요하면 여기에 API 추가하세요.
}
