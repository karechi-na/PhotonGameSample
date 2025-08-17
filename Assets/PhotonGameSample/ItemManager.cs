using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;
using PhotonGameSample.Infrastructure; // ServiceRegistry 参照用 追加

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
    private bool hasValidTotal = false; // アイテム総数が確定したか
    public static ItemManager Instance { get; private set; }
    
    // すべてのアイテムの参照をキャッシュ（非アクティブでも参照可能）
    private List<Item> cachedItems = new List<Item>();
    
    // ネットワーク管理（初期化用のみ保持）
    private NetworkRunner networkRunner;
    
    public int TotalItems => totalItemsInScene;
    public int CollectedItems => itemsCollected;
    public int RemainingItems => totalItemsInScene - itemsCollected;
    
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"ItemManager: Duplicate instance detected. Destroying this one. existingID={Instance.GetInstanceID()} newID={GetInstanceID()}");
            Destroy(this);
            return;
        }
        Instance = this;
        Debug.Log($"ItemManager: Awake singleton set instanceID={GetInstanceID()}");
    }

    void Start()
    {
        CountExistingItems();

        // PlayerManager が既に登録されていればアタッチ、まだなら登録イベントを待つ
        TryAttachPlayerManager();
        ServiceRegistry.OnAnyRegistered += HandleServiceRegistered; // 遅延登録対応

        // アイテムリセットイベントを購読
        GameEvents.OnItemsReset += ResetAllItemsViaRPC;
    }

    private void HandleServiceRegistered(Type type, object instance)
    {
        if (type == typeof(PlayerManager))
        {
            TryAttachPlayerManager();
        }
    }

    private void TryAttachPlayerManager()
    {
        var pm = ServiceRegistry.GetOrNull<PlayerManager>();
        if (pm == null) return;

        // 既存プレイヤーを登録（Find を排除）
        foreach (var kv in pm.AllPlayers)
        {
            if (kv.Value != null)
            {
                RegisterPlayer(kv.Value);
            }
        }

        // PlayerManager のイベントに追加 (重複購読防止用に一旦解除してから追加)
        pm.OnPlayerRegistered -= RegisterPlayer; // 直接 delegate 変換不可のためラップ
        pm.OnPlayerRegistered += RegisterPlayer;
    }
    
    // 旧 RegisterToPlayerEvents() は PlayerManager 経由イベント方式へ移行したため削除
    
    /// <summary>
    /// プレイヤーがアイテムをキャッチした時の処理
    /// </summary>
    private void HandleItemCaught(Item item, PlayerAvatar player)
    {
        Debug.Log($"ItemManager: HandleItemCaught start total={totalItemsInScene} collected={itemsCollected} hasValid={hasValidTotal} item='{item.name}' player={player.NickName.Value}");

        // 総数未確定なら再計測
        if (!hasValidTotal || totalItemsInScene <= 0)
        {
            Debug.LogWarning($"ItemManager: totalItemsInScene invalid ({totalItemsInScene}) -> recounting before processing pickup");
            CountExistingItems();
        }

        // アイテムを非アクティブ化
        item.DeactivateItem();

        // 総数 >0 の場合のみカウントアップ
        if (totalItemsInScene > 0)
        {
            itemsCollected++;
        }

        OnItemCountChanged?.Invoke(itemsCollected, totalItemsInScene);
        Debug.Log($"ItemManager: Items collected: {itemsCollected}/{totalItemsInScene}");

        if (totalItemsInScene > 0 && itemsCollected >= totalItemsInScene)
        {
            Debug.Log("ItemManager: All items collected!");
            OnAllItemsCollected?.Invoke();
        }
        Debug.Log($"ItemManager: HandleItemCaught end total={totalItemsInScene} collected={itemsCollected}");
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
    /// シーン内の既存アイテムをカウントし、キャッシュします。
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
    hasValidTotal = totalItemsInScene > 0;
    Debug.Log($"ItemManager: Found {totalItemsInScene} items in scene (instanceID={GetInstanceID()})");
        
        // 既存アイテムにイベントを登録
        foreach (var item in cachedItems)
        {
            RegisterItemEvents(item);
        }
        
        OnItemCountChanged?.Invoke(itemsCollected, totalItemsInScene);
    }
    
    /// <summary>
    /// アイテムにイベントを登録します。
    /// </summary>
    /// <param name="item">登録対象のアイテム</param>
    private void RegisterItemEvents(Item item)
    {
        // 現在は特別なイベントはないが、将来の拡張に備える
    }
    
    /// <summary>
    /// プレイヤーをItemManagerに登録（GameControllerから呼び出される）
    /// </summary>
    public void RegisterPlayer(PlayerAvatar player)
    {
        if (player == null) return;
        // プレイヤーのItemCatcherに登録 (重複防止のため一旦解除後追加)
        var catcher = player.GetComponent<ItemCatcher>();
        if (catcher != null)
        {
            catcher.OnItemCaught -= HandleItemCaught;
            catcher.OnItemCaught += HandleItemCaught;
            Debug.Log($"ItemManager: Player {player.playerId} registered via PlayerManager event");
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
        // 追加シーンロード後に出現したアイテムが cachedItems に含まれないケースへの対処として、
        // リセット前に最新のアイテム一覧へ更新する（inactive も含む）。
        RebuildCachedItems();

        // 権限外クライアントが Networked 値を書き換えると再同期で上書きされ欠落が発生する恐れがあるため
        // StateAuthority を持つアイテムのみ ResetItem() を呼び出し RPC_ActivateItem を経由して全クライアント更新する。
        itemsCollected = 0;
        int authoritativeResets = 0;
        int skipped = 0;
        foreach (var item in cachedItems)
        {
            if (item == null) continue;
            if (item.Object != null && item.Object.HasStateAuthority)
            {
                item.ResetItem(); // 内部で RPC_ActivateItem() を発行
                authoritativeResets++;
            }
            else
            {
                // 非権限側は何もしない（StateAuthority の同期を待つ）
                skipped++;
            }
        }
        OnItemCountChanged?.Invoke(itemsCollected, totalItemsInScene);
        Debug.Log($"ItemManager: ResetAllItemsViaRPC authoritativeResets={authoritativeResets} skipped(non-authority)={skipped} totalCached={cachedItems.Count}");

    // ネットワークレプリケーションによる OnChangedRender が遅延した場合に備え、
    // 次フレームで GameObject.activeSelf と IsItemActive の整合性を保証するフォールバックを実施。
    StartCoroutine(ReactivateItemsNextFrame());
    }

    /// <summary>
    /// 既存 CountExistingItems のロジックから UI への即時イベント送出を除いたキャッシュ再構築専用ヘルパー。
    /// 再開時などリセット処理の直前に最新シーン状態を反映するために使用。
    /// </summary>
    private void RebuildCachedItems()
    {
        var allItems = Resources.FindObjectsOfTypeAll<Item>();
        var sceneItems = new List<Item>();
        foreach (var item in allItems)
        {
            if (item.gameObject.scene.isLoaded && item.gameObject.hideFlags == HideFlags.None)
            {
                sceneItems.Add(item);
            }
        }
        cachedItems.Clear();
        cachedItems.AddRange(sceneItems);
        totalItemsInScene = cachedItems.Count;
        Debug.Log($"ItemManager: RebuildCachedItems refreshed list count={cachedItems.Count}");
    }

    /// <summary>
    /// 次フレームで Active フラグと GameObject 状態の不整合を補正するフォールバック。
    /// </summary>
    private IEnumerator ReactivateItemsNextFrame()
    {
        yield return null; // 1フレーム待機（Networkedプロパティ反映待ち）
        int reactivated = 0;
        var allItems = Resources.FindObjectsOfTypeAll<Item>();
        foreach (var item in allItems)
        {
            if (item == null) continue;
            if (!item.gameObject.scene.isLoaded || item.gameObject.hideFlags != HideFlags.None) continue;
            if (item.IsItemActive && !item.gameObject.activeSelf)
            {
                item.gameObject.SetActive(true);
                reactivated++;
            }
        }
        if (reactivated > 0)
        {
            Debug.Log($"ItemManager: ReactivateItemsNextFrame reactivated={reactivated}");
        }
    }
    
    void OnDestroy()
    {
        // イベント購読解除
        GameEvents.OnItemsReset -= ResetAllItemsViaRPC;
        ServiceRegistry.OnAnyRegistered -= HandleServiceRegistered;
        var pm = ServiceRegistry.GetOrNull<PlayerManager>();
        if (pm != null)
        {
            pm.OnPlayerRegistered -= RegisterPlayer;
        }
    }
}