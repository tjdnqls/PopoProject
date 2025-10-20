using UnityEngine;

/// <summary>
/// P2 상태일 때 F키를 누르면 P2가 바라보는 방향을 중심으로 중심각 100도의 호를 생성하는 컨트롤러
/// </summary>
public class ArcController : MonoBehaviour
{
    [Header("Arc Settings")]
    [SerializeField] private float arcAngle = 100f; // 중심각 (도)
    [SerializeField] private float arcRadius = 2f; // 호의 반지름
    [SerializeField] private int arcSegments = 50; // 호를 구성하는 선분 개수 (부드러움 조절)
    
    [Header("Visual Settings")]
    [SerializeField] private Color arcColor = Color.cyan;
    [SerializeField] private float arcWidth = 0.1f;
    [SerializeField] private Material arcMaterial;
    
    [Header("References")]
    [SerializeField] private SwapController swapController;
    [SerializeField] private PlayerMouseMovement p2Movement;
    
    private LineRenderer arcLineRenderer;
    private GameObject arcObject;
    private bool isHoldingArc = false; // 호 표시 상태
    
    void Start()
    {
        // SwapController 자동 찾기
        if (swapController == null)
        {
            swapController = FindObjectOfType<SwapController>();
        }
        
        // P2 PlayerMouseMovement 자동 찾기
        if (p2Movement == null)
        {
            var allMovements = FindObjectsOfType<PlayerMouseMovement>();
            foreach (var movement in allMovements)
            {
                if (movement.playerID == SwapController.PlayerChar.P2)
                {
                    p2Movement = movement;
                    break;
                }
            }
        }
        
        // 호 오브젝트 생성
        CreateArcObject();
        
        // 경고 메시지
        if (swapController == null) Debug.LogWarning("[ArcController] SwapController not found!");
        if (p2Movement == null) Debug.LogWarning("[ArcController] P2 PlayerMouseMovement not found!");
    }
    
    void Update()
    {
        // P2가 선택된 상태에서만 동작
        if (!IsP2Selected())
        {
            if (isHoldingArc)
            {
                HideArc();
                EnableP2Movement();
            }
            return;
        }
        
        // 마우스 좌클릭 입력 체크
        if (Input.GetMouseButtonDown(0)) // 좌클릭 시작
        {
            isHoldingArc = true;
            DisableP2Movement();
            CreateArc();
        }
        else if (Input.GetMouseButton(0) && isHoldingArc) // 좌클릭 유지 중
        {
            UpdateArc(); // 지속적으로 호 업데이트
        }
        else if (Input.GetMouseButtonUp(0) && isHoldingArc) // 좌클릭 해제
        {
            isHoldingArc = false;
            HideArc();
            EnableP2Movement();
        }
    }
    
    /// <summary>
    /// 현재 P2가 선택되어 있는지 확인
    /// </summary>
    private bool IsP2Selected()
    {
        if (swapController == null) return false;
        return swapController.IsP2Selected();
    }
    
    /// <summary>
    /// P2 움직임 비활성화
    /// </summary>
    private void DisableP2Movement()
    {
        if (p2Movement != null)
        {
            p2Movement.enabled = false;
        }
    }
    
    /// <summary>
    /// P2 움직임 활성화
    /// </summary>
    private void EnableP2Movement()
    {
        if (p2Movement != null)
        {
            p2Movement.enabled = true;
        }
    }
    
    /// <summary>
    /// 호 지속적 업데이트 (홀드 중)
    /// </summary>
    private void UpdateArc()
    {
        CreateArc(); // 매 프레임 호 재생성으로 실시간 업데이트
    }
    
    /// <summary>
    /// 호 오브젝트 생성 및 초기화
    /// </summary>
    private void CreateArcObject()
    {
        // 호 전용 GameObject 생성
        arcObject = new GameObject("Arc");
        arcObject.transform.SetParent(transform);
        
        // LineRenderer 컴포넌트 추가
        arcLineRenderer = arcObject.AddComponent<LineRenderer>();
        
        // LineRenderer 설정
        arcLineRenderer.useWorldSpace = true;
        arcLineRenderer.material = arcMaterial != null ? arcMaterial : CreateDefaultMaterial();
        arcLineRenderer.startColor = arcColor;
        arcLineRenderer.endColor = arcColor;
        arcLineRenderer.startWidth = arcWidth;
        arcLineRenderer.endWidth = arcWidth;
        arcLineRenderer.positionCount = 0;
        arcLineRenderer.enabled = false;
        
        // 정렬 순서 설정 (플레이어보다 앞에 표시)
        arcLineRenderer.sortingOrder = 10;
    }
    
