using UnityEngine;
using UnityEngine.Tilemaps;

[DisallowMultipleComponent]
[RequireComponent(typeof(Tilemap))]
public class DestructibleTilemap2D : MonoBehaviour, IBreakable
{
    public Tilemap tilemap;
    [Tooltip("충돌 지점 주변으로 제거할 반경(셀 단위)")]
    [Min(0)] public int radius = 0;
    [Tooltip("파편 이펙트(선택)")]
    public GameObject breakVfxPrefab;

    void Reset()
    {
        tilemap = GetComponent<Tilemap>();
    }

    public void Break(Vector2 worldPoint) => BreakAt(worldPoint);

    public void BreakAt(Vector2 worldPoint)
    {
        if (tilemap == null) tilemap = GetComponent<Tilemap>();
        Vector3Int cell = tilemap.WorldToCell(worldPoint);
        if (radius <= 0)
        {
            if (tilemap.HasTile(cell))
            {
                SpawnVfx(tilemap.GetCellCenterWorld(cell));
                tilemap.SetTile(cell, null);
            }
            return;
        }

        for (int y = -radius; y <= radius; y++)
            for (int x = -radius; x <= radius; x++)
            {
                var c = new Vector3Int(cell.x + x, cell.y + y, cell.z);
                if (tilemap.HasTile(c))
                {
                    SpawnVfx(tilemap.GetCellCenterWorld(c));
                    tilemap.SetTile(c, null);
                }
            }
    }

    private void SpawnVfx(Vector3 at)
    {
        if (breakVfxPrefab == null) return;
        Instantiate(breakVfxPrefab, at, Quaternion.identity);
    }
}

/// <summary>임의의 오브젝트도 구현하면 파괴 로직을 커스텀 가능</summary>
public interface IBreakable
{
    void Break(Vector2 worldPoint);
}
