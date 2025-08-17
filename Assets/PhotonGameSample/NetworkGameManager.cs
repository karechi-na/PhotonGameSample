using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Fusion;
using PhotonGameSample.Infrastructure; // 追加

/// <summary>
/// ネットワーク関連の管理を担当するコンポーネント
/// プレイヤーの接続、スポーン、シーン管理を行う
/// </summary>
public class NetworkGameManager : MonoBehaviour
{
    [Header("Network Settings")]
    [SerializeField] private PlayerAvatar playerAvatarPrefab;

    // GameSyncManagerのPrefab参照をInspectorでセット
    [SerializeField] private GameSyncManager gameSyncManagerPrefab;
    private GameSyncManager gameSyncManagerInstance;
    public GameSyncManager GameSyncManagerInstance
    {
        get
        {
            return gameSyncManagerInstance;
        }
    }
    [SerializeField]
    private Vector3[] spawnPositions = {
        new Vector3(-5, 2, 0),
        new Vector3(5, 2, 0),
    };

    // イベント
    public event Action<NetworkRunner, PlayerRef, bool> OnClientJoined;
    public event Action<PlayerAvatar> OnPlayerSpawned;
    public event Action<int> OnPlayerLeft;
    public event Action OnGameEndRequested; // ゲーム終了要求イベント
    public event Action<GameSyncManager> OnGameSyncManagerSpawned; // GameSyncManager生成時イベント

    // ネットワーク状態
    private bool isMasterClient = false;
    private NetworkRunner networkRunner;

    [Header("Scene Reload Restart Settings")] 
    [SerializeField] private string itemsSceneName = "ItemsScene"; // アイテム配置専用シーン名（Build Settings登録前提）
    [SerializeField] private int itemsSceneBuildIndex = 1; // 追加シーンのビルドインデックス（初期 Join 時と同じ想定）
    [SerializeField] private bool useSceneReloadRestart = false; // シーン再ロード方式を使うかのフラグ
    [SerializeField] private bool useFullReloadRestart = false; // フルシーン再ロード方式（ネットワーク含め全再初期化）
    private bool isReloadingItemsScene = false;
    private bool isFullReloading = false; // 二重実行防止
    private bool pendingAdditiveItemsSceneLoad = false; // Additive 読み込み中フラグ
    // フルリロード後のローカルアバター再生成制御
    private bool pendingLocalAvatarCheck = false;
    private bool localAvatarSpawnedAfterReload = false;
    // プレイヤーID安定割当（PlayerRef.PlayerId が 3,4... と増加するケースへの対策）
    private Dictionary<PlayerRef, int> assignedPlayerIds = new Dictionary<PlayerRef, int>(); // raw PlayerRef -> stableId (1 or 2)

    // 参照
    private GameLauncher gameLauncher;
    private ItemManager itemManager;

    // プロパティ
    public bool IsMasterClient => isMasterClient;
    public NetworkRunner NetworkRunner => networkRunner;
    public bool UseFullReloadRestart => useFullReloadRestart;
    [Header("Hard Reset Settings")]
    [SerializeField] private string bootstrapSceneName = "MainScene"; // 初期ブートシーン名（Build Settings 登録想定）
    private bool hardResetInProgress = false;
    // Additive ItemsScene ロード後に再カウント済みか
    private bool additiveItemsSceneCounted = false;
    private bool sceneLoadedCallbackRegistered = false;

    void Awake()
    {
        // GameLauncherの参照を取得
        ServiceRegistry.Register<NetworkGameManager>(this); // フェーズ1登録
        gameLauncher = FindFirstObjectByType<GameLauncher>();
        if (gameLauncher != null)
        {
            gameLauncher.OnJoindClient += OnJoindClient;
        }
        else
        {
            Debug.LogError("NetworkGameManager: GameLauncher not found!");
        }

        // ItemManagerの参照を取得
        itemManager = GetComponent<ItemManager>();
        if (itemManager == null)
        {
            itemManager = FindFirstObjectByType<ItemManager>();
        }

    // リスタート実行イベント購読（ゲーム再開フローの Scene Reload 版）
    GameEvents.OnGameRestartExecution += OnGameRestartExecutionForSceneReload; // 既存（シーン単体リロード）
    GameEvents.OnGameRestartExecution += OnGameRestartExecutionForFullReload; // 新規（フルリロード）
    GameEvents.OnHardResetRequested += OnHardResetRequested;
    }

