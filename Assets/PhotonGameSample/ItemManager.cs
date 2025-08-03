using System;
using System.Collections.Generic;
using UnityEngine;
using Fusion;

/// <summary>
/// シーン内のアイテムを一括管理し、アイテムのカウント・リセット・プレイヤーとの連携を行うマネージャ。
/// アイテムの収集状況やリセット処理、イベント発火などを担当します。
/// </summary>
public class ItemManager : MonoBehaviour
{
    // イベント
    public event Action<int, int> OnItemCountChanged; // (currentCount, totalCount)
    public event Action OnAllItemsCollected; // 全アイテム収集完了時
    
    // アイテム管理
    private int totalItemsInScene = 0; // シーン内の全アイテム数（固定）
    private int itemsCollected = 0;
    
    // すべてのアイテムの参照をキャッシュ（非アクティブでも参照可能）
    private List<Item> cachedItems = new List<Item>();
    
    // ネットワーク管理（初期化用のみ保持）
    private NetworkRunner networkRunner;
    
    public int TotalItems => totalItemsInScene;
    public int CollectedItems => itemsCollected;
    public int RemainingItems => totalItemsInScene - itemsCollected;
    
    void Start()
    {
        CountExistingItems();
        RegisterToPlayerEvents();
        
        // アイテムリセットイベントを購読
        GameEvents.OnItemsReset += ResetAllItemsViaRPC;
    }
    
    /// <summary>
    /// 全プレイヤーのItemCatcherイベントに登録（初期化時のみ使用）
    /// </summary>
    private void RegisterToPlayerEvents()
    {
        // 既存のプレイヤーのItemCatcherに登録（初期化時の処理）
        PlayerAvatar[] players = FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None);
        Debug.Log($"ItemManager: Found {players.Length} existing players during initialization");
        
        foreach (var player in players)
        {
            RegisterPlayer(player);
        }
    }
    
    /// <summary>
    /// プレイヤーがアイテムをキャッチした時の処理
    /// </summary>
    private void HandleItemCaught(Item item, PlayerAvatar player)
    {
        Debug.Log($"ItemManager: Player {player.NickName.Value} caught item (value: {item.itemValue})");
        
        // アイテムをシーンから無効化
        item.DeactivateItem();
        
        // アイテム収集数を増加
        itemsCollected++;
        
        // UI更新イベントを発火
        OnItemCountChanged?.Invoke(itemsCollected, totalItemsInScene);
        
        Debug.Log($"ItemManager: Items collected: {itemsCollected}/{totalItemsInScene}");
        
        // 全アイテム収集チェック
        if (itemsCollected >= totalItemsInScene)
        {
            Debug.Log("ItemManager: All items collected!");
            OnAllItemsCollected?.Invoke();
        }
    }
    
    /// <summary>
    /// NetworkRunnerを設定（初期化時に呼び出し）
    /// </summary>
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
        // アクティブ・非アクティブ関係なく、すべてのItemコンポーネントを取得してキャッシュ
        // Resources.FindObjectsOfTypeAllを使用して、非アクティブなオブジェクトも含めて検索
        var allItems = Resources.FindObjectsOfTypeAll<Item>();
        
        // シーン内のオブジェクトのみをフィルタリング（Prefabは除外）
        var sceneItems = new List<Item>();
        foreach (var item in allItems)
        {
            // シーン内にあるオブジェクトのみを対象とする（Prefabを除外）
            if (item.gameObject.scene.isLoaded && item.gameObject.hideFlags == HideFlags.None)
            {
                sceneItems.Add(item);
            }
        }
        
        // キャッシュをクリアして再構築
        cachedItems.Clear();
        cachedItems.AddRange(sceneItems);
        
        totalItemsInScene = cachedItems.Count;
        itemsCollected = 0; // リセット
        
        Debug.Log($"ItemManager: Found {totalItemsInScene} items in scene");
        
        // 既存アイテムにイベントを登録
        foreach (var item in cachedItems)
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
        // 現在は特別なイベントはないが、将来の拡張に備える
    }
    
    /// <summary>
    /// プレイヤーをItemManagerに登録（GameControllerから呼び出される）
    /// </summary>
    public void RegisterPlayer(PlayerAvatar player)
    {
        // プレイヤーのItemCatcherに登録
        ItemCatcher catcher = player.GetComponent<ItemCatcher>();
        if (catcher != null)
        {
            catcher.OnItemCaught += HandleItemCaught;
            Debug.Log($"ItemManager: Player {player.playerId} registered");
        }
    }
    
    /// <summary>
    /// アイテムを動的に追加（ゲーム中に新しいアイテムが生成される場合）
    /// </summary>
    public void AddItem(Item item)
    {
        totalItemsInScene++;
        RegisterItemEvents(item);
        
        // UI更新
        OnItemCountChanged?.Invoke(itemsCollected, totalItemsInScene);
    }
    
    /// <summary>
    /// アイテムを削除（ゲーム中にアイテムが破壊される場合）
    /// </summary>
    public void RemoveItem(Item item)
    {
        totalItemsInScene--;
        
        // UI更新
        OnItemCountChanged?.Invoke(itemsCollected, totalItemsInScene);
    }
    
    /// <summary>
    /// デバッグ情報を取得
    /// </summary>
    public string GetDebugInfo()
    {
        return $"Items: {itemsCollected}/{totalItemsInScene} (Remaining: {RemainingItems})";
    }
    
    /// <summary>
    /// アイテムカウントをリセット（GameControllerから呼び出される）
    /// </summary>
    public void ResetItemCount()
    {
        itemsCollected = 0;
        
        // UIを更新
        OnItemCountChanged?.Invoke(itemsCollected, totalItemsInScene);
        
        Debug.Log($"ItemManager: Item count reset - {itemsCollected}/{totalItemsInScene}");
    }
    
    /// <summary>
    /// 全アイテムをリセット（ゲーム再開時に使用）
    /// </summary>
    public void ResetAllItems()
    {
        Debug.Log("ItemManager: Resetting all items for game restart");
        
        // アイテム収集数をリセット
        itemsCollected = 0;
        
        // シーン内の全Itemオブジェクトをリセット
        Item[] allItems = FindObjectsByType<Item>(FindObjectsSortMode.None);
        foreach (var item in allItems)
        {
            if (item != null)
            {
                // Itemクラスのリセットメソッドを呼び出し
                item.ResetItem();
            }
        }
        
        // アイテム数を再初期化
        CountExistingItems();
        
        // UIを更新
        OnItemCountChanged?.Invoke(itemsCollected, totalItemsInScene);
        
        Debug.Log($"ItemManager: Reset complete - {totalItemsInScene} items available");
    }
    
    /// <summary>
    /// RPC経由でのアイテムリセット（権限チェックなし）
    /// </summary>
    public void ResetAllItemsViaRPC()
    {
        // アイテム収集数をリセット
        itemsCollected = 0;
        
        // キャッシュされたアイテムを使用してリセット
        int resetCount = 0;
        foreach (var item in cachedItems)
        {
            if (item != null)
            {
                // 権限チェックなしでアイテムをリセット
                item.ForceResetItem();
                resetCount++;
            }
        }
        
        // UIを更新
        OnItemCountChanged?.Invoke(itemsCollected, totalItemsInScene);
    }
    
    void OnDestroy()
    {
        // イベント購読解除
        GameEvents.OnItemsReset -= ResetAllItemsViaRPC;
    }
}