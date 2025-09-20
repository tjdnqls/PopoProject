// LaserAnchor.cs
using UnityEngine;

[DisallowMultipleComponent]
public class LaserAnchor : MonoBehaviour
{
    [Tooltip("이 값을 >0으로 주면 공주-앵커 최대 길이를 이 값으로 덮어씁니다. 0이면 플레이어 기본 길이 사용")]
    public float maxDistanceOverride = 0f;
}
