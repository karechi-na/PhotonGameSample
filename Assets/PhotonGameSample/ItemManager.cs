using Fusion;
using UnityEngine;
using static Unity.Collections.Unicode;

public class ItemManager : MonoBehaviour
{
    [SerializeField]private GameObject itemPrefab; // Prefab for the item to be spawned
    [SerializeField]private Vector3[] spawnPoints; // Point where the item will be spawned

    public void SpawnItem(NetworkRunner runner, int index)
    {
        if (index < 0 || index >= spawnPoints.Length)
        {
            Debug.LogError("Invalid spawn point index: " + index);
            return;
        }
        Vector3 spawnPosition = spawnPoints[index];
        var spawnd = runner.Spawn(itemPrefab, spawnPosition, Quaternion.identity);
        Debug.Log("Item spawned at: " + spawnPosition);
    }

}
