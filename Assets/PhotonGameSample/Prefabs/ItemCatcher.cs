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
        if (playerAvatar.Object.HasStateAuthority)
        {
            Debug.Log($"Item caught by {playerAvatar.NickName.Value}");

            // アイテムをキャッチしたときの処理
            OnItemCaught?.Invoke(item, playerAvatar);
        }
        else
        {
            Debug.LogWarning("ItemCatcher does not have state authority to catch items.");
        }
    }
}
