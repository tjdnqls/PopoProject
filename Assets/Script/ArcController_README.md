# ArcController 사용 가이드

## 개요
`ArcController`는 P2 상태일 때 F키를 누르면 P2가 바라보는 방향을 중심으로 중심각 100도의 호를 생성하는 스크립트입니다.

## 주요 기능
- **P2 전용**: P2가 선택된 상태에서만 동작
- **F키 입력**: F키를 누르면 호 생성
- **방향 기반**: P2가 바라보는 방향(좌/우)을 중심으로 호 생성
- **자동 소멸**: 설정된 시간 후 호가 자동으로 사라짐

## 설치 방법

### 1. 스크립트 추가
1. `ArcController.cs` 파일을 프로젝트의 `Assets/Script/` 폴더에 추가
2. 빈 GameObject를 생성하고 "ArcController"로 이름 변경
3. 생성한 GameObject에 `ArcController` 컴포넌트 추가

### 2. 자동 설정
스크립트는 시작 시 자동으로 다음 참조들을 찾습니다:
- `SwapController`: 현재 선택된 플레이어 확인용
- `PlayerMouseMovement` (P2): P2의 위치와 방향 정보 획득용

### 3. 수동 설정 (선택사항)
Inspector에서 직접 참조를 설정할 수도 있습니다:
- **Swap Controller**: SwapController 오브젝트 드래그
- **P2 Movement**: P2의 PlayerMouseMovement 컴포넌트 드래그

## Inspector 설정

### Arc Settings
- **Arc Angle**: 호의 중심각 (기본값: 100도)
- **Arc Radius**: 호의 반지름 (기본값: 2.0)
- **Arc Segments**: 호의 부드러움 조절 (기본값: 50)
- **Arc Duration**: 호가 표시되는 시간 (기본값: 3초)

### Visual Settings
- **Arc Color**: 호의 색상 (기본값: Cyan)
- **Arc Width**: 호의 두께 (기본값: 0.1)
- **Arc Material**: 호에 사용할 머티리얼 (선택사항)

## 사용법

### 기본 사용법
1. Tab키로 P2를 선택
2. F키를 누르면 P2 위치에서 바라보는 방향으로 호 생성
3. 설정된 시간 후 호가 자동으로 사라짐

### 방향별 동작
- **P2가 오른쪽을 바라볼 때**: 0도를 중심으로 ±50도 호 생성
- **P2가 왼쪽을 바라볼 때**: 180도를 중심으로 ±50도 호 생성

## 코드 API

### 공개 메서드
```csharp
// 호 설정 변경
SetArcSettings(float angle, float radius, float duration)

// 호 색상 변경
SetArcColor(Color color)

// 수동으로 호 생성
TriggerArc()
```

### 사용 예시
```csharp
// 다른 스크립트에서 ArcController 제어
ArcController arcController = FindObjectOfType<ArcController>();

// 호 설정 변경
arcController.SetArcSettings(120f, 3f, 5f); // 120도, 반지름 3, 5초 지속

// 색상 변경
arcController.SetArcColor(Color.red);

// 수동으로 호 생성 (P2 선택 상태에서만 동작)
arcController.TriggerArc();
```

## 문제 해결

### 호가 생성되지 않는 경우
1. **P2가 선택되었는지 확인**: Tab키로 P2 선택
2. **참조 확인**: SwapController와 P2 PlayerMouseMovement가 올바르게 연결되었는지 확인
3. **콘솔 확인**: 경고 메시지가 있는지 확인

### 호가 보이지 않는 경우
1. **카메라 위치 확인**: 호가 카메라 시야 내에 있는지 확인
2. **색상 설정 확인**: 배경과 구분되는 색상으로 설정
3. **머티리얼 확인**: 올바른 머티리얼이 설정되었는지 확인

## 커스터마이징

### 호 모양 변경
- `arcAngle`: 호의 각도 조절
- `arcRadius`: 호의 크기 조절
- `arcSegments`: 호의 부드러움 조절 (높을수록 부드러움)

### 시각적 효과 변경
- `arcColor`: 호의 색상
- `arcWidth`: 호의 두께
- `arcMaterial`: 특별한 효과를 위한 커스텀 머티리얼

### 지속 시간 조절
- `arcDuration`: 호가 화면에 표시되는 시간

## 주의사항
- P1 상태에서는 F키를 눌러도 호가 생성되지 않습니다
- 호는 월드 좌표계를 사용하므로 카메라가 움직여도 위치가 고정됩니다
- 동시에 여러 개의 호를 생성할 수 없습니다 (새로운 호가 이전 호를 대체)
