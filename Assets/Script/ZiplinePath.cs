using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class ZiplinePath : MonoBehaviour
{
    [Tooltip("웨이포인트(순서대로). 최소 2개 이상 필요")]
    public List<Transform> waypoints = new List<Transform>();

    [Tooltip("길이 캐시(에디터에서 갱신)")]
    public float totalLength = 0f;

    private readonly List<float> segmentLengths = new List<float>();

    void OnValidate() { RebuildCache(); }
    void Awake() { RebuildCache(); }

    public void RebuildCache()
    {
        segmentLengths.Clear();
        totalLength = 0f;
        if (waypoints == null || waypoints.Count < 2) return;

        for (int i = 0; i < waypoints.Count - 1; i++)
        {
            float len = Vector3.Distance(waypoints[i].position, waypoints[i + 1].position);
            segmentLengths.Add(len);
            totalLength += len;
        }
    }

    // t ∈ [0,1] → 월드 좌표
    public Vector3 GetPoint(float t)
    {
        if (waypoints == null || waypoints.Count == 0) return transform.position;
        if (waypoints.Count == 1) return waypoints[0].position;

        t = Mathf.Clamp01(t);
        float targetDist = totalLength * t;

        float accum = 0f;
        for (int i = 0; i < segmentLengths.Count; i++)
        {
            float segLen = segmentLengths[i];
            if (targetDist <= accum + segLen || i == segmentLengths.Count - 1)
            {
                float localT = segLen > 0f ? (targetDist - accum) / segLen : 0f;
                return Vector3.Lerp(waypoints[i].position, waypoints[i + 1].position, localT);
            }
            accum += segLen;
        }
        return waypoints[^1].position;
    }

    // 단위 접선(진행 방향)
    public Vector3 GetTangent(float t)
    {
        t = Mathf.Clamp01(t);
        float dt = 0.001f;
        Vector3 p1 = GetPoint(Mathf.Clamp01(t));
        Vector3 p2 = GetPoint(Mathf.Clamp01(t + dt));
        Vector3 dir = (p2 - p1);
        if (dir.sqrMagnitude < 1e-6f) // 끝단 보정
        {
            p2 = GetPoint(Mathf.Clamp01(t - dt));
            dir = (p1 - p2);
        }
        return dir.sqrMagnitude > 0f ? dir.normalized : Vector3.right;
    }

    public bool IsValid => waypoints != null && waypoints.Count >= 2 && totalLength > 0f;
}
