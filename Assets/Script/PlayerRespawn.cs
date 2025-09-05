using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerRespawn : MonoBehaviour
{
    [Header("체크포인트 지정")]
    public Transform firstCheckpoint;

    [Header("플레이어 두 명 지정")]
    public GameObject player1;
    public GameObject player2;

    [Header("리셋 키 설정")]
    public KeyCode firstCheckpointKey = KeyCode.R; // 첫 번째 체크포인트
    public KeyCode lastSavedKey = KeyCode.Q;       // 마지막 저장된 체크포인트

    void Update()
    {
        // R 키 → 첫 번째 체크포인트
        if (Input.GetKeyDown(firstCheckpointKey) && firstCheckpoint != null)
        {
            SaveCheckpoint(firstCheckpoint.position);
            ReloadScene();
            Debug.Log("R 키 → 씬 리로드 후 첫 번째 체크포인트로 이동!");
        }

        // Q 키 → 마지막 저장된 체크포인트
        if (Input.GetKeyDown(lastSavedKey) && PlayerPrefs.HasKey("SavedX"))
        {
            Vector3 savedPos = new Vector3(
                PlayerPrefs.GetFloat("SavedX"),
                PlayerPrefs.GetFloat("SavedY"),
                PlayerPrefs.GetFloat("SavedZ")
            );
            SaveCheckpoint(savedPos); // 좌표 유지
            ReloadScene();
            Debug.Log("Q 키 → 씬 리로드 후 마지막 저장된 체크포인트로 이동!");
        }
    }

    private void SaveCheckpoint(Vector3 pos)
    {
        PlayerPrefs.SetFloat("SavedX", pos.x);
        PlayerPrefs.SetFloat("SavedY", pos.y);
        PlayerPrefs.SetFloat("SavedZ", pos.z);
        PlayerPrefs.Save();
    }

    private void ReloadScene()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    void Start()
    {
        // 씬 시작 시 저장된 위치 불러오기
        if (PlayerPrefs.HasKey("SavedX"))
        {
            Vector3 savedPos = new Vector3(
                PlayerPrefs.GetFloat("SavedX"),
                PlayerPrefs.GetFloat("SavedY"),
                PlayerPrefs.GetFloat("SavedZ")
            );

            if (player1 != null) player1.transform.position = savedPos;
            if (player2 != null) player2.transform.position = savedPos;
        }
        else if (firstCheckpoint != null)
        {
            MovePlayersToFirstCheckpoint();
        }
    }

    private void MovePlayersToFirstCheckpoint()
    {
        if (player1 != null) player1.transform.position = firstCheckpoint.position;
        if (player2 != null) player2.transform.position = firstCheckpoint.position;
    }
}