    private void OnEnable()
    {
        if (!sceneLoadedCallbackRegistered)
        {
            SceneManager.sceneLoaded += OnSceneLoadedCallback;
            sceneLoadedCallbackRegistered = true;
        }
    }

    private void OnDisable()
    {
        if (sceneLoadedCallbackRegistered)
        {
            SceneManager.sceneLoaded -= OnSceneLoadedCallback;
            sceneLoadedCallbackRegistered = false;
        }
    }

    private void Start()
    {
        Debug.Log("NetworkGameManager: Start called");

        // Startでもアイテムが存在する場合は初期化を確認
        if (itemManager != null)
        {
            Debug.Log($"NetworkGameManager: ItemManager found in Start - Total items: {itemManager.TotalItems}");
        }
    }

    /// <summary>
    /// クライアント接続時の処理
    /// </summary>
    private void OnJoindClient(NetworkRunner runner, PlayerRef player, bool isMasterClient)
    {
        Debug.Log($"NetworkGameManager: OnJoindClient called - Player: {player.PlayerId}, IsMaster: {isMasterClient}, LocalPlayer: {runner.LocalPlayer.PlayerId}");
        Debug.Log($"NetworkGameManager: Client joined - Player: {player.PlayerId}, IsMaster: {isMasterClient}");
        this.networkRunner = runner;

        // 2人専用: 既に2人分の安定IDが埋まっている場合は以降の Join を無視（ホスト再接続/ゴースト防止）
        if (!assignedPlayerIds.ContainsKey(player) && assignedPlayerIds.Count >= 2)
        {
            Debug.LogWarning($"NetworkGameManager: Rejecting/ignoring additional join PlayerRef {player.PlayerId} - max players reached (assignedPlayerIds={assignedPlayerIds.Count})");
            return; // 以降の処理を行わない（スポーン・イベント発火抑止）
        }
        if (runner.LocalPlayer == player)
        {
            Debug.Log($"NetworkGameManager: This is LOCAL player {player.PlayerId}");
            // ここで常にフラグを上書き（以前のセッション値が残るのを防止）
            this.isMasterClient = isMasterClient;
            Debug.Log($"NetworkGameManager: isMasterClient flag set to {this.isMasterClient} (param={isMasterClient})");

            if (this.isMasterClient)
            {
                Debug.Log("NetworkGameManager: This client is now the master client");
                // マスタークライアントの場合、追加シーンを読み込む（SceneAuthority 確認）
                if (runner.IsSceneAuthority)
                {
                    SpawnGameSyncManager(runner);
                    runner.LoadScene(SceneRef.FromIndex(1), LoadSceneMode.Additive);
                    Debug.Log("NetworkGameManager: Loading additional scene (master)");
                }
                else
                {
                    Debug.LogWarning("NetworkGameManager: isMasterClient true but runner.IsSceneAuthority false - possible desync");
                }
            }
            else
            {
                Debug.Log($"NetworkGameManager: This is NON-MASTER client {player.PlayerId}");
            }

            // ItemManagerを全クライアントで初期化
            InitializeItemManager(runner);

            // 安定 playerId: Master=1, Non-Master=2 （2人専用）
            if (!assignedPlayerIds.ContainsKey(player))
            {
                int stable = isMasterClient ? 1 : 2;
                // 既にそのIDが他 PlayerRef に使われていないか確認（理論上不要）
                foreach (var kv in assignedPlayerIds)
                {
                    if (kv.Value == stable && kv.Key != player)
                    {
                        Debug.LogWarning($"NetworkGameManager: Stable ID {stable} already used by another PlayerRef. Forcing reassignment.");
                        // 衝突時: 逆IDが使用中なら異常 -> ログ残して早期 return
                        int alt = stable == 1 ? 2 : 1;
                        bool altUsed = false;
                        foreach (var kv2 in assignedPlayerIds)
                        {
                            if (kv2.Value == alt) { altUsed = true; break; }
                        }
                        if (altUsed)
                        {
                            Debug.LogError($"NetworkGameManager: Both stable IDs already assigned. Rejecting spawn for PlayerRef {player.PlayerId}");
                            return;
                        }
                        stable = alt; // 逆IDへ切替
                    }
                }
                assignedPlayerIds[player] = stable;
                Debug.Log($"NetworkGameManager: Assigned stable playerId={stable} to PlayerRef {player.PlayerId} (IsMaster={isMasterClient})");
            }

            // プレイヤーをスポーン（安定ID使用）
            Debug.Log($"NetworkGameManager: Starting SpawnPlayerAfterDelay for local player stableId={assignedPlayerIds[player]}");
            StartCoroutine(SpawnPlayerAfterDelay(runner, player));
        }
        else
        {
            Debug.Log($"NetworkGameManager: This is REMOTE player {player.PlayerId}, skipping spawn");
        }
    // イベントを発火（上限超過で早期 return したケースは到達しない）
    OnClientJoined?.Invoke(runner, player, isMasterClient);
    }

