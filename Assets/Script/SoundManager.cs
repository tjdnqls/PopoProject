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

        [Header("Volume/Pitch")]
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
        public bool gracefulStopLoop = true;

        [Header("Sub Sound Mode")]
        public SubMode subMode = SubMode.None;
        public bool startWithSub = false;
        [Min(0f)] public float subDelay = 0f;

        [Header("Polyphony / Mixer")]
        [Min(1)] public int maxVoices = 8;
        public bool stealOldestOnLimit = true;
        public AudioMixerGroup outputMixerGroup;
        [Range(0, 256)] public int priority = 128;

        [Header("Advanced")]
        public Transform defaultAnchor;

        [Header("FX")]
        [Tooltip("클립 끝에서 자동으로 볼륨을 서서히 0으로 (초). 0=끄기")]
        [Min(0f)] public float tailFadeSeconds = 0.08f; // ★ 추가
    }

    // ---------- 런타임 ----------
    private class Voice
    {
        public AudioSource src;
        public Transform anchor;
        public bool follow;
        public string soundName;
        public double scheduledEnd;  // dspTime
        public bool inUse;

        public Coroutine fadeCo;     // ★ 추가: 꼬리 페이드
    }

    private class SoundRuntime
    {
        public bool looping;
        public bool nextIsSub;
        public double lastTriggerDsp;
        public Coroutine loopCo;
        public readonly List<Voice> voices = new List<Voice>();
    }

    [Header("Library")]
    [SerializeField] private List<SoundDef> sounds = new List<SoundDef>();

    [Header("Pool")]
    [SerializeField, Min(0)] private int prewarmVoices = 8;

    private readonly Dictionary<string, SoundDef> _map = new();
    private readonly Dictionary<string, SoundRuntime> _run = new();
    private readonly List<Voice> _pool = new();

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

            if (v.follow && v.anchor)
                v.src.transform.position = v.anchor.position;

            if (!v.src.isPlaying && now >= v.scheduledEnd - 0.001)
                ReleaseVoice(v);
        }
    }

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
    }

    // ---------- 퍼블릭 API ----------
    public static void Play(string name, Transform at = null) => Instance?.PlayOneShot(name, at, null);
    public static void PlayAt(string name, Vector3 worldPos) => Instance?.PlayOneShot(name, null, worldPos);
    public static void StartLoop(string name, Transform at = null, bool restartIfRunning = false)
        => Instance?.BeginLoop(name, at, null, restartIfRunning);
    public static void StartLoopAt(string name, Vector3 worldPos, bool restartIfRunning = false)
        => Instance?.BeginLoop(name, null, worldPos, restartIfRunning);
    public static void StopLoop(string name, bool graceful = true) => Instance?.EndLoop(name, graceful);
    public static void StopAll(string name) => Instance?.StopAllVoices(name);

    // ---------- 본체 ----------
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
            PlayClipOnce(def, false, now, at, worldPosOverride);
            double when2 = def.subDelay > 0f ? now + def.subDelay : now;
            PlayClipOnce(def, true, when2, at, worldPosOverride);
            rt.lastTriggerDsp = now;
            return;
        }

        PlayClipOnce(def, useSub, when, at, worldPosOverride);
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
        if (!graceful) StopAllVoices(name);
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

            // ★ 루프에도 쿨다운 게이트
            if (def.loopMode == LoopMode.RetriggerWithCooldown)
            {
                double nextAllowed = rt.lastTriggerDsp + Math.Max(0.0001f, def.cooldown);
                while (rt.looping && AudioSettings.dspTime < nextAllowed)
                    yield return null;
                if (!rt.looping) yield break;
            }

            now = AudioSettings.dspTime;

            if (def.subMode == SubMode.Simultaneous && def.enableSub && def.subClips.Count > 0)
            {
                PlayClipOnce(def, false, now, at, worldPosOverride);
                double when2 = def.subDelay > 0f ? now + def.subDelay : now;
                PlayClipOnce(def, true, when2, at, worldPosOverride);
            }
            else
            {
                bool useSub = (def.subMode == SubMode.Alternate && def.enableSub && def.subClips.Count > 0) ? rt.nextIsSub : false;
                PlayClipOnce(def, useSub, now, at, worldPosOverride);
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

    private void PlayClipOnce(SoundDef def, bool useSub, double when, Transform at, Vector3? worldPosOverride)
    {
        var clips = (!useSub) ? def.mainClips : def.subClips;
        if (clips == null || clips.Count == 0) return;

        var clip = clips[UnityEngine.Random.Range(0, clips.Count)];
        if (!clip) return;

        float start = Mathf.Clamp(def.startAt, 0f, Mathf.Max(0f, clip.length - 0.0001f));
        float end = (def.endAt > start + 0.0001f) ? Mathf.Min(def.endAt, clip.length) : clip.length;
        float playDur = Mathf.Max(0.0001f, end - start);

        float pitch = Mathf.Max(0.001f, def.pitch + UnityEngine.Random.Range(-def.pitchRandom, def.pitchRandom));
        float volume = Mathf.Clamp01(def.volume + UnityEngine.Random.Range(-def.volumeRandom, def.volumeRandom));

        var v = AcquireVoice(def, def.name);
        if (v == null) return;

        // 위치/팔로우
        v.anchor = at ? at : def.defaultAnchor;
        v.follow = def.followTarget && (v.anchor != null);
        var pos = worldPosOverride ?? (v.anchor ? v.anchor.position : Vector3.zero);
        v.src.transform.position = pos;

        // 공통 파라미터
        var s = v.src;
        s.outputAudioMixerGroup = def.outputMixerGroup;
        s.priority = def.priority;
        s.spatialBlend = def.spatialBlend;
        s.rolloffMode = def.rolloff;
        s.minDistance = def.minDistance;
        s.maxDistance = def.maxDistance;
        s.dopplerLevel = def.dopplerLevel;
        s.spread = def.spread;
        s.loop = false;
        s.ignoreListenerPause = true;

        StopFade(v); // 재사용 시 이전 페이드 중지

        bool fullSpan = (Mathf.Approximately(start, 0f) && Mathf.Abs(end - clip.length) < 0.0001f);
        bool allowOneShot = Mathf.Approximately(def.spatialBlend, 0f) && fullSpan && def.tailFadeSeconds <= 0f; // ★ 페이드가 있으면 일반 재생

        if (allowOneShot)
        {
            // 간단 경로
            s.volume = 1f; // PlayOneShot의 volumeScale로 적용
            s.PlayOneShot(clip, volume);
            v.scheduledEnd = AudioSettings.dspTime + (playDur / pitch);
            return;
        }

        // 일반 경로(페이드 지원)
        s.clip = clip;
        s.pitch = pitch;
        s.volume = volume;

        s.time = start;
        if (when > AudioSettings.dspTime + 0.0005) s.PlayScheduled(when);
        else s.Play();

        double startDsp = (when > 0 ? when : AudioSettings.dspTime);
        double endDsp = startDsp + (playDur / pitch);

        // 부분 재생이면 DSP로 끝 잘라주기
        if (!fullSpan || !Mathf.Approximately(def.spatialBlend, 0f))
            s.SetScheduledEndTime(endDsp);

        v.scheduledEnd = endDsp;

        // ★ 꼬리 페이드 스케줄
        if (def.tailFadeSeconds > 0f)
        {
            v.fadeCo = StartCoroutine(CoFadeOutAt(v, def.tailFadeSeconds));
        }
    }

    private System.Collections.IEnumerator CoFadeOutAt(Voice v, float fadeSec)
    {
        var s = v.src;
        double startAt = v.scheduledEnd - fadeSec;

        // 페이드 시작 시점까지 대기
        while (v.inUse && AudioSettings.dspTime < startAt)
            yield return null;

        // 페이드
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
