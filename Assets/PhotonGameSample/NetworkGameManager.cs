using System;
using System.Collections;
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

    // 参照
    private GameLauncher gameLauncher;
    private ItemManager itemManager;

    // プロパティ
    public bool IsMasterClient => isMasterClient;
    public NetworkRunner NetworkRunner => networkRunner;

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
        if (runner.LocalPlayer == player)
        {
            Debug.Log($"NetworkGameManager: This is LOCAL player {player.PlayerId}");
            if (isMasterClient)
            {
                this.isMasterClient = true;
                Debug.Log("NetworkGameManager: This client is now the master client");

                // マスタークライアントの場合、追加シーンを読み込む
                if (runner.IsSceneAuthority)
                {
                    // ゲームシンクマネージャーをスポーン
                    SpawnGameSyncManager(runner);
                    runner.LoadScene(SceneRef.FromIndex(1), LoadSceneMode.Additive);
                    Debug.Log("NetworkGameManager: Loading additional scene");
                }
            }
            else
            {
                Debug.Log($"NetworkGameManager: This is NON-MASTER client {player.PlayerId}");
            }

            // ItemManagerを全クライアントで初期化
            InitializeItemManager(runner);

            // プレイヤーをスポーン
            Debug.Log($"NetworkGameManager: Starting SpawnPlayerAfterDelay for local player {player.PlayerId}");
            StartCoroutine(SpawnPlayerAfterDelay(runner, player));
        }
        else
        {
            Debug.Log($"NetworkGameManager: This is REMOTE player {player.PlayerId}, skipping spawn");
        }
        // イベントを発火
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

        // フレームを少し待つ
        yield return new WaitForEndOfFrame();
        yield return new WaitForSeconds(0.1f);

        var playerIndex = runner.SessionInfo.PlayerCount - 1;
        var spawnedPosition = spawnPositions[playerIndex % spawnPositions.Length];

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
                playerAvatar.NickName = $"Player{player.PlayerId}";
                playerAvatar.playerId = player.PlayerId;
                Debug.Log($"NetworkGameManager: ⚙️ Configured player {player.PlayerId} before spawn - NickName: {playerAvatar.NickName.Value}, playerId: {playerAvatar.playerId}");
            });

        // スポーン完了をイベントで通知
        if (spawnedObject != null)
        {
            var playerAvatar = spawnedObject.GetComponent<PlayerAvatar>();
            if (playerAvatar != null)
            {
                Debug.Log($"NetworkGameManager: ✅ Player {player.PlayerId} spawned successfully");
                Debug.Log($"NetworkGameManager: Player {playerAvatar.playerId} HasStateAuthority: {playerAvatar.HasStateAuthority}");
                Debug.Log($"NetworkGameManager: Player {playerAvatar.playerId} NickName: '{playerAvatar.NickName.Value}'");
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
}