    /// <summary>
    /// ItemManagerの初期化
    /// </summary>
    private void InitializeItemManager(NetworkRunner runner)
    {
        if (itemManager != null)
        {
            itemManager.Initialize(runner);
            StartCoroutine(CountItemsAfterDelay());
            Debug.Log("NetworkGameManager: ItemManager initialized");
        }
        else
        {
            Debug.LogWarning("NetworkGameManager: ItemManager not found!");
        }
    }

    /// <summary>
    /// 遅延してアイテムをカウント
    /// </summary>
    private IEnumerator CountItemsAfterDelay()
    {
        yield return new WaitForSeconds(1f); // シーンロード完了を待つ

        if (itemManager != null)
        {
            itemManager.CountExistingItems();
            Debug.Log("NetworkGameManager: Counted existing static items in scene");
        }
    }

    /// <summary>
    /// プレイヤーのスポーン処理
    /// </summary>
    private IEnumerator SpawnPlayerAfterDelay(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"NetworkGameManager: Starting player spawn for Player {player.PlayerId}");

        if (hardResetInProgress)
        {
            Debug.LogWarning("NetworkGameManager: Spawn aborted (hard reset in progress)");
            yield break;
        }

        // フレームを少し待つ
        yield return new WaitForEndOfFrame();
        yield return new WaitForSeconds(0.1f);

