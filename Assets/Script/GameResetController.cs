using UnityEngine;

public class GameResetController : MonoBehaviour
{
    public Transform player;
    public Vector3 playerStartPosition;

    public CrystalManager crystalManager;

    public void ResetGame()
    {
        // 플레이어 위치 초기화
        player.position = playerStartPosition;

        // 크리스탈 리셋
        if (crystalManager != null)
            crystalManager.RespawnAll();
    }
}
