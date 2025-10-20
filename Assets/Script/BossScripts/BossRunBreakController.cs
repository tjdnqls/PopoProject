using System.Collections;
using UnityEngine;

/// <summary>
/// Keypad8: Idle 즉시 종료 → Run 재생 → 우측 돌진.
/// 돌진 중 BreakSensor에서 Ground 레이어 충돌을 알려오면 해당 오브젝트 파괴 + 이펙트 생성.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteAnimationManager))]
[RequireComponent(typeof(Rigidbody2D))]
public class BossRunBreakController : MonoBehaviour
{
    [Header("Anim Names")]
    [SerializeField] private string idleName = "Idle";
    [SerializeField] private string runName = "Run";

    [Header("Input")]
    [SerializeField] private KeyCode runKey = KeyCode.Keypad8;

    [Header("Run Motion")]
    [Tooltip("돌진 속도(우측 +X)")]
    [Min(0f)] public float runSpeed = 8f;
    [Tooltip("돌진 지속 시간(초)")]
    [Min(0.05f)] public float runDuration = 1.2f;
    [Tooltip("돌진 중 중력 영향 차단 여부")]
    public bool freezeGravityDuringRun = true;
    
    [Header("Movement Smoothing")]
    [Tooltip("이동 시 가속도 (더 부드러운 이동)")]
    [Min(0f)] public float acceleration = 30f;
    [Tooltip("이동 중단 시 감속도")]
    [Min(0f)] public float deceleration = 20f;
    [Tooltip("최소 속도 임계값 (이하에서 완전 정지)")]
    [Min(0f)] public float minSpeedThreshold = 0.1f;

    [Header("Layers (comma-separated names)")]
    [Tooltip("부수기 대상으로 인식할 레이어 이름 목록(예: \"Ground, EventGround\")")]
    [SerializeField] private string breakableLayerNames = "Ground";

    [Header("VFX")]
    [Tooltip("산산조각 이펙트 프리팹(선택). 없으면 런타임 파티클 생성")]
    public GameObject breakVfxPrefab;
    [Tooltip("이펙트 생존 시간(프리팹 미지정 시에만 사용)")]
    public float fallbackVfxLifetime = 1.2f;

    private SpriteAnimationManager _anim;
    private Rigidbody2D _rb;
    private LayerMask _breakableMask;
    private bool _isRunning;
    private float _savedGravityScale;
    private float _currentSpeed; // 현재 속도
    private bool _isExternalTriggered; // 외부 호출 여부

    void Awake()
    {
        _anim = GetComponent<SpriteAnimationManager>();
        _rb = GetComponent<Rigidbody2D>();
        _breakableMask = NamesToMask(breakableLayerNames);
    }

    void Start()
    {
        // 시작 시 Idle 보장
        if (_anim.HasClip(idleName))
            _anim.Play(idleName, forceRestart: true, interruptOneShot: true);

        // BreakSensor가 있다면 콜백 구독
        foreach (var s in GetComponentsInChildren<BreakSensor>(true))
            s.Setup(this, _breakableMask);
    }

    void Update()
    {
        if (Input.GetKeyDown(runKey))
            TryRunRight();
            
        // 디버그 정보 표시 (선택적)
        if (Application.isEditor && _isRunning)
        {
            Debug.Log($"[BossRunBreak] {GetDebugInfo()}");
        }
    }

    public void TryRunRight()
    {
        if (_isRunning) return;
        _isExternalTriggered = false; // 수동 입력
        StartRun();
    }
    
    /// <summary>
    /// 외부 스크립트에서 호출할 수 있는 달리기 트리거 함수
    /// </summary>
    public void TriggerRun()
    {
        if (_isRunning) return;
        _isExternalTriggered = true; // 외부 호출
        StartRun();
    }
    
    /// <summary>
    /// 달리기 중단 (외부에서 호출 가능)
    /// </summary>
    public void StopRun()
    {
        if (!_isRunning) return;
        StopAllCoroutines();
        StartCoroutine(StopRunRoutine());
    }
    
    /// <summary>
    /// 현재 달리기 상태 확인
    /// </summary>
    public bool IsRunning => _isRunning;
    
    /// <summary>
    /// 현재 속도 확인
    /// </summary>
    public float CurrentSpeed => _currentSpeed;
    
    private void StartRun()
    {
        // Idle 즉시 종료 후 Run 재생
        if (_anim.HasClip(runName))
            _anim.Play(runName, forceRestart: true, interruptOneShot: true);

        StartCoroutine(RunRoutine());
    }

