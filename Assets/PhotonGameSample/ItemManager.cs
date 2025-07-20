using System;
using UnityEngine;
using Fusion;

public class ItemManager : MonoBehaviour
{
    [Header("Static Item Management")]
    [SerializeField] private bool debugMode = false; // デバッグ用フラグ
    
    // イベント
    public event Action<int, int> OnItemCountChanged; // (currentCount, totalCount)
    public event Action OnAllItemsCollected; // 全アイテム収集完了時
    
    // アイテム管理
    private int totalItemsInScene = 0; // シーン内の全アイテム数（固定）
    private int itemsCollected = 0;
    
    // ネットワーク管理（初期化用のみ保持）
    private NetworkRunner networkRunner;
    
    public int TotalItems => totalItemsInScene;
    public int CollectedItems => itemsCollected;
    public int RemainingItems => totalItemsInScene - itemsCollected;
    
    void Start()
    {
        CountExistingItems();
        RegisterToPlayerEvents();
    }
    
    /// <summary>
    /// 全プレイヤーのItemCatcherイベントに登録
    /// </summary>
    private void RegisterToPlayerEvents()
    {
        // 既存のプレイヤーのItemCatcherに登録
        PlayerAvatar[] players = FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None);
        foreach (var player in players)
        {
            ItemCatcher catcher = player.GetComponent<ItemCatcher>();
            if (catcher != null)
            {
                catcher.OnItemCaught += HandleItemCaught;
                Debug.Log($"ItemManager: Registered to player {player.NickName.Value}'s ItemCatcher");
            }
        }
    }
    
    /// <summary>
    /// プレイヤーがアイテムをキャッチした時の処理
    /// </summary>
    private void HandleItemCaught(Item item, PlayerAvatar player)
    {
        Debug.Log($"ItemManager: Player {player.NickName.Value} caught an item");
        OnItemCollected(item, player);
    }
    
    /// <summary>
    /// 新しいプレイヤーのItemCatcherイベントに登録
    /// </summary>
    public void RegisterPlayer(PlayerAvatar player)
    {
        ItemCatcher catcher = player.GetComponent<ItemCatcher>();
        if (catcher != null)
        {
            catcher.OnItemCaught += HandleItemCaught;
            Debug.Log($"ItemManager: Registered new player {player.NickName.Value}'s ItemCatcher");
        }
    }
    
    public void Initialize(NetworkRunner runner)
    {
        networkRunner = runner;
        Debug.Log("ItemManager initialized");
    }
    
    /// <summary>
    /// シーン内の既存アイテムをカウント（静的アイテムのみ）
    /// </summary>
    public void CountExistingItems()
    {
        var existingItems = FindObjectsByType<Item>(FindObjectsSortMode.None);
        totalItemsInScene = existingItems.Length;
        itemsCollected = 0; // リセット
        
        Debug.Log($"ItemManager: Found {totalItemsInScene} static items in scene");
        
        // 既存アイテムにイベントを登録
        foreach (var item in existingItems)
        {
            RegisterItemEvents(item);
        }
        
        OnItemCountChanged?.Invoke(itemsCollected, totalItemsInScene);
    }
    
    /// <summary>
    /// アイテムにイベントを登録
    /// </summary>
    private void RegisterItemEvents(Item item)
    {
        if (item == null) return;
        
        // アイテムが取得されたときのイベントを設定
        // Itemクラスに取得イベントを追加する必要があります
    }
    
    /// <summary>
    /// アイテムが取得されたときに呼び出される
    /// </summary>
    public void OnItemCollected(Item item, PlayerAvatar player)
    {
        itemsCollected++;
        Debug.Log($"ItemManager: Item collected by Player {player.playerId}. Progress: {itemsCollected}/{totalItemsInScene}");
        
        OnItemCountChanged?.Invoke(itemsCollected, totalItemsInScene);
        
        // 全アイテムが収集されたかチェック
        if (itemsCollected >= totalItemsInScene && totalItemsInScene > 0)
        {
            Debug.Log($"ItemManager: All items collected! Final count: {itemsCollected}/{totalItemsInScene}");
            OnAllItemsCollected?.Invoke();
        }
        else
        {
            Debug.Log($"ItemManager: Still {totalItemsInScene - itemsCollected} items remaining");
        }
    }
    
    /// <summary>
    /// ゲームリセット時の処理
    /// </summary>
    public void ResetItemCount()
    {
        itemsCollected = 0;
        CountExistingItems();
        Debug.Log("ItemManager: Item count reset - using static items only");
    }
    
    /// <summary>
    /// デバッグ情報を取得
    /// </summary>
    public string GetDebugInfo()
    {
        return $"Items: {itemsCollected}/{totalItemsInScene} (Remaining: {RemainingItems})";
    }
}
