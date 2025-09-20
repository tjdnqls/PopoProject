using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class RunningPlatformManager : MonoBehaviour
{
    [Header("Start Condition")]
    [SerializeField] private bool autoStart = true;

    [Header("Spawn Settings")]
    [SerializeField] private Transform spawnOrigin;          // 맵 스포너 위치
    [SerializeField] private List<GameObject> prefabs;       // 선택한 10개 프리팹(순서대로 소환)
    [SerializeField] private float spawnInterval = 5f;       // 5초마다
    [SerializeField] private float stepX = 25f;              // 이전 스폰 X + 25
    [SerializeField] private Transform spawnParent;          // 선택(정리용)

    [Header("Camera Switch")]
    [SerializeField] private Camera defaultCamera;           // 비워두면 Camera.main
    [SerializeField] private RunnerSideScrollCamera runnerCamera; // 러너 전용 카메라(아래 스크립트)

    [Header("Debug")]
    [SerializeField] private bool showGizmos = true;
    [SerializeField] private Color gizmoColor = new Color(0.3f, 0.9f, 0.6f, 0.75f);

    private Coroutine _loop;
    private float _lastX;
    private bool _running;

    void Reset()
    {
        if (spawnOrigin == null) spawnOrigin = transform;
        if (defaultCamera == null && Camera.main) defaultCamera = Camera.main;
    }

    void Start()
    {
        if (autoStart) StartRun();
    }

    public void StartRun()
    {
        if (_running) return;
        if (spawnOrigin == null) { Debug.LogError("[RunningPlatformManager] spawnOrigin 미지정"); return; }
        if (prefabs == null || prefabs.Count == 0) { Debug.LogError("[RunningPlatformManager] prefabs 비어있음(10개 구성 추천)"); return; }

        // 카메라 스위치
        if (defaultCamera == null && Camera.main) defaultCamera = Camera.main;
        if (defaultCamera) defaultCamera.gameObject.SetActive(false);
        if (runnerCamera)
        {
            runnerCamera.gameObject.SetActive(true);
            runnerCamera.Begin(); // y고정 및 진행 시작
        }

        _lastX = spawnOrigin.position.x;
        _loop = StartCoroutine(SpawnLoop());
        _running = true;
    }

    public void StopRun()
    {
        if (!_running) return;
        if (_loop != null) StopCoroutine(_loop);
        _loop = null;

        // 카메라 원복
        if (runnerCamera) runnerCamera.End();
        if (defaultCamera) defaultCamera.gameObject.SetActive(true);

        _running = false;
    }

    private IEnumerator SpawnLoop()
    {
        // 요구사항: "내가 고른 10개의 프리팹" → 리스트 길이만큼만 1회 소환
        for (int i = 0; i < prefabs.Count; i++)
        {
            var p = prefabs[i];
            if (p == null) { Debug.LogWarning($"[RunningPlatformManager] prefabs[{i}]가 null"); continue; }

            Vector3 pos;
            if (i == 0)
                pos = spawnOrigin.position; // 첫 스폰은 스포너 위치
            else
                pos = new Vector3(_lastX + stepX, spawnOrigin.position.y, spawnOrigin.position.z);

            var go = Instantiate(p, pos, Quaternion.identity, spawnParent);
            _lastX = pos.x;

            if (i < prefabs.Count - 1) // 마지막 이후에는 대기 필요 없음
                yield return new WaitForSeconds(spawnInterval);
        }

        // 1회 진행 후 자동 종료(요구사항 충족). 계속 달리게 하고 싶으면 아래 주석 해제.
        // _loop = StartCoroutine(SpawnLoop());  // 순환
        // 또는 StopRun();                       // 명시 종료
    }

    void OnDisable() { if (_running) StopRun(); }

    void OnDrawGizmos()
    {
        if (!showGizmos || spawnOrigin == null || prefabs == null) return;
        Gizmos.color = gizmoColor;
        var basePos = spawnOrigin.position;
        for (int i = 0; i < prefabs.Count; i++)
        {
            float x = (i == 0) ? basePos.x : basePos.x + stepX * i;
            var pos = new Vector3(x, basePos.y, basePos.z);
            Gizmos.DrawWireCube(pos, new Vector3(2f, 0.5f, 0.1f));
        }
    }
}
