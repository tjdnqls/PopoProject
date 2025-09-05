using UnityEngine;

[DisallowMultipleComponent]
public class CameraShaker : MonoBehaviour
{
    public static CameraShaker Instance { get; private set; }
    public static bool Exists => Instance != null;

    [Header("Amplitude")]
    [Tooltip("포지션 흔들림 최대치(월드 단위)")]
    [SerializeField] private float maxPositionShake = 0.6f;
    [Tooltip("Z 회전 흔들림 최대치(도)")]
    [SerializeField] private float maxRotationShake = 4f;

    [Header("Noise")]
    [Tooltip("노이즈 주파수(값이 클수록 빠르게 흔들림)")]
    [SerializeField] private float frequency = 22f;

    [Header("Options")]
    [Tooltip("회전 흔들림을 적용할지")]
    [SerializeField] private bool applyZRotation = false;
    [Tooltip("TimeScale의 영향을 무시할지(=일시정지 중에도 흔들림 진행)")]
    [SerializeField] private bool useUnscaledTime = true;

    // 외부에서 읽어서 카메라 팔로우 결과에 더해 쓰는 값
    public Vector2 CurrentOffset { get; private set; }
    public float CurrentAngleZ { get; private set; }

    // 내부 상태(트라우마 기반)
    private float _trauma;                 // 0~1
    private float _decayPerSec;            // 초당 감소량
    private float _t;                      // 시간 축
    private float _seedX, _seedY, _seedR;  // 노이즈 시드

    void Awake()
    {
        if (Instance && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        _seedX = Random.value * 1000f;
        _seedY = Random.value * 2000f;
        _seedR = Random.value * 3000f;
    }

    /// <summary>
    /// 어디서든 호출: 강도[0~1 권장], 지속시간(초)
    /// </summary>
    public static void Shake(float intensity, float seconds)
    {
        if (!Exists) return;
        intensity = Mathf.Clamp01(intensity);
        seconds = Mathf.Max(0.0001f, seconds);

        // 누적되도록 더하고(1로 클램프), 가장 센 감쇠 속도를 유지
        Instance._trauma = Mathf.Clamp01(Instance._trauma + intensity);
        Instance._decayPerSec = Mathf.Max(Instance._decayPerSec, intensity / seconds);
    }

    // 한국어/별칭 호출도 지원(원하셨던 “카메라 쉐이킹(강도, 초)”) 
    public static void 카메라쉐이킹(float 강도, float 초) => Shake(강도, 초);

    public static void StopShake()
    {
        if (!Exists) return;
        Instance._trauma = 0f;
        Instance.CurrentOffset = Vector2.zero;
        Instance.CurrentAngleZ = 0f;
    }

    void LateUpdate()
    {
        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        float tt = useUnscaledTime ? Time.unscaledTime : Time.time;

        // 트라우마 감소
        if (_trauma > 0f)
        {
            _trauma = Mathf.Max(0f, _trauma - _decayPerSec * dt);
        }

        // 흔들림 계산 (trauma^2로 고주파 억제)
        float amp = _trauma * _trauma;

        if (amp <= 0f)
        {
            CurrentOffset = Vector2.zero;
            CurrentAngleZ = 0f;
            return;
        }

        float nx = Mathf.PerlinNoise(_seedX, tt * frequency) * 2f - 1f;
        float ny = Mathf.PerlinNoise(_seedY, tt * frequency) * 2f - 1f;
        float nr = Mathf.PerlinNoise(_seedR, tt * frequency) * 2f - 1f;

        CurrentOffset = new Vector2(nx, ny) * (maxPositionShake * amp);
        CurrentAngleZ = applyZRotation ? nr * (maxRotationShake * amp) : 0f;
    }
}
