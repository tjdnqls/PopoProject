using System.Collections.Generic;
using UnityEngine;

public class CrystalManager : MonoBehaviour
{
    [System.Serializable]
    public class CrystalData
    {
        public GameObject crystalObject;
        [HideInInspector] public Vector3 originalPosition; // 처음 위치 자동 저장용
    }

    public List<CrystalData> crystals = new List<CrystalData>();

    void Start()
    {
        // 시작할 때 초기 위치 저장!
        foreach (var data in crystals)
        {
            data.originalPosition = data.crystalObject.transform.position;
        }
    }

    public void RespawnAll()
    {
        foreach (var data in crystals)
        {
            // 위치 리셋도 같이 할 경우:
            data.crystalObject.transform.position = data.originalPosition;

            // 오브젝트 활성화
            data.crystalObject.SetActive(true);
        }
    }
}
