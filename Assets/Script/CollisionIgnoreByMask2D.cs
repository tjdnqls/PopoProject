// ===================== CollisionIgnoreByMask2D.cs =====================
// Unity 6.1
// - 이 스크립트가 붙은 오브젝트의 모든 Collider2D(자식 포함)가
//   1) 지정한 ignoreMask 에 속한 오브젝트의 Collider2D,
//   2) (옵션) TilemapCollider2D (레이어 무관 또는 지정 레이어만)
//   와 충돌하지 않도록 Physics2D.IgnoreCollision 을 개별 적용합니다.
// - 전역 레이어 매트릭스는 변경하지 않습니다(Per-object 전용).
// - 동적 생성 대응: 일정 간격으로 재스캔하여 새로 생긴 콜라이더에도 적용 가능.
//
// 사용법:
// 1) 빈 C# 스크립트로 저장 후 아무 오브젝트에 부착
// 2) Ignore Mask 에 충돌을 끌 레이어들을 선택
// 3) 타일맵도 끌 거면 "Ignore Tilemaps" 체크 (AnyLayer or SpecificLayers 선택)
// 4) 성능/동적생성 필요 시 Rescan Interval 조절
//
// 주의: Physics2D.IgnoreCollision 은 Collider 쌍 단위로 적용됩니다.
//      너무 많은 콜라이더 쌍에 적용하면 비용이 증가할 수 있으니
//      Rescan 범위/주기를 프로젝트 규모에 맞게 설정하세요.

using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class CollisionIgnoreByMask2D : MonoBehaviour
{
    [Header("Targets to Ignore (by LayerMask)")]
    [Tooltip("여기에 포함된 레이어의 오브젝트들과 충돌을 끕니다.")]
    [SerializeField] private LayerMask ignoreMask = 0;

    [Header("Tilemap Ignore")]
    [Tooltip("TilemapCollider2D와도 충돌을 끌지 여부")]
    [SerializeField] private bool ignoreTilemaps = true;

    public enum TilemapMode { AnyLayer, SpecificLayers }
    [SerializeField] private TilemapMode tilemapMode = TilemapMode.AnyLayer;

    [Tooltip("TilemapMode가 SpecificLayers일 때만 사용됩니다.")]
    [SerializeField] private LayerMask tilemapLayers = 0;

    [Header("Dynamic World Support")]
    [Tooltip("새로 생긴 콜라이더에도 자동 적용할지")]
    [SerializeField] private bool rescanPeriodically = true;

    [Tooltip("주기(초). 0이면 Start에서 1회만 스캔합니다.")]
    [Min(0f)][SerializeField] private float rescanInterval = 0.5f;

    [Header("Safety")]
    [Tooltip("자기 자신/자식 콜라이더(들)")]
    [SerializeField] private bool includeInactiveChildren = true;

    // 내부 상태
    private Collider2D[] _selfCols;                          // 자신+자식 콜라이더
    private readonly HashSet<(Collider2D a, Collider2D b)> _ignoredPairs = new();
    private float _nextScanTime;

    void Awake()
    {
        CacheSelfColliders();
    }

    void OnEnable()
    {
        // 즉시 1회 적용
        ScanAndApplyIgnores();
        _nextScanTime = Time.time + Mathf.Max(0.01f, rescanInterval);
    }

    void Update()
    {
        if (!rescanPeriodically) return;
        if (rescanInterval <= 0f) return;
        if (Time.time >= _nextScanTime)
        {
            ScanAndApplyIgnores();
            _nextScanTime = Time.time + rescanInterval;
        }
    }

    void OnTransformChildrenChanged()
    {
        // 자식 구조 변경 시 자기 콜라이더 재캐시
        CacheSelfColliders();
    }

    private void CacheSelfColliders()
    {
        _selfCols = includeInactiveChildren
            ? GetComponentsInChildren<Collider2D>(true)
            : GetComponentsInChildren<Collider2D>(false);
    }

    private void ScanAndApplyIgnores()
    {
        if (_selfCols == null || _selfCols.Length == 0)
            CacheSelfColliders();

        // 씬 내 모든 Collider2D 스캔 (한 번에)
        // 주의: 대규모 씬에서는 비용이 있을 수 있으니 rescanInterval을 키우세요.
        var all = FindObjectsOfType<Collider2D>(true);

        for (int i = 0; i < all.Length; i++)
        {
            var other = all[i];
            if (other == null) continue;

            // 자기 자신/자식은 제외
            if (IsSelfCollider(other)) continue;

            // 마스크 조건
            if (!ShouldIgnore(other)) continue;

            // 쌍별 IgnoreCollision 적용
            ApplyIgnoreToPair(other, true);
        }
    }

    private bool IsSelfCollider(Collider2D c)
    {
        if (_selfCols == null) return false;
        for (int i = 0; i < _selfCols.Length; i++)
            if (c == _selfCols[i]) return true;
        return false;
    }

    private bool ShouldIgnore(Collider2D other)
    {
        // 1) 레이어 마스크에 속한 경우
        if (((1 << other.gameObject.layer) & ignoreMask.value) != 0)
            return true;

        // 2) 타일맵 무시 옵션
        if (ignoreTilemaps && other is UnityEngine.Tilemaps.TilemapCollider2D)
        {
            if (tilemapMode == TilemapMode.AnyLayer) return true;
            if (tilemapMode == TilemapMode.SpecificLayers)
            {
                if (((1 << other.gameObject.layer) & tilemapLayers.value) != 0)
                    return true;
                return false;
            }
        }
        return false;
    }

    private void ApplyIgnoreToPair(Collider2D other, bool ignore)
    {
        if (_selfCols == null || _selfCols.Length == 0) return;

        for (int i = 0; i < _selfCols.Length; i++)
        {
            var a = _selfCols[i]; if (a == null) continue;

            var pair = (a, other);
            if (ignore)
            {
                if (_ignoredPairs.Contains(pair)) continue;
                Physics2D.IgnoreCollision(a, other, true);
                _ignoredPairs.Add(pair);
            }
            else
            {
                // 필요 시 되돌릴 수 있게 구현(현재는 사용 안 함)
                if (_ignoredPairs.Remove(pair))
                    Physics2D.IgnoreCollision(a, other, false);
            }
        }
    }

    // 충돌 콜백에서 즉시 반응하고 싶다면(초기 프레임 침투도 방지):
    void OnCollisionEnter2D(Collision2D collision)
    {
        var other = collision.collider;
        if (ShouldIgnore(other))
            ApplyIgnoreToPair(other, true);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (ShouldIgnore(other))
            ApplyIgnoreToPair(other, true);
    }
}