    private IEnumerator RunRoutine()
    {
        _isRunning = true;
        _currentSpeed = 0f;

        // 중력 정지(선택)
        if (freezeGravityDuringRun)
        {
            _savedGravityScale = _rb.gravityScale;
            _rb.gravityScale = 0f;
        }

        float t = 0f;
        
        // 부드러운 가속 단계
        while (t < runDuration && _currentSpeed < runSpeed)
        {
            t += Time.deltaTime;
            
            // 부드러운 가속
            _currentSpeed = Mathf.MoveTowards(_currentSpeed, runSpeed, acceleration * Time.deltaTime);
            
            var v = _rb.linearVelocity;
            v.x = _currentSpeed;
            if (!freezeGravityDuringRun) v.y = v.y; // 그대로 유지
            else v.y = 0f;
            _rb.linearVelocity = v;
            
            yield return null;
        }
        
        // 최대 속도 유지 단계
        while (t < runDuration)
        {
            t += Time.deltaTime;
            
            var v = _rb.linearVelocity;
            v.x = runSpeed; // 일정한 속도 유지
            if (!freezeGravityDuringRun) v.y = v.y;
            else v.y = 0f;
            _rb.linearVelocity = v;
            
            yield return null;
        }

        // 달리기 종료 후 감속
        yield return StartCoroutine(StopRunRoutine());
    }
    
    private IEnumerator StopRunRoutine()
    {
        // 부드러운 감속
        while (_currentSpeed > minSpeedThreshold)
        {
            _currentSpeed = Mathf.MoveTowards(_currentSpeed, 0f, deceleration * Time.deltaTime);
            
            var v = _rb.linearVelocity;
            v.x = _currentSpeed;
            if (!freezeGravityDuringRun) v.y = v.y;
            else v.y = 0f;
            _rb.linearVelocity = v;
            
            yield return null;
        }
        
        // 완전 정지
        _currentSpeed = 0f;
        _rb.linearVelocity = new Vector2(0f, freezeGravityDuringRun ? 0f : _rb.linearVelocity.y);
        
        // 중력 복원
        if (freezeGravityDuringRun) 
            _rb.gravityScale = _savedGravityScale;

        // Idle 복귀
        if (_anim.HasClip(idleName))
            _anim.Play(idleName, forceRestart: true, interruptOneShot: true);

        _isRunning = false;
    }

    /// <summary>
    /// BreakSensor에서 호출. Ground 레이어 등의 타겟에 부딪히면 파괴 및 이팩트.
    /// </summary>
    public void HandleBreakHit(Collider2D other, Vector2 hitPoint)
    {
        if (!_isRunning) return; // 달리기 중이 아니면 무시
        
        if (((1 << other.gameObject.layer) & _breakableMask) == 0)
            return;

        // 1) 이팩트
        SpawnBreakVfx(hitPoint);

        // 2) 파괴 우선순위: IBreakable → Rigidbody 루트 → 콜라이더 오브젝트
        if (other.TryGetComponent<IBreakable>(out var br))
        {
            br.Break(hitPoint);
            return;
        }

        // Tilemap을 통째로 날리는 실수를 방지: DestructibleTilemap2D가 있으면 그쪽으로 위임
        var tilemapBreak = other.GetComponentInParent<DestructibleTilemap2D>();
        if (tilemapBreak != null)
        {
            tilemapBreak.BreakAt(hitPoint);
            return;
        }

        var rootRb = other.attachedRigidbody ? other.attachedRigidbody.gameObject : null;
        if (rootRb != null && rootRb != gameObject)
        {
            Destroy(rootRb);
        }
        else
        {
            Destroy(other.gameObject);
        }
    }
    
    /// <summary>
    /// 달리기 상태 디버그 정보
    /// </summary>
    public string GetDebugInfo()
    {
        return $"Running: {_isRunning}, Speed: {_currentSpeed:F2}/{runSpeed:F2}, External: {_isExternalTriggered}";
    }

    private void SpawnBreakVfx(Vector2 at)
    {
        if (breakVfxPrefab != null)
        {
            var go = Instantiate(breakVfxPrefab, at, Quaternion.identity);
            return;
        }

        // 프리팹 없을 때: 간단한 파편 파티클 생성
        var vfx = new GameObject("BreakVFX(runtime)");
        vfx.transform.position = at;
        var ps = vfx.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startLifetime = 0.6f;
        main.startSpeed = 6f;
        main.startSize = 0.12f;
        main.gravityModifier = 1.2f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 64;
        main.duration = 0.4f;
        main.loop = false;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0f, 24, 36, 1, 0.01f)
        });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.1f;

        var color = ps.colorOverLifetime;
        color.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.8f,0.7f,0.65f), 0f),
                new GradientColorKey(new Color(0.5f,0.45f,0.4f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        color.color = new ParticleSystem.MinMaxGradient(grad);

        ps.Play();
        Object.Destroy(vfx, fallbackVfxLifetime);
    }

    private static LayerMask NamesToMask(string namesCsv)
    {
        if (string.IsNullOrWhiteSpace(namesCsv)) return 0;
        int mask = 0;
        var parts = namesCsv.Split(',');
        foreach (var p in parts)
        {
            var name = p.Trim();
            if (string.IsNullOrEmpty(name)) continue;
            int layer = LayerMask.NameToLayer(name);
            if (layer >= 0) mask |= (1 << layer);
        }
        return mask;
    }
}
