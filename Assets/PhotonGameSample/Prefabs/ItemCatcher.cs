using System;
using UnityEngine;
using Fusion;

[RequireComponent(typeof(PlayerAvatar))]
public class ItemCatcher : MonoBehaviour
{
    public event Action<Item, PlayerAvatar> OnItemCaught;
    private PlayerAvatar playerAvatar;

    private void Awake()
    {
        playerAvatar = GetComponent<PlayerAvatar>();
        // nullチェックやDebug.Logは不要（RequireComponentで保証されるため）
    }

    public void ItemCought(Item item)
    {
        OnItemCaught?.Invoke(item, playerAvatar);
    }
}
