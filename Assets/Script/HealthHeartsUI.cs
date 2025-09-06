using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Player1HP의 HpChanged 이벤트를 받아, 하트(GameObject)들을 On/Off로 표시.
/// Hearts Parent의 "직계 자식"을 하트로 간주(좌→우).
/// </summary>
public class HealthHeartsUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Player1HP playerHP;
    [SerializeField] private Transform heartsParent; // 자식들이 하트

    private readonly List<GameObject> _hearts = new();

    private void Reset()
    {
        // 에디터에서 자동 채우기 시도
        if (!playerHP) playerHP = FindAnyObjectByType<Player1HP>();
        if (!heartsParent) heartsParent = transform;
    }

    private void Awake()
    {
        if (!heartsParent) heartsParent = transform;

        _hearts.Clear();
        for (int i = 0; i < heartsParent.childCount; i++)
        {
            var child = heartsParent.GetChild(i).gameObject;
            _hearts.Add(child);
        }
    }

    private void OnEnable()
    {
        if (!playerHP)
        {
            playerHP = FindAnyObjectByType<Player1HP>();
        }

        if (playerHP != null)
        {
            playerHP.HpChanged += OnHpChanged;
            playerHP.Died += OnDied;

            // 초기 상태 동기화
            OnHpChanged(playerHP.CurrentHP, playerHP.MaxHP);
        }
        else
        {
            Debug.LogWarning("[HealthHeartsUI] Player1HP 레퍼런스가 없습니다.");
        }
    }

    private void OnDisable()
    {
        if (playerHP != null)
        {
            playerHP.HpChanged -= OnHpChanged;
            playerHP.Died -= OnDied;
        }
    }

    private void OnHpChanged(int current, int max)
    {
        int total = _hearts.Count;

        for (int i = 0; i < total; i++)
        {
            // 오른쪽부터 켜지도록 역순 인덱스 계산
            // ex) current=2 → 오른쪽 2개 켬, 왼쪽 1개 끔
            bool visible = i >= total - current;
            _hearts[i].SetActive(visible);
        }
    }

    private void OnDied()
    {
        // 선택: 사망 시 모두 Off로 확실히 정리
        for (int i = 0; i < _hearts.Count; i++)
            _hearts[i].SetActive(false);
    }
}
