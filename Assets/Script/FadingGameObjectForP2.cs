using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class FadingGameObjectForP2 : MonoBehaviour
{
    [Header("Target Roots (auto-filled by tags if enabled)")]
    public Transform player2Root;
    public Transform player1Root;

    [Header("Fade Distance (world units)")]
    public float fadeStartDistance = 6f;
    public float fadeEndDistance = 1.5f;

    [Header("Mode")]
    [Tooltip("true면: 멀수록 투명, 가까울수록 보이게(알파 ↑)")]
    public bool reverse = false;

    [Header("Appear gating")]
    [Tooltip("알파가 이 값 이상일 때부터 충돌 시작(예: 0.98)")]
    [Range(0f, 1f)] public float appearAlphaThreshold = 0.98f; // 원본과 동일: 실제 분기에는 미사용

    [Header("Easing")]
    public AnimationCurve alphaCurve = AnimationCurve.Linear(0, 0, 1, 1);

    [Tooltip("충돌용 콜라이더 오브젝트(선택). 비워두면 무시")]
    public GameObject coll;

    [Header("Auto-assign by Tag")]
    [Tooltip("시작 시 태그로 Player1/Player2를 자동 탐색하여 부모 Transform을 Root로 설정")]
    public bool autoAssignPlayersByTag = true;
    public string player1Tag = "Player1";
    public string player2Tag = "Player2";
    [Tooltip("태그로 찾은 오브젝트의 부모를 Root로 삼습니다. 부모가 없으면 해당 오브젝트를 사용")]
    public bool assignToParentOfTagged = true;

    // --- cached ---
    private Collider2D wallCollider;
    private SpriteRenderer[] spriteRenderers;  // 이 오브젝트에 붙은 SR들(자식 제외)
    private Renderer[] genericRenderers;       // MeshRenderer/Skinned 등(_Color MPB 사용)

    private readonly List<Collider2D> p2Cols = new();
    private readonly List<Collider2D> p1Cols = new();

    private bool collisionsIgnoredWithP2 = false;
    private bool collisionsIgnoredWithP1 = false;

    void Awake()
    {
        wallCollider = GetComponent<Collider2D>();

        // 현재 오브젝트에 부착된 렌더러만 수집(자식 제외: 원본 범위 유지)
        spriteRenderers = GetComponents<SpriteRenderer>();

        var allRenderers = GetComponents<Renderer>();
        var genericList = new List<Renderer>();
        foreach (var r in allRenderers)
        {
            if (r is SpriteRenderer) continue;
            genericList.Add(r);
        }
        genericRenderers = genericList.ToArray();

        // ★ 태그 기반 자동 할당
        if (autoAssignPlayersByTag)
        {
            AutoAssignPlayersByTag();
        }

        CacheP2Colliders();
        CacheP1Colliders();
        ValidateThresholds();
    }

    void OnValidate() => ValidateThresholds();

    private void ValidateThresholds()
    {
        if (fadeEndDistance < 0f) fadeEndDistance = 0f;
        if (fadeStartDistance < fadeEndDistance + 0.01f)
            fadeStartDistance = fadeEndDistance + 0.01f;
        if (appearAlphaThreshold < 0f) appearAlphaThreshold = 0f;
        if (appearAlphaThreshold > 1f) appearAlphaThreshold = 1f;
    }

    private void AutoAssignPlayersByTag()
    {
        // Player1
        if (player1Root == null && !string.IsNullOrEmpty(player1Tag))
        {
            var p1 = GameObject.FindWithTag(player1Tag);
            if (p1)
            {
                var root = assignToParentOfTagged && p1.transform.parent ? p1.transform.parent : p1.transform;
                SetPlayer1Root(root);
            }
            else
            {
                Debug.LogWarning($"[FadingGameObjectForP2] '{player1Tag}' 태그를 가진 오브젝트를 찾지 못했습니다.", this);
            }
        }

        // Player2
        if (player2Root == null && !string.IsNullOrEmpty(player2Tag))
        {
            var p2 = GameObject.FindWithTag(player2Tag);
            if (p2)
            {
                var root = assignToParentOfTagged && p2.transform.parent ? p2.transform.parent : p2.transform;
                SetPlayer2Root(root);
            }
            else
            {
                Debug.LogWarning($"[FadingGameObjectForP2] '{player2Tag}' 태그를 가진 오브젝트를 찾지 못했습니다.", this);
            }
        }
    }

    private void CacheP2Colliders()
    {
        p2Cols.Clear();
        if (player2Root == null) return;
        player2Root.GetComponentsInChildren(true, p2Cols);
        p2Cols.RemoveAll(c => c == null);
    }

    private void CacheP1Colliders()
    {
        p1Cols.Clear();
        if (player1Root == null) return;
        player1Root.GetComponentsInChildren(true, p1Cols);
        p1Cols.RemoveAll(c => c == null);
    }

    void LateUpdate()
    {
        if (player2Root == null && player1Root == null)
        {
            SetAlpha(1f);
            ForceCollision(true); // 항상 충돌 ON
            if (coll) coll.SetActive(true);
            return;
        }

        // 기준 위치: P2 우선, 없으면 P1
        Vector2 refPos = player2Root ? (Vector2)player2Root.position
                       : player1Root ? (Vector2)player1Root.position
                       : (Vector2)transform.position;

        // 원본과 동일: 이 오브젝트의 Collider2D로 ClosestPoint
        Vector2 closest = wallCollider ? wallCollider.ClosestPoint(refPos) : (Vector2)transform.position;
        float dist = Vector2.Distance(closest, refPos);

        // far(0) -> near(1)
        float t = Mathf.InverseLerp(fadeStartDistance, fadeEndDistance, dist);

        // 기본: 멀면 1, 가까우면 0  → 커브 → reverse면 뒤집기
        float alphaNormal = Mathf.Clamp01(alphaCurve.Evaluate(1f - t));
        float alpha = reverse ? (1f - alphaNormal) : alphaNormal;

        SetAlpha(alpha);

        // === 원본 충돌 규칙 호출 1: alpha가 완전히 0일 때만 통과(중간 한 번 호출) ===
        bool allowCollision = alpha > 0f;
        ForceCollision(allowCollision);

        // === 원본과 동일한 최종 분기 ===
        if (alpha <= 0f)
        {
            ForceCollision(false);            // 충돌 해제
            if (coll) coll.SetActive(false);  // 콜라이더 오브젝트 비활성화
        }
        else if (alpha >= 0.5f)
        {
            ForceCollision(true);             // 충돌 복구
            if (coll) coll.SetActive(true);   // 콜라이더 오브젝트 활성화
        }
        else
        {
            ForceCollision(false);
            if (coll) coll.SetActive(false);
        }
    }

    private void ForceCollision(bool enable)
    {
        bool ignore = !enable;

        if (collisionsIgnoredWithP2 != ignore)
        {
            SetIgnoreCollisionWithList(p2Cols, ignore);
            collisionsIgnoredWithP2 = ignore;
        }
        if (collisionsIgnoredWithP1 != ignore)
        {
            SetIgnoreCollisionWithList(p1Cols, ignore);
            collisionsIgnoredWithP1 = ignore;
        }
    }

    private void SetAlpha(float a)
    {
        // 1) SpriteRenderer들
        if (spriteRenderers != null && spriteRenderers.Length > 0)
        {
            for (int i = 0; i < spriteRenderers.Length; i++)
            {
                var sr = spriteRenderers[i];
                if (!sr) continue;
                Color c = sr.color; c.a = a; sr.color = c;
            }
        }

        // 2) 그 외 Renderer들(MeshRenderer/Skinned 등): MPB로 _Color 알파만 변경
        if (genericRenderers != null && genericRenderers.Length > 0)
        {
            for (int i = 0; i < genericRenderers.Length; i++)
            {
                var r = genericRenderers[i];
                if (!r) continue;

                var mpb = new MaterialPropertyBlock();
                r.GetPropertyBlock(mpb);

                Color c = Color.white;
                if (!mpb.isEmpty && mpb.HasVector("_Color")) c = mpb.GetColor("_Color");
                else if (r.sharedMaterial && r.sharedMaterial.HasProperty("_Color")) c = r.sharedMaterial.color;

                c.a = a;
                mpb.SetColor("_Color", c);
                r.SetPropertyBlock(mpb);
            }
        }
    }

    private void SetIgnoreCollisionWithList(List<Collider2D> list, bool ignore)
    {
        if (!wallCollider) return;
        for (int i = 0; i < list.Count; i++)
        {
            var col = list[i];
            if (col) Physics2D.IgnoreCollision(wallCollider, col, ignore);
        }
    }

    void OnDisable()
    {
        ForceCollision(true); // 원복: 충돌 ON
        SetAlpha(1f);
        if (coll) coll.SetActive(true);
    }

    public void SetPlayer2Root(Transform newRoot) { player2Root = newRoot; CacheP2Colliders(); }
    public void SetPlayer1Root(Transform newRoot) { player1Root = newRoot; CacheP1Colliders(); }

    // 에디터에서 수동 갱신용
    [ContextMenu("Auto-Assign Players From Tags")]
    public void Editor_AutoAssignNow()
    {
        AutoAssignPlayersByTag();
        CacheP2Colliders();
        CacheP1Colliders();
    }
}