    /// <summary>
    /// 기본 머티리얼 생성
    /// </summary>
    private Material CreateDefaultMaterial()
    {
        var shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Standard");
        return new Material(shader);
    }
    
    /// <summary>
    /// P2 위치와 방향을 기준으로 호 생성
    /// </summary>
    private void CreateArc()
    {
        if (p2Movement == null || arcLineRenderer == null) return;
        
        // P2의 현재 위치와 방향 가져오기
        Vector3 p2Position = p2Movement.transform.position;
        float p2ScaleX = p2Movement.transform.localScale.x; // Scale 값으로 방향 판단
        
        // 호의 중심점 계산 (P2 위치 기준)
        Vector3 arcCenter = p2Position;
        
        // P2가 바라보는 방향을 기준으로 호의 시작각과 끝각 계산
        float halfArcAngle = arcAngle * 0.5f;
        float startAngle, endAngle;
        
        if (p2ScaleX > 0) // Scale이 양수면 오른쪽을 바라봄
        {
            // 0도(오른쪽)를 중심으로 위아래로 호 생성
            startAngle = -halfArcAngle;
            endAngle = halfArcAngle;
        }
        else // Scale이 음수면 왼쪽을 바라봄
        {
            // 180도(왼쪽)를 중심으로 위아래로 호 생성
            startAngle = 180f - halfArcAngle;
            endAngle = 180f + halfArcAngle;
        }
        
        // 호를 구성하는 점들 계산
        Vector3[] arcPoints = new Vector3[arcSegments + 1];
        
        for (int i = 0; i <= arcSegments; i++)
        {
            float t = (float)i / arcSegments;
            float currentAngle = Mathf.Lerp(startAngle, endAngle, t);
            float angleRad = currentAngle * Mathf.Deg2Rad;
            
            Vector3 point = arcCenter + new Vector3(
                Mathf.Cos(angleRad) * arcRadius,
                Mathf.Sin(angleRad) * arcRadius,
                0f
            );
            
            arcPoints[i] = point;
        }
        
        // LineRenderer에 점들 설정
        arcLineRenderer.positionCount = arcPoints.Length;
        arcLineRenderer.SetPositions(arcPoints);
        arcLineRenderer.enabled = true;
        
        // 호 표시 종료 시간 설정
        arcEndTime = Time.time + arcDuration;
        
        Debug.Log($"[ArcController] Arc created at P2 position: {p2Position}, direction: {(p2ScaleX > 0 ? "Right" : "Left")} (Scale.x: {p2ScaleX})");
    }
    
    /// <summary>
    /// 호 숨기기
    /// </summary>
    private void HideArc()
    {
        if (arcLineRenderer != null)
        {
            arcLineRenderer.enabled = false;
            arcLineRenderer.positionCount = 0;
        }
        arcEndTime = -1f;
    }
    
    /// <summary>
    /// 호 설정 변경 (런타임에서 호출 가능)
    /// </summary>
    public void SetArcSettings(float angle, float radius, float duration)
    {
        arcAngle = angle;
        arcRadius = radius;
        arcDuration = duration;
    }
    
    /// <summary>
    /// 호 색상 변경
    /// </summary>
    public void SetArcColor(Color color)
    {
        arcColor = color;
        if (arcLineRenderer != null)
        {
            arcLineRenderer.startColor = color;
            arcLineRenderer.endColor = color;
        }
    }
    
    /// <summary>
    /// 수동으로 호 생성 (외부에서 호출 가능)
    /// </summary>
    public void TriggerArc()
    {
        if (IsP2Selected())
        {
            CreateArc();
        }
    }
    
    void OnDestroy()
    {
        // 정리
        if (arcObject != null)
        {
            DestroyImmediate(arcObject);
        }
    }
}
