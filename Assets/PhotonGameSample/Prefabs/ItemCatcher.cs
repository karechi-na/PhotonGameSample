using System;
using System.Collections;
using UnityEngine;
using Fusion;

[RequireComponent(typeof(PlayerAvatar))]
public class ItemCatcher : MonoBehaviour
{
    public event Action<Item, PlayerAvatar> OnItemCaught; // Event to notify when an item is caught
    private PlayerAvatar playerAvatar;
    private void Awake()
    {
        playerAvatar = GetComponent<PlayerAvatar>();
    }
    public void ItemCought(Item item)
    {
        Debug.Log($"=== ItemCatcher.ItemCought === Item {item.GetInstanceID()} caught by Player {playerAvatar.playerId} ({playerAvatar.NickName.Value})" +
                  $"\n  Unity Frame: {Time.frameCount}, Time: {Time.time:F3}s" +
                  $"\n  OnItemCaught subscribers: {OnItemCaught?.GetInvocationList()?.Length ?? 0}");

        // アイテムをキャッチしたときの処理
        // 権限チェックはPlayerAvatar側で行うため、ここでは常にイベントを発火
        OnItemCaught?.Invoke(item, playerAvatar);
        
        Debug.Log($"=== ItemCatcher.ItemCought END === Event fired for item {item.GetInstanceID()}");
    }
}
