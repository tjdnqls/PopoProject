using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class BreakOnLayerHit2D : MonoBehaviour
{
    [Header("Break When Hit By (Layer Names, comma-separated)")]
    [SerializeField] private string breakLayerNames = "MonAttack";
    [SerializeField] private LayerMask extraBreakMask;

    [Header("Shards")]
    [SerializeField] private List<GameObject> shardPrefabs = new List<GameObject>();
    [Min(1)][SerializeField] private int shardCount = 5;
    [SerializeField] private float spawnRadius = 0.05f;

    [Header("Shard Kinetics")]
    [SerializeField] private float minSpeed = 3f;
    [SerializeField] private float maxSpeed = 6f;
    [SerializeField] private float maxAngularSpeed = 540f;
    [Range(0f, 1f)][SerializeField] private float upwardBias = 0.25f;

    public enum SpreadMode { FullCircle, AwayFromImpactNormal }
    [Header("Spread")]
    [SerializeField] private SpreadMode spreadMode = SpreadMode.AwayFromImpactNormal;
    [Range(0f, 360f)][SerializeField] private float arcDegrees = 160f;
    [SerializeField] private float angleJitter = 10f;

    [Header("Options")]
    [SerializeField] private bool spawnAtFirstContactPoint = true;
    [SerializeField] private bool inheritOtherVelocity = true;
    [SerializeField] private bool destroyWholeRoot = true;
    [SerializeField] private Transform destroyTarget;

    [Header("Force-Workarounds")]
    [Tooltip("이 오브젝트에 Rigidbody2D가 없으면 자동으로 Kinematic RB를 붙여 충돌/트리거 이벤트를 강제 수신합니다.")]
    [SerializeField] private bool autoAddKinematicRB = true;
    [Tooltip("상대 콜라이더의 부모/루트 중 하나라도 레이어가 일치하면 히트로 인정합니다.")]
    [SerializeField] private bool acceptParentOrRootLayer = true;

    // ====== 추가: 장면 제한 (Wood_Skewer_* 개수 관리) ======
    [Header("Scene Limit (Wood_Skewer_*)")]
    [Tooltip("파편 소환 전에 장면 내 특정 이름 접두어 오브젝트 수를 제한합니다.")]
    [SerializeField] private bool limitWoodSkewerInScene = true;

    [Tooltip("이 접두어들로 시작하는 오브젝트 수를 셉니다. (예: Wood_Skewer_0001, ..., 0005)")]
    [SerializeField]
    private string[] skewerNamePrefixes = new string[] {
        "Wood_Skewer_0001","Wood_Skewer_0002","Wood_Skewer_0003","Wood_Skewer_0004","Wood_Skewer_0005"
    };

    [Tooltip("이 수 이상이면 파편 소환 전에 일부를 제거합니다.")]
    [SerializeField] private int skewerLimitThreshold = 20;

    [Tooltip("임계치 이상일 때 제거할 개수")]
    [SerializeField] private int skewerPruneCount = 5;

    [Tooltip("제거 대상을 무작위로 선택합니다.")]
    [SerializeField] private bool pruneRandom = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    private int _breakMask;
    private bool _broken;

    private void Awake()
    {
        _breakMask = NamesToMask(breakLayerNames) | extraBreakMask.value;

        // 파괴 대상 기본값
        if (destroyTarget == null)
            destroyTarget = destroyWholeRoot ? transform.root : transform;

        // ★ 이벤트 강제 수신: RB 없으면 자동 부착
        if (autoAddKinematicRB && GetComponent<Rigidbody2D>() == null)
        {
            var rb = gameObject.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.useFullKinematicContacts = true;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.sleepMode = RigidbodySleepMode2D.NeverSleep;
            if (debugLogs) Debug.Log($"[BreakOnLayerHit2D] Added Kinematic RB to force contacts. ({name})");
        }

        if (_breakMask == 0 && debugLogs)
            Debug.LogWarning($"[BreakOnLayerHit2D] Resolved _breakMask=0. Check layer names: \"{breakLayerNames}\" on {name}");
    }

    private void OnValidate()
    {
        _breakMask = NamesToMask(breakLayerNames) | extraBreakMask.value;
        if (destroyTarget == null)
            destroyTarget = destroyWholeRoot ? transform.root : transform;
        if (shardCount < 1) shardCount = 1;
        if (maxSpeed < minSpeed) maxSpeed = minSpeed;
        if (skewerPruneCount < 0) skewerPruneCount = 0;
        if (skewerLimitThreshold < 0) skewerLimitThreshold = 0;
    }

    private void OnCollisionEnter2D(Collision2D c)
    {
        if (_broken) return;
        if (debugLogs) Debug.Log($"[Break] Collision with {c.collider.name} (layer={LayerMask.LayerToName(c.collider.gameObject.layer)})");

        if (!LayerMatched(c.collider)) return;

        Vector2 pt = transform.position;
        Vector2 n = Vector2.up;
        if (c.contactCount > 0)
        {
            var cp = c.GetContact(0);
            pt = spawnAtFirstContactPoint ? cp.point : (Vector2)transform.position;
            n = cp.normal;
        }
        Break(pt, n, c.rigidbody);
    }

    private void OnTriggerEnter2D(Collider2D col)
    {
        if (_broken) return;
        if (debugLogs) Debug.Log($"[Break] Trigger with {col.name} (layer={LayerMask.LayerToName(col.gameObject.layer)})");

        if (!LayerMatched(col)) return;

        Vector2 center = transform.position;
        Vector2 normal = ((Vector2)transform.position - (Vector2)col.bounds.center).normalized;
        if (normal.sqrMagnitude < 0.0001f) normal = Vector2.up;
        Break(center, normal, col.attachedRigidbody);
    }

    private bool LayerMatched(Collider2D col)
    {
        // 1) 자식 콜라이더 레이어
        if (IsInMask(col.gameObject.layer, _breakMask)) return true;

        // 2) 부모/루트 레이어까지 허용
        if (acceptParentOrRootLayer)
        {
            Transform t = col.transform.parent;
            while (t != null)
            {
                if (IsInMask(t.gameObject.layer, _breakMask))
                {
                    if (debugLogs) Debug.Log($"[Break] Accepted by parent/root layer: {LayerMask.LayerToName(t.gameObject.layer)}");
                    return true;
                }
                t = t.parent;
            }
        }

        if (debugLogs)
            Debug.Log($"[Break] Rejected: layer not in mask (self+parents).");
        return false;
    }

    private void Break(Vector2 center, Vector2 impactNormal, Rigidbody2D otherRb)
    {
        _broken = true;

        // ====== 여기서 장면 내 Wood_Skewer_* 개수 제한 & 정리 ======
        MaybePruneSceneSkewers();

        Vector2 inheritVel = Vector2.zero;
        if (inheritOtherVelocity && otherRb != null) inheritVel = otherRb.linearVelocity;

        for (int i = 0; i < shardCount; i++)
        {
            var prefab = (shardPrefabs != null && shardPrefabs.Count > 0) ? shardPrefabs[i % shardPrefabs.Count] : null;
            Vector2 spawnPos = center + Random.insideUnitCircle * spawnRadius;
            Quaternion rot = Quaternion.Euler(0, 0, Random.Range(0f, 360f));
            var go = prefab != null ? Instantiate(prefab, spawnPos, rot) : null;

            var rb = go ? go.GetComponent<Rigidbody2D>() : null;
            if (rb != null)
            {
                Vector2 dir = ComputeDirection(i, impactNormal, shardCount);
                float speed = Random.Range(minSpeed, maxSpeed);
                rb.linearVelocity = dir.normalized * speed + inheritVel;  // rb.velocity 금지 기준 준수
                rb.angularVelocity = Random.Range(-maxAngularSpeed, maxAngularSpeed);
            }
        }

        if (destroyTarget != null) Destroy(destroyTarget.gameObject);
    }

    private Vector2 ComputeDirection(int index, Vector2 impactNormal, int count)
    {
        if (spreadMode == SpreadMode.FullCircle)
        {
            float baseAngle = (360f / count) * index + Random.Range(-angleJitter, angleJitter);
            return DirFromDeg(baseAngle);
        }
        else
        {
            float centerDeg = Vector2.SignedAngle(Vector2.right, impactNormal);
            float half = arcDegrees * 0.5f;
            float a = centerDeg + Random.Range(-half, half);
            Vector2 dir = DirFromDeg(a);
            dir += Vector2.up * upwardBias;
            return dir.normalized;
        }
    }

    private static Vector2 DirFromDeg(float deg)
    {
        float r = deg * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(r), Mathf.Sin(r));
    }

    private static bool IsInMask(int layer, int mask) => (mask & (1 << layer)) != 0;

    private static int NamesToMask(string names)
    {
        if (string.IsNullOrWhiteSpace(names)) return 0;
        int mask = 0;
        var parts = names.Split(',');
        foreach (var raw in parts)
        {
            var n = raw.Trim();
            if (n.Length == 0) continue;
            int id = LayerMask.NameToLayer(n);
            if (id >= 0) mask |= (1 << id);
        }
        return mask;
    }

    // ====== 장면 내 Wood_Skewer_* 개수 체크 & 정리 ======
    private void MaybePruneSceneSkewers()
    {
        if (!limitWoodSkewerInScene || skewerPruneCount <= 0 || skewerNamePrefixes == null || skewerNamePrefixes.Length == 0)
            return;

        var list = FindSkewerObjects();
        int count = list.Count;
        if (debugLogs) Debug.Log($"[SkewerLimit] Found {count} skewer-like objects in scene.");

        if (count >= skewerLimitThreshold)
        {
            int toRemove = Mathf.Min(skewerPruneCount, count);
            for (int i = 0; i < toRemove && list.Count > 0; i++)
            {
                int idx = pruneRandom ? Random.Range(0, list.Count) : list.Count - 1;
                var victim = list[idx];
                list.RemoveAt(idx);

                if (!victim) { i--; continue; }
                if (destroyTarget && victim == destroyTarget.gameObject) { i--; continue; } // 자기 자신 보호

                if (debugLogs) Debug.Log($"[SkewerLimit] Destroy {victim.name}");
                Destroy(victim);
            }
        }
    }

    private List<GameObject> FindSkewerObjects()
    {
        List<GameObject> res = new List<GameObject>();

        // 활성 오브젝트만 스캔 (비활성은 무시)
#if UNITY_2023_1_OR_NEWER
        var trs = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
        var trs = Object.FindObjectsOfType<Transform>(false);
#endif
        for (int i = 0; i < trs.Length; i++)
        {
            var go = trs[i].gameObject;
            if (!go.activeInHierarchy) continue;
            if (destroyTarget && go == destroyTarget.gameObject) continue;

            string nm = go.name; // 예: "Wood_Skewer_0001" 또는 "Wood_Skewer_0001(Clone)"
            if (StartsWithAny(nm, skewerNamePrefixes))
                res.Add(go);
        }
        return res;
    }

    private static bool StartsWithAny(string s, string[] prefixes)
    {
        if (string.IsNullOrEmpty(s) || prefixes == null) return false;
        for (int i = 0; i < prefixes.Length; i++)
        {
            var pre = prefixes[i];
            if (!string.IsNullOrEmpty(pre) && s.StartsWith(pre)) return true;
        }
        return false;
    }
}
