using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

[DisallowMultipleComponent]
public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

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
        [Min(0f)] public float startAt = 0f;
        [Min(0f)] public float endAt = 0f;

        [Header("Volume / Pitch")]
        [Range(0f, 1f)] public float volume = 1f;
        [Range(0f, 3f)] public float pitch = 1f;
        [Range(0f, 1f)] public float volumeRandom = 0f;
        [Range(0f, 1f)] public float pitchRandom = 0f;

        [Header("Space / Follow")]
        [Range(0f, 1f)] public float spatialBlend = 0f; // 0=2D, 1=3D
        public bool followTarget = true;
        public AudioRolloffMode rolloff = AudioRolloffMode.Logarithmic;
        public float minDistance = 1f;
        public float maxDistance = 20f;
        [Range(0f, 5f)] public float dopplerLevel = 0f;
        [Range(0f, 360f)] public float spread = 0f;

        [Header("Loop / Cooldown")]
        public LoopMode loopMode = LoopMode.None;
        [Min(0f)] public float cooldown = 0.2f;

        [Tooltip("StopLoop 호출 시 부드럽게 끌지(=바로 끊지) 여부")]
        public bool gracefulStopLoop = true;

        [Tooltip("StopLoop(graceful) 시 마지막 배치(틱)만 남기고 그 이전 보이스는 즉시 종료")]
        public bool gracefulStopOnlyLatest = false;

        [Header("Sub Sound Mode")]
        public SubMode subMode = SubMode.None;
        public bool startWithSub = false;
        [Min(0f)] public float subDelay = 0f;

        [Header("Polyphony / Mixer")]
        [Min(1)] public int maxVoices = 8;
        public bool stealOldestOnLimit = true;
        public AudioMixerGroup outputMixerGroup;
        [Range(0, 256)] public int priority = 128;

        [Header("Advanced Anchors")]
        [Tooltip("위치가 필요할 때 기본으로 따라붙을 Transform (없으면 2D로 강등)")]
        public Transform defaultAnchor;

        [Header("FX")]
        [Tooltip("클립 끝에서 자동으로 볼륨을 서서히 0으로 (초). 0=끄기")]
        [Min(0f)] public float tailFadeSeconds = 0.08f;

        [Header("Camera Gate")]
        [Tooltip("카메라 시야(뷰포트) 안에 있을 때만 재생(시작 검사)")]
        public bool requireInCamera = false;
        [Tooltip("재생 중 시야 밖으로 나가면 즉시 끊기(=true) / 그대로 두기(=false)")]
        public bool stopIfLeaveCamera = false;
        [Tooltip("뷰포트 패딩(0~0.5). 0이면 화면 딱 맞춤")]
        [Range(0f, 0.5f)] public float cameraViewportPadding = 0.05f;

        [Tooltip("이 사운드는 이 카메라로만 시야 판정(지정 시 전역 카메라 무시)")]
        public Camera overrideCamera;

        [Header("Auto Start")]
        [Tooltip("게임 시작 시 자동 재생")]
        public bool playOnStart = false;
        [Tooltip("AutoStart 시 루프 사용(LoopMode가 None이 아니어야 의미 있음)")]
        public bool useLoopOnStart = true;
        [Tooltip("AutoStart가 루프를 다시 시작하도록 강제")]
        public bool restartIfRunningOnStart = false;
        [Tooltip("AutoStart 지연(초)")]
        [Min(0f)] public float autoStartDelay = 0f;
    }

    // ---------- Runtime ----------
    private class Voice
    {
        public AudioSource src;
        public Transform anchor;
        public bool follow;
        public string soundName;
        public double scheduledEnd;  // dspTime
        public bool inUse;
        public int batchId = -1;     // 어느 루프 틱(배치)에서 시작됐는지
        public Coroutine fadeCo;     // 꼬리 페이드 코루틴
    }

    private class SoundRuntime
    {
        public bool looping;
        public bool nextIsSub;
        public double lastTriggerDsp;
        public Coroutine loopCo;

        public int lastBatchId = 0;  // 마지막으로 트리거된 배치 번호
    }

    [Header("Library")]
    [SerializeField] private List<SoundDef> sounds = new List<SoundDef>();

    [Header("Pool")]
    [SerializeField, Min(0)] private int prewarmVoices = 8;

    [Header("Cameras (전역 등록)")]
    [Tooltip("여기에 등록된 카메라들 중 하나라도 보이면 ‘requireInCamera’ 조건을 만족합니다.")]
    [SerializeField] private List<Camera> registeredCameras = new List<Camera>();

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    private readonly Dictionary<string, SoundDef> _map = new();
    private readonly Dictionary<string, SoundRuntime> _run = new();
    private readonly List<Voice> _pool = new();

    private Camera _cachedMainCam;

    // ---------- Lifecycle ----------
    private void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        BuildMap();
        for (int i = 0; i < prewarmVoices; i++) _pool.Add(CreateVoice());