        // 既に同じ playerId の Avatar が存在する場合は重複スポーンを回避
        var existingAvatars = FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None);
        int stableCheckId = assignedPlayerIds.TryGetValue(player, out var tmpStable) ? tmpStable : player.PlayerId;
        foreach (var av in existingAvatars)
        {
            if (av != null && av.playerId == stableCheckId)
            {
                Debug.LogWarning($"NetworkGameManager: Duplicate spawn prevented for stableId {stableCheckId} (PlayerRef {player.PlayerId}) - avatar already exists");
                // PlayerManager 登録確認
                var pm = FindFirstObjectByType<PlayerManager>();
                if (pm != null && pm.GetPlayerAvatar(stableCheckId) == null)
                {
                    Debug.Log("NetworkGameManager: Existing avatar not in PlayerManager dictionary -> registering now");
                    pm.RegisterPlayerAvatar(av);
                }
                yield break;
            }
        }

        // 安定した配置: PlayerId ベース (1 -> index0, 2 -> index1 ...)
    // 安定ID → 1,2 のみ
    int stableId = assignedPlayerIds.TryGetValue(player, out var sid) ? sid : player.PlayerId;
    var playerIndex = Mathf.Max(0, stableId - 1);
        var spawnedPosition = spawnPositions[playerIndex % spawnPositions.Length];
    // シンプル化: 重なり回避シフトは行わず、安定ID→固定インデックスをそのまま使用

        Debug.Log($"NetworkGameManager: Spawning player at position {spawnedPosition}");

        // プレイヤーアバターをスポーン
    var spawnedObject = runner.Spawn(
            playerAvatarPrefab,
            spawnedPosition,
            Quaternion.identity,
            player,
            onBeforeSpawned: (_, networkObject) =>
            {
                var playerAvatar = networkObject.GetComponent<PlayerAvatar>();
        playerAvatar.NickName = $"Player{stableId}";
        playerAvatar.playerId = stableId;
        Debug.Log($"NetworkGameManager: ⚙️ Configured player stableId={stableId} origRefId={player.PlayerId} before spawn - NickName: {playerAvatar.NickName.Value}, playerId: {playerAvatar.playerId}");
            });

        // スポーン完了をイベントで通知
        if (spawnedObject != null)
        {
            var playerAvatar = spawnedObject.GetComponent<PlayerAvatar>();
            if (playerAvatar != null)
            {
                Debug.Log($"NetworkGameManager: ✅ Player stableId={stableId} (ref={player.PlayerId}) spawned successfully");
                Debug.Log($"NetworkGameManager: Player {playerAvatar.playerId} HasStateAuthority: {playerAvatar.HasStateAuthority}");
                Debug.Log($"NetworkGameManager: Player {playerAvatar.playerId} NickName: '{playerAvatar.NickName.Value}'");
                // PlayerManager へ自動登録
                var pm = FindFirstObjectByType<PlayerManager>();
                if (pm != null)
                {
                    pm.RegisterPlayerAvatar(playerAvatar);
                }
                OnPlayerSpawned?.Invoke(playerAvatar);
            }
        }
        else
        {
            Debug.LogError($"NetworkGameManager: Failed to spawn player {player.PlayerId}");
        }
    }

    /// <summary>
    /// プレイヤー離脱処理
    /// </summary>
    public void HandlePlayerLeft(int playerId)
    {
        Debug.Log($"NetworkGameManager: Player {playerId} left the game");
        OnPlayerLeft?.Invoke(playerId);
    }

    /// <summary>
    /// ゲーム終了を全クライアントに通知
    /// </summary>
    public void RequestGameEnd()
    {
        Debug.Log("NetworkGameManager: Game end requested - triggering local event");
        OnGameEndRequested?.Invoke();
    }

    /// <summary>
    /// ネットワーク情報取得
    /// </summary>
    public int GetPlayerCount()
    {
        return networkRunner?.SessionInfo.PlayerCount ?? 0;
    }

    /// <summary>
    /// デバッグ情報
    /// </summary>
    public string GetNetworkDebugInfo()
    {
        if (networkRunner == null) return "NetworkRunner: null";

        return $"NetworkRunner: Connected={networkRunner.IsConnectedToServer}, " +
               $"Players={networkRunner.SessionInfo.PlayerCount}, " +
               $"IsMaster={isMasterClient}, " +
               $"IsSceneAuthority={networkRunner.IsSceneAuthority}";
    }

    void OnDestroy()
    {
        // イベントの登録解除
        if (gameLauncher != null)
        {
            gameLauncher.OnJoindClient -= OnJoindClient;
        }
    GameEvents.OnGameRestartExecution -= OnGameRestartExecutionForSceneReload;
    GameEvents.OnGameRestartExecution -= OnGameRestartExecutionForFullReload;
    GameEvents.OnHardResetRequested -= OnHardResetRequested;
    }

    // GameSyncManager生成時にイベント発火
    private void SpawnGameSyncManager(NetworkRunner runner)
    {
        Debug.Log("NetworkGameManager: SpawnGameSyncManager called");
        
        if (gameSyncManagerInstance == null)
        {
            Debug.Log("NetworkGameManager: Spawning GameSyncManager prefab");
            var spawnedObj = runner.Spawn(gameSyncManagerPrefab, Vector3.zero, Quaternion.identity, inputAuthority: null);
            gameSyncManagerInstance = spawnedObj.GetComponent<GameSyncManager>();
            
            Debug.Log($"NetworkGameManager: GameSyncManager spawned successfully: {gameSyncManagerInstance != null}");
            
            // ここでイベント発火
            OnGameSyncManagerSpawned?.Invoke(gameSyncManagerInstance);
            Debug.Log("NetworkGameManager: OnGameSyncManagerSpawned event fired");
        }
        else
        {
            Debug.Log("NetworkGameManager: GameSyncManager already exists, skipping spawn");
        }
    }

    // =============================================================
    // シーン再ロード型リスタート処理
    // =============================================================
    private void OnGameRestartExecutionForSceneReload()
    {
        // フルリロードモードが有効な場合は本ルートは無効化（優先度：フルリロード > アイテムシーンリロード）
        if (useFullReloadRestart) return;
        if (!useSceneReloadRestart) return; // オプション無効なら何もしない
        if (!isMasterClient) return; // Shared モードのマスターのみシーン操作
        if (isReloadingItemsScene) return;

        StartCoroutine(ReloadItemsSceneRoutine());
    }

    // =============================================================
    // フルシーン再ロード型リスタート処理（最小実装）
    // =============================================================
    private void OnGameRestartExecutionForFullReload()
    {
        if (!useFullReloadRestart) return; // モード無効
        if (!isMasterClient) return; // マスターのみトリガー（他クライアントはRPC経由で同じイベントを受ける）
        if (isFullReloading) return; // 二重防止
        StartCoroutine(FullReloadRoutine());
    }

    // =============================================================
    // ハードリセット: Runner を完全 Shutdown し、Bootstrap シーンを再ロードして完全初期化
    // =============================================================
    private void OnHardResetRequested()
    {
        // すべてのクライアントでローカル Runner を終了させブートシーン再ロード（Photon Shared でセッションを同期的に閉じる想定）
        if (hardResetInProgress)
        {
            Debug.Log("NetworkGameManager: Hard reset already in progress - ignoring duplicate request");
            return;
        }
        bool runtimeSceneAuthority = networkRunner != null && networkRunner.IsSceneAuthority;
        Debug.Log($"NetworkGameManager: Handling HardResetRequested (isMasterClient={isMasterClient}, IsSceneAuthority={runtimeSceneAuthority}) -> executing local hard reset");
        StartCoroutine(HardResetRoutine());
    }

    public void RequestHardReset()
    {
        Debug.Log("NetworkGameManager: RequestHardReset invoked");
        GameEvents.TriggerHardResetRequested();
    }

    private IEnumerator HardResetRoutine()
    {
        hardResetInProgress = true;
        Debug.Log("NetworkGameManager: HardResetRoutine start");

    // RPC 配信/他クライアントイベント処理の猶予 (1フレーム+α)
    yield return null; // 1 frame
    yield return new WaitForSeconds(0.1f);

        // 事前クリーンアップ（イベント解除など）
        GameEvents.TriggerHardResetPreCleanup();

        // 既存プレイヤー / GameSyncManager などの NetworkObject を authority 側で明示 Despawn する（任意: Runner 内部に任せる場合は省略可）
        if (networkRunner != null)
        {
            var allNetObjs = FindObjectsByType<NetworkObject>(FindObjectsSortMode.None);
            int despawned = 0;
            foreach (var no in allNetObjs)
            {
                if (no == null) continue;
                if (no.Runner != networkRunner) continue;
                // シーン常駐 (SceneManager 管理) のものも含め Despawn (Fusion が内部状態解放)
                if (networkRunner.IsRunning && networkRunner.IsSceneAuthority)
                {
                    networkRunner.Despawn(no);
                    despawned++;
                }
            }
            Debug.Log($"NetworkGameManager: Prefab network objects despawned={despawned}");
        }

        yield return null; // 1フレームで Despawn 反映

        // Runner Shutdown
        if (networkRunner != null)
        {
            Debug.Log("NetworkGameManager: Shutting down NetworkRunner");
            var shutdownTask = networkRunner.Shutdown();
            while (!shutdownTask.IsCompleted)
            {
                yield return null;
            }
            Debug.Log("NetworkGameManager: Runner shutdown complete");
        }

        // 静的シングルトン & イベントクリア
        PhotonGameSample.Infrastructure.ServiceRegistry.Clear();
        GameEvents.ClearAllHandlers();
    // 安定プレイヤーIDマップをクリア
    assignedPlayerIds.Clear();

        // Bootstrap シーン再ロード（Single）
        if (!string.IsNullOrEmpty(bootstrapSceneName))
        {
            Debug.Log($"NetworkGameManager: Loading bootstrap scene '{bootstrapSceneName}' (hard reset)");
            additiveItemsSceneCounted = false; // 次回シーンロードで再カウント再試行
            SceneManager.LoadScene(bootstrapSceneName, LoadSceneMode.Single);
        }
        else
        {
            Debug.LogError("NetworkGameManager: bootstrapSceneName not set for hard reset");
        }
        // 以降、新しい GameLauncher が Start で新規 Runner を作成し完全初期化される想定
    }

    private IEnumerator FullReloadRoutine()
    {
        isFullReloading = true;
        Debug.Log("NetworkGameManager: FullReloadRoutine start - reloading active scene for clean restart");

        // 1フレーム待機して他のリスタート関連処理(Log/UI)が流れる余地を与える
        yield return null;

        var active = SceneManager.GetActiveScene();
        Debug.Log($"NetworkGameManager: FullReloadRoutine active='{active.name}' index={active.buildIndex}");

        if (networkRunner != null && networkRunner.IsSceneAuthority)
        {
            // Fusion のシーンロードで全クライアント同期（Master だけが呼ぶ）
            Debug.Log($"NetworkGameManager: Using NetworkRunner.LoadScene to sync all clients -> index {active.buildIndex}");
            var sceneRef = SceneRef.FromIndex(active.buildIndex);
            networkRunner.LoadScene(sceneRef, LoadSceneMode.Single);
            // ローカルアバター再確認予定
            pendingLocalAvatarCheck = true;
            localAvatarSpawnedAfterReload = false;
        }
        else
        {
            // フォールバック（理論上ここには来ない想定）
            Debug.LogWarning("NetworkGameManager: SceneAuthority not available, falling back to SceneManager.LoadScene (clients will desync)");
            SceneManager.LoadScene(active.name);
        }

        // NetworkRunner.LoadScene は非同期。OnSceneLoadDone コールバックや再初期化側で後続処理を行う。
    }

    // =============================================================
    // NetworkRunner シーンロード完了コールバックから呼ばれる外部 API（GameLauncher 経由）
    // =============================================================
    public void OnRunnerSceneLoadDone(NetworkRunner runner)
    {
        Debug.Log("NetworkGameManager: OnRunnerSceneLoadDone invoked");
        if (runner == null)
        {
            Debug.LogWarning("NetworkGameManager: OnRunnerSceneLoadDone runner null");
            return;
        }

        // SceneAuthority 専用処理（Additive シーンロードや GameSyncManager 管理）
        if (runner.IsSceneAuthority)
        {
            // GameSyncManager が消えていたら再スポーン
            if (gameSyncManagerInstance == null)
            {
                Debug.Log("NetworkGameManager: GameSyncManager missing after scene load - respawning (authority)");
                SpawnGameSyncManager(runner);
            }

            if (useFullReloadRestart)
            {
                if (!IsSceneLoaded(itemsSceneName) && !pendingAdditiveItemsSceneLoad && itemsSceneBuildIndex >= 0)
                {
                    Debug.Log($"NetworkGameManager: Items scene '{itemsSceneName}' not loaded yet - loading additively (buildIndex={itemsSceneBuildIndex})");
                    pendingAdditiveItemsSceneLoad = true;
                    runner.LoadScene(SceneRef.FromIndex(itemsSceneBuildIndex), LoadSceneMode.Additive);
                    // authority 部分はここで一旦終了（後続は次のロード完了で続行）
                }
                if (pendingAdditiveItemsSceneLoad && IsSceneLoaded(itemsSceneName))
                {
                    Debug.Log("NetworkGameManager: Additive items scene load confirmed");
                    pendingAdditiveItemsSceneLoad = false;
                }
            }

            // アイテム再カウント
            itemManager = FindFirstObjectByType<ItemManager>();
            if (itemManager != null)
            {
                bool itemsSceneIsLoaded = IsSceneLoaded(itemsSceneName);
                if (itemsSceneIsLoaded)
                {
                    itemManager.CountExistingItems();
                    additiveItemsSceneCounted = true;
                    Debug.Log("NetworkGameManager: CountExistingItems invoked after scene load (authority, items scene loaded)");
                }
                else
                {
                    Debug.Log("NetworkGameManager: Items scene not yet loaded - will recount on sceneLoaded callback");
                }
            }
            else
            {
                Debug.LogWarning("NetworkGameManager: ItemManager not found after scene load (authority)");
            }
        }
        else
        {
            Debug.Log("NetworkGameManager: OnRunnerSceneLoadDone on non-authority client");
        }

        // 各クライアントが自分のアバターを確認（authority 以外も）
        if (useFullReloadRestart && pendingLocalAvatarCheck && !localAvatarSpawnedAfterReload)
        {
            StartCoroutine(EnsureLocalPlayerAvatarDeferred(runner));
        }

        // フルリロード後: PlayerManager が存在すればシーン実体から再構築（重複登録は内部で抑止）
        if (useFullReloadRestart)
        {
            var pm = FindFirstObjectByType<PlayerManager>();
            if (pm != null)
            {
                pm.RebuildFromSceneAvatars();
            }
            else
            {
                Debug.LogWarning("NetworkGameManager: PlayerManager not found for rebuild after scene load");
            }
        }
    }

    // Unity SceneManager callback: additive ItemsScene 完全ロード後に最終カウントを実施
    private void OnSceneLoadedCallback(Scene scene, LoadSceneMode mode)
    {
        if (string.IsNullOrEmpty(itemsSceneName)) return;
        if (!scene.name.Equals(itemsSceneName, StringComparison.Ordinal)) return;
        if (additiveItemsSceneCounted) return; // 既にカウント済み
        var runner = networkRunner;
        if (runner == null || !runner.IsSceneAuthority) return; // authority が最終カウント
        itemManager = FindFirstObjectByType<ItemManager>();
        if (itemManager != null)
        {
            itemManager.CountExistingItems();
            additiveItemsSceneCounted = true;
            Debug.Log("NetworkGameManager: OnSceneLoadedCallback final items recount complete");
        }
        else
        {
            Debug.LogWarning("NetworkGameManager: OnSceneLoadedCallback ItemManager not found for recount");
        }
    }

    private bool IsSceneLoaded(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return false;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var sc = SceneManager.GetSceneAt(i);
            if (sc.name == sceneName && sc.isLoaded) return true;
        }
        return false;
    }

    // ローカルプレイヤーのアバター存在確認と再スポーン（フルリロード対応）
    private IEnumerator EnsureLocalPlayerAvatarDeferred(NetworkRunner runner)
    {
        if (runner == null) yield break;
        var localId = runner.LocalPlayer.PlayerId;
        int frames = 0;
        const int maxFrames = 30; // ~0.5秒 目安
        while (frames < maxFrames)
        {
            var avatars = FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None);
            foreach (var av in avatars)
            {
                if (av != null && av.playerId == localId)
                {
                    Debug.Log($"NetworkGameManager: Local avatar already present after reload (frame={frames}) id={localId}");
                    localAvatarSpawnedAfterReload = true;
                    pendingLocalAvatarCheck = false;
                    yield break;
                }
            }
            frames++;
            yield return null;
        }
        Debug.Log($"NetworkGameManager: Local avatar NOT found after {maxFrames} frames, spawning now id={localId}");
        localAvatarSpawnedAfterReload = true;
        pendingLocalAvatarCheck = false;
        StartCoroutine(SpawnPlayerAfterDelay(runner, runner.LocalPlayer));
    }

    private IEnumerator ReloadItemsSceneRoutine()
    {
        if (string.IsNullOrWhiteSpace(itemsSceneName)) yield break;
        isReloadingItemsScene = true;
        Debug.Log($"NetworkGameManager: ReloadItemsSceneRoutine start scene={itemsSceneName}");

        // 既にロードされているならアンロード（存在しない場合はスキップ）
        Scene target = SceneManager.GetSceneByName(itemsSceneName);
        if (target.isLoaded)
        {
            Debug.Log($"NetworkGameManager: Unloading scene {itemsSceneName}");
            var unloadOp = SceneManager.UnloadSceneAsync(itemsSceneName);
            while (unloadOp != null && !unloadOp.isDone) yield return null;
            Debug.Log($"NetworkGameManager: Unload complete {itemsSceneName}");
            // 1フレーム待機（内部クリーンアップ待ち）
            yield return null;
        }

        Debug.Log($"NetworkGameManager: Loading scene {itemsSceneName} additively");
        var loadOp = SceneManager.LoadSceneAsync(itemsSceneName, LoadSceneMode.Additive);
        while (loadOp != null && !loadOp.isDone) yield return null;
        Debug.Log($"NetworkGameManager: Load complete {itemsSceneName}");
        // シーンが有効になるまで1フレーム待機
        yield return null;

        // 新しいシーンでアイテム再カウント（Master）
        var newItemManager = FindFirstObjectByType<ItemManager>();
        if (newItemManager != null)
        {
            newItemManager.CountExistingItems();
            Debug.Log("NetworkGameManager: Re-counted items after scene reload");
        }
        else
        {
            Debug.LogWarning("NetworkGameManager: ItemManager not found after scene reload");
        }

        // 全クライアントへ「アイテムシーン再ロード完了」を RPC 経由で伝達
        if (gameSyncManagerInstance != null && gameSyncManagerInstance.HasStateAuthority)
        {
            // GameSyncManager に専用RPCが無いので GameEvents を直接同期する仕組みを追加するか検討。
            // 暫定：GameState を WaitingForPlayers → Countdown 判定トリガーに任せる or ItemsSceneReloaded イベントをローカル発火のみ
            GameEvents.TriggerItemsSceneReloaded();
        }
        else
        {
            // ローカルのみ発火（Master外ではシーン制御しないため）
            GameEvents.TriggerItemsSceneReloaded();
        }

        isReloadingItemsScene = false;
        Debug.Log("NetworkGameManager: ReloadItemsSceneRoutine finished");
    }
}