#if UNITY_EDITOR
        AudioListener.pause = false;
        AudioListener.volume = 1f;
#endif
    }

    private void Start()
    {
        // Auto Start
        foreach (var def in sounds)
        {
            if (!def.playOnStart || string.IsNullOrEmpty(def.name)) continue;
            if (def.useLoopOnStart && def.loopMode != LoopMode.None)
            {
                if (def.autoStartDelay <= 0f)
                    BeginLoop(def.name, def.defaultAnchor, null, def.restartIfRunningOnStart);
                else
                    StartCoroutine(CoDelay(() => BeginLoop(def.name, def.defaultAnchor, null, def.restartIfRunningOnStart), def.autoStartDelay));
            }
            else
            {
                if (def.autoStartDelay <= 0f)
                    PlayOneShot(def.name, def.defaultAnchor, null);
                else
                    StartCoroutine(CoDelay(() => PlayOneShot(def.name, def.defaultAnchor, null), def.autoStartDelay));
            }
        }
    }

    private System.Collections.IEnumerator CoDelay(Action act, float sec)
    {
        yield return new WaitForSeconds(sec);
        act?.Invoke();
    }

    private void LateUpdate()
    {
#if UNITY_EDITOR
        if (AudioListener.pause) AudioListener.pause = false;
        if (AudioListener.volume <= 0.0001f) AudioListener.volume = 1f;
#endif
        double now = AudioSettings.dspTime;

        for (int i = _pool.Count - 1; i >= 0; i--)
        {
            var v = _pool[i];
            if (!v.inUse) continue;

            // 팔로우
            if (v.follow && v.anchor)
                v.src.transform.position = v.anchor.position;

            // 카메라 게이트: 재생 중 이탈 시 강제 종료 옵션
            if (_map.TryGetValue(v.soundName, out var def) && def.requireInCamera && def.stopIfLeaveCamera)
            {
                // 위치가 없으면(2D 무위치) 이 옵션은 적용 불가 → 스킵
                Vector3 checkPos;
                bool hasPos = (v.anchor != null);
                checkPos = hasPos ? v.anchor.position : v.src.transform.position;

                if (hasPos && !IsInAnyCameraView(GetCamerasFor(def), checkPos, def.cameraViewportPadding))
                    ReleaseVoice(v);
            }

            // 자연 종료
            if (!v.src.isPlaying && now >= v.scheduledEnd - 0.001)
                ReleaseVoice(v);
        }
    }

    // ---------- Build / Pool ----------
    private void BuildMap()
    {
        _map.Clear(); _run.Clear();
        foreach (var s in sounds)
        {
            if (string.IsNullOrWhiteSpace(s.name)) continue;
            if (!_map.ContainsKey(s.name)) _map.Add(s.name, s);
            if (!_run.ContainsKey(s.name))
                _run.Add(s.name, new SoundRuntime { nextIsSub = s.startWithSub, lastTriggerDsp = -9999 });
        }
    }

    private Voice CreateVoice()
    {
        var go = new GameObject("SFXVoice");
        go.transform.SetParent(transform, false);

        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = false;
        src.spatialBlend = 0f;
        src.rolloffMode = AudioRolloffMode.Logarithmic;
        src.minDistance = 1f;
        src.maxDistance = 20f;
        src.dopplerLevel = 0f;
        src.spread = 0f;
        src.priority = 128;

        return new Voice { src = src, inUse = false };
    }

    private Voice AcquireVoice(SoundDef def, string name)
    {
        int activeCount = 0;
        for (int i = 0; i < _pool.Count; i++)
            if (_pool[i].inUse && _pool[i].soundName == name) activeCount++;

        if (activeCount >= def.maxVoices)
        {
            if (!def.stealOldestOnLimit) return null;

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

        for (int i = 0; i < _pool.Count; i++)
            if (!_pool[i].inUse)
            {
                var v = _pool[i];
                v.inUse = true;
                v.soundName = name;
                return v;
            }

        var nv = CreateVoice();
        nv.inUse = true;
        nv.soundName = name;
        _pool.Add(nv);
        return nv;
    }

    private void StopFade(Voice v)
    {
        if (v.fadeCo != null)
        {
            StopCoroutine(v.fadeCo);
            v.fadeCo = null;
        }
    }

    private void ReleaseVoice(Voice v)
    {
        if (!v.inUse) return;
        StopFade(v);
        v.inUse = false;
        v.src.Stop();
        v.src.volume = 1f;   // 리셋
        v.src.clip = null;
        v.anchor = null;
        v.follow = false;
        v.soundName = null;
        v.scheduledEnd = 0;
        v.batchId = -1;
    }

    // ---------- Public API ----------
    public static void Play(string name, Transform at = null) => Instance?.PlayOneShot(name, at, null);
    public static void PlayAt(string name, Vector3 worldPos) => Instance?.PlayOneShot(name, null, worldPos);
    public static void StartLoop(string name, Transform at = null, bool restartIfRunning = false)
        => Instance?.BeginLoop(name, at, null, restartIfRunning);
    public static void StartLoopAt(string name, Vector3 worldPos, bool restartIfRunning = false)
        => Instance?.BeginLoop(name, null, worldPos, restartIfRunning);
    public static void StopLoop(string name, bool graceful = true) => Instance?.EndLoop(name, graceful);
    public static void StopAll(string name) => Instance?.StopAllVoices(name);

    // --- 카메라 등록/설정 ---
    public static void RegisterCamera(Camera cam, bool makePrimary = false)
    {
        if (!Instance || !cam) return;
        if (!Instance.registeredCameras.Contains(cam))
            Instance.registeredCameras.Add(cam);
        if (makePrimary)
        {
            Instance.registeredCameras.Remove(cam);
            Instance.registeredCameras.Insert(0, cam);
        }
    }

    public static void UnregisterCamera(Camera cam)
    {
        if (!Instance || !cam) return;
        Instance.registeredCameras.Remove(cam);
    }

    public static void SetCameras(IEnumerable<Camera> cams)
    {
        if (!Instance) return;
        Instance.registeredCameras.Clear();
        if (cams == null) return;
        foreach (var c in cams) if (c) Instance.registeredCameras.Add(c);
    }

    // ---------- Core ----------
    public void PlayOneShot(string name, Transform at, Vector3? worldPosOverride)
    {
        if (string.IsNullOrEmpty(name) || !_map.TryGetValue(name, out var def)) return;

        var rt = _run[name];
        double now = AudioSettings.dspTime;

        if (def.loopMode == LoopMode.RetriggerWithCooldown && (now - rt.lastTriggerDsp) < def.cooldown)
            return;

        bool useSub = false;
        double when = now;

        if (def.subMode == SubMode.Alternate && def.enableSub && def.subClips.Count > 0)
        {
            useSub = rt.nextIsSub;
            rt.nextIsSub = !rt.nextIsSub;
            if (def.subDelay > 0f) when += (useSub ? 0f : def.subDelay);
        }
        else if (def.subMode == SubMode.Simultaneous && def.enableSub && def.subClips.Count > 0)
        {
            PlayClipOnce(def, false, now, at, worldPosOverride, -1);
            double when2 = def.subDelay > 0f ? now + def.subDelay : now;
            PlayClipOnce(def, true, when2, at, worldPosOverride, -1);
            rt.lastTriggerDsp = now;
            return;
        }

        PlayClipOnce(def, useSub, when, at, worldPosOverride, -1);
        rt.lastTriggerDsp = now;
    }

    public void BeginLoop(string name, Transform at, Vector3? worldPosOverride, bool restartIfRunning)
    {
        if (string.IsNullOrEmpty(name) || !_map.TryGetValue(name, out var def)) return;
        var rt = _run[name];

        if (rt.looping && !restartIfRunning) return;
        if (rt.loopCo != null) { StopCoroutine(rt.loopCo); rt.loopCo = null; }

        rt.looping = true;
        rt.loopCo = StartCoroutine(CoLoop(def, name, at, worldPosOverride));
    }

    public void EndLoop(string name, bool graceful)
    {
        if (string.IsNullOrEmpty(name) || !_map.TryGetValue(name, out var def)) return;
        var rt = _run[name];

        rt.looping = false;
        if (rt.loopCo != null) { StopCoroutine(rt.loopCo); rt.loopCo = null; }

        if (!def.gracefulStopLoop) graceful = false;

        if (!graceful)
        {
            StopAllVoices(name);
            return;
        }

        // 마지막 배치만 남기고 이전은 즉시 끊기
        if (def.gracefulStopOnlyLatest)
        {
            int keepBatch = rt.lastBatchId;
            for (int i = 0; i < _pool.Count; i++)
            {
                var v = _pool[i];
                if (!v.inUse || v.soundName != name) continue;
                if (v.batchId != keepBatch) ReleaseVoice(v);
            }
        }
        // 나머지는 자연 종료
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

    private System.Collections.IEnumerator CoLoop(SoundDef def, string name, Transform at, Vector3? worldPosOverride)
    {
        var rt = _run[name];

        while (rt.looping)
        {
            double now = AudioSettings.dspTime;

            // 쿨다운 게이트
            if (def.loopMode == LoopMode.RetriggerWithCooldown)
            {
                double nextAllowed = rt.lastTriggerDsp + Math.Max(0.0001, def.cooldown);
                while (rt.looping && AudioSettings.dspTime < nextAllowed)
                    yield return null;
                if (!rt.looping) yield break;
            }

            now = AudioSettings.dspTime;

            // 배치 증가(이번 틱에서 시작되는 보이스 공통 batchId)
            rt.lastBatchId++;

            if (def.subMode == SubMode.Simultaneous && def.enableSub && def.subClips.Count > 0)
            {
                PlayClipOnce(def, false, now, at, worldPosOverride, rt.lastBatchId);
                double when2 = def.subDelay > 0f ? now + def.subDelay : now;
                PlayClipOnce(def, true, when2, at, worldPosOverride, rt.lastBatchId);
            }
            else
            {
                bool useSub = (def.subMode == SubMode.Alternate && def.enableSub && def.subClips.Count > 0) ? rt.nextIsSub : false;
                PlayClipOnce(def, useSub, now, at, worldPosOverride, rt.lastBatchId);
                if (def.subMode == SubMode.Alternate && def.enableSub && def.subClips.Count > 0)
                    rt.nextIsSub = !rt.nextIsSub;
            }

            rt.lastTriggerDsp = AudioSettings.dspTime;

            float wait = Mathf.Max(0.01f, def.cooldown);
            if (def.loopMode == LoopMode.Continuous) wait = Mathf.Max(wait, GetPlayDuration(def));

            float t = 0f;
            while (rt.looping && t < wait) { t += Time.unscaledDeltaTime; yield return null; }
        }
    }

    private void PlayClipOnce(SoundDef def, bool useSub, double when, Transform at, Vector3? worldPosOverride, int batchId)
    {
        var clips = (!useSub) ? def.mainClips : def.subClips;
        if (clips == null || clips.Count == 0) return;

        var clip = clips[UnityEngine.Random.Range(0, clips.Count)];
        if (!clip) return;

        // 위치 계산 먼저 (카메라 게이트 확인 때문에)
        Transform anchor = at ? at : def.defaultAnchor;
        bool hasPos = worldPosOverride.HasValue || anchor != null;
        Vector3 pos = worldPosOverride ?? (anchor ? anchor.position : Vector3.zero);

        // 카메라 게이트: 시작 시점에 화면 밖이면 재생 스킵 (위치 없으면 스킵하지 않음)
        if (def.requireInCamera && hasPos)
        {
            if (!IsInAnyCameraView(GetCamerasFor(def), pos, def.cameraViewportPadding))
            {
                if (debugLogs) Debug.Log($"[SoundManager] Skip '{def.name}' (out of camera).");
                return;
            }
        }

        float start = Mathf.Clamp(def.startAt, 0f, Mathf.Max(0f, clip.length - 0.0001f));
        float end = (def.endAt > start + 0.0001f) ? Mathf.Min(def.endAt, clip.length) : clip.length;
        float playDur = Mathf.Max(0.0001f, end - start);

        float pitch = Mathf.Max(0.001f, def.pitch + UnityEngine.Random.Range(-def.pitchRandom, def.pitchRandom));
        float volume = Mathf.Clamp01(def.volume + UnityEngine.Random.Range(-def.volumeRandom, def.volumeRandom));

        var v = AcquireVoice(def, def.name);
        if (v == null) return;

        // 위치/팔로우
        v.anchor = anchor;
        v.follow = def.followTarget && (v.anchor != null);
        v.src.transform.position = pos;

        // 공통 파라미터
        var s = v.src;
        s.outputAudioMixerGroup = def.outputMixerGroup;
        s.priority = def.priority;

        // 3D 요청인데 앵커/좌표 없으면 2D로 강등
        bool want3D = def.spatialBlend > 0.001f;
        bool haveAnchor = hasPos;
        float spatial = (want3D && haveAnchor) ? def.spatialBlend : 0f;

        s.spatialBlend = spatial;
        s.rolloffMode = def.rolloff;
        s.minDistance = def.minDistance;
        s.maxDistance = def.maxDistance;
        s.dopplerLevel = def.dopplerLevel;
        s.spread = def.spread;
        s.loop = false;
        s.ignoreListenerPause = true;

        if (want3D && !haveAnchor)
            Debug.LogWarning($"[SoundManager] '{def.name}' 3D이지만 위치가 없습니다. 2D로 재생합니다.");

        StopFade(v);            // 재사용 시 이전 페이드 중지
        v.batchId = batchId;    // 배치 기록

        bool fullSpan = (Mathf.Approximately(start, 0f) && Mathf.Abs(end - clip.length) < 0.0001f);
        bool allowOneShot = (spatial <= 0.001f) && fullSpan && def.tailFadeSeconds <= 0f;

        if (allowOneShot)
        {
            s.volume = 1f; // PlayOneShot의 volumeScale 사용
            s.PlayOneShot(clip, volume);
            v.scheduledEnd = AudioSettings.dspTime + (playDur / pitch);
            return;
        }

        s.clip = clip;
        s.pitch = pitch;
        s.volume = volume;

        s.time = start;
        if (when > AudioSettings.dspTime + 0.0005) s.PlayScheduled(when);
        else s.Play();

        double startDsp = (when > 0 ? when : AudioSettings.dspTime);
        double endDsp = startDsp + (playDur / pitch);

        // 부분 재생 또는 3D면 DSP로 끝 잘라주기
        if (!fullSpan || spatial > 0.001f)
            s.SetScheduledEndTime(endDsp);

        v.scheduledEnd = endDsp;

        // 꼬리 페이드
        if (def.tailFadeSeconds > 0f)
            v.fadeCo = StartCoroutine(CoFadeOutAt(v, def.tailFadeSeconds));
    }

    private System.Collections.IEnumerator CoFadeOutAt(Voice v, float fadeSec)
    {
        var s = v.src;
        double startAt = v.scheduledEnd - fadeSec;

        while (v.inUse && AudioSettings.dspTime < startAt)
            yield return null;

        float t = 0f;
        float startVol = s.volume;
        while (v.inUse && t < fadeSec)
        {
            t += Time.unscaledDeltaTime;
            float k = 1f - Mathf.Clamp01(t / fadeSec);
            s.volume = startVol * k;
            yield return null;
        }
        v.fadeCo = null;
    }

    private float GetPlayDuration(SoundDef def)
    {
        AudioClip c = null;
        if (def.mainClips != null && def.mainClips.Count > 0) c = def.mainClips[0];
        if (!c) return def.cooldown;

        float start = Mathf.Clamp(def.startAt, 0f, Mathf.Max(0f, c.length - 0.0001f));
        float end = (def.endAt > start + 0.0001f) ? Mathf.Min(def.endAt, c.length) : c.length;
        float dur = Mathf.Max(0.0001f, end - start);
        float pitch = Mathf.Max(0.001f, def.pitch);
        return dur / pitch;
    }

    // ---------- Camera Helpers ----------
    private List<Camera> GetCamerasFor(SoundDef def)
    {
        // per-sound override
        if (def.overrideCamera != null)
            return _tmpSingleCamList(def.overrideCamera);

        // global registered
        if (registeredCameras != null && registeredCameras.Count > 0)
            return registeredCameras;

        // fallback: main camera
        var c = GetMainCamera();
        if (c != null) return _tmpSingleCamList(c);

        // no camera → “보인다”로 간주하여 차단하지 않음
        return null;
    }

    private static readonly List<Camera> _oneCamBuffer = new List<Camera>(1);
    private List<Camera> _tmpSingleCamList(Camera c)
    {
        _oneCamBuffer.Clear();
        _oneCamBuffer.Add(c);
        return _oneCamBuffer;
    }

    private Camera GetMainCamera()
    {
        if (_cachedMainCam == null) _cachedMainCam = Camera.main;
        if (_cachedMainCam == null) _cachedMainCam = FindAnyObjectByType<Camera>();
        return _cachedMainCam;
    }

    private static bool IsInAnyCameraView(List<Camera> cams, Vector3 worldPos, float padViewport)
    {
        if (cams == null || cams.Count == 0) return true; // 카메라 없으면 게이트 통과
        for (int i = 0; i < cams.Count; i++)
        {
            var cam = cams[i];
            if (!cam) continue;
            if (IsInCameraView(cam, worldPos, padViewport)) return true;
        }
        return false;
    }

    private static bool IsInCameraView(Camera cam, Vector3 worldPos, float padViewport)
    {
        if (!cam) return true;
        var vp = cam.WorldToViewportPoint(worldPos);
        if (vp.z < 0f) return false; // 카메라 뒤
        float min = -padViewport;
        float max = 1f + padViewport;
        return (vp.x >= min && vp.x <= max && vp.y >= min && vp.y <= max);
    }

    [ContextMenu("Rebuild Map")]
    private void RebuildMapContext() => BuildMap();

#if UNITY_EDITOR
    [ContextMenu("DEBUG Ping Beep")]
    private void DebugPingBeep()
    {
        int hz = 440;
        float sec = 0.2f;
        int sr = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
        int samples = Mathf.CeilToInt(sr * sec);

        var clip = AudioClip.Create("dbg_beep", samples, 1, sr, false);
        var data = new float[samples];
        for (int i = 0; i < samples; i++) data[i] = Mathf.Sin(2f * Mathf.PI * hz * i / sr) * 0.2f;
        clip.SetData(data, 0);

        var go = new GameObject("Beep (temp)");
        var src = go.AddComponent<AudioSource>();
        src.spatialBlend = 0f;
        src.ignoreListenerPause = true;
        src.PlayOneShot(clip, 1f);

        if (Application.isPlaying) Destroy(go, 1f);
        else DestroyImmediate(go);
    }
#endif
}
