using UnityEngine;
using UnityEngine.SceneManagement;
using Fusion;
using TMPro;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(ItemManager), typeof(PlayerManager), typeof(GameUIManager))]
[RequireComponent(typeof(GameRuleProcessor))]
/// <summary>
/// GameController is responsible for managing the game state and handling player interactions.
/// Stay within the maximum player limit and manage player models.
/// </summary>
public class GameController : MonoBehaviour
{
    const int MAX_PLAYERS = 2; // Maximum number of players allowed in the game

    // ゲーム終了管理
    private bool gameEnded = false;

    /// <summary>
    /// external references to managers components.
    /// </summary>
    [SerializeField] private ItemManager itemManager;
    [SerializeField] private NetworkGameManager networkGameManager;
    [SerializeField] private PlayerManager playerManager; // PlayerManagerとして直接宣言
    private GameUIManager gameUIManager; // RequireComponentで取得
    private GameRuleProcessor gameRuleProcessor; // ゲームルール処理

    private PlayerModel localPlayerModel; // ローカルプレイヤーのモデル

    private GameState currentGameState = GameState.WaitingForPlayers;
    public GameState CurrentGameState
    {
        get { return currentGameState; }
        set
        {
            if (currentGameState == value)
            {
                // 状態が変わらない場合は何もしない
                return;
            }
            else
            {
                Debug.Log("Game State Changed: " + currentGameState);
                OnChangeState(value);
                currentGameState = value;
            }

        }
    }

    void Awake()
    {
        Debug.Log("GameController: Awake() - Initializing components");
        
        // GameUIManagerの参照を取得
        gameUIManager = GetComponent<GameUIManager>();
        if (gameUIManager == null)
        {
            Debug.LogError("GameController: GameUIManager not found!");
        }
        
        // NetworkGameManagerの参照を取得してイベントを登録
        networkGameManager = GetComponent<NetworkGameManager>();
        if (networkGameManager == null)
        {
            networkGameManager = FindFirstObjectByType<NetworkGameManager>();
        }
        
        if (networkGameManager != null)
        {
            networkGameManager.OnClientJoined += OnClientJoined;
            networkGameManager.OnPlayerSpawned += OnPlayerSpawned;
            networkGameManager.OnPlayerLeft += OnPlayerLeft;
            networkGameManager.OnGameEndRequested += OnGameEndRequested;
        }
        else
        {
            Debug.LogError("GameController: NetworkGameManager not found!");
        }

        // PlayerManagerの参照を取得してイベントを登録
        playerManager = GetComponent<PlayerManager>();
        
        if (playerManager != null)
        {
            playerManager.OnPlayerRegistered += OnPlayerRegistered;
            playerManager.OnPlayerUnregistered += OnPlayerUnregistered;
            playerManager.OnPlayerScoreChanged += OnPlayerScoreChanged;
            playerManager.OnPlayerCountChanged += OnPlayerCountChanged;
        }
        else
        {
            Debug.LogError("GameController: PlayerManager not found!");
        }

        // GameRuleProcessorの参照を取得してイベントを登録
        gameRuleProcessor = GetComponent<GameRuleProcessor>();
        if (gameRuleProcessor != null)
        {
            gameRuleProcessor.OnGameEndTriggered += EndGame;
        }
        else
        {
            Debug.LogError("GameController: GameRuleProcessor not found!");
        }
    }

    void Start()
    {
        itemManager = GetComponent<ItemManager>();
        
        // ItemManagerの初期化
        InitializeItemManager();
    }

    private void InitializeItemManager()
    {
        if (itemManager != null)
        {
            // ItemManagerのイベントを登録
            itemManager.OnItemCountChanged += OnItemCountChanged;
        }
        else
        {
            Debug.LogWarning("ItemManager is not assigned!");
        }
    }

    private void OnItemCountChanged(int collectedCount, int totalCount)
    {
        // アイテム進捗の重要な変更のみログ出力
        if (collectedCount == totalCount)
        {
            Debug.Log($"All items collected! {collectedCount}/{totalCount}");
        }
    }

    // PlayerManagerからのイベントハンドラー
    private void OnPlayerRegistered(PlayerAvatar avatar)
    {
        Debug.Log($"GameController: Player {avatar.playerId} registered");
        
        // ItemManagerにプレイヤーを登録
        if (itemManager != null)
        {
            itemManager.RegisterPlayer(avatar);
        }
    }

    private void OnPlayerUnregistered(int playerId)
    {
        Debug.Log($"GameController: Player {playerId} unregistered via PlayerManager");
    }

    private void OnPlayerCountChanged(int playerCount)
    {
        Debug.Log($"GameController: Player count changed to {playerCount}");
        
        // GameEventsを通じてUIに伝達
        GameEvents.TriggerPlayerCountChanged(playerCount);
        
        // ゲーム状態をチェック（これが主要なゲーム状態管理トリガー）
        if (networkGameManager != null && networkGameManager.NetworkRunner != null)
        {
            CheckPlayerCountAndUpdateGameState(networkGameManager.NetworkRunner);
        }
    }


    private void EndGame()
    {
        if (gameEnded) return;

        gameEnded = true;
        CurrentGameState = GameState.GameOver;
        Debug.Log("GameController: Game ended");

        // 全プレイヤーの入力を無効化
        EnableAllPlayersInput(false);

        // GameRuleProcessorに勝者決定を委任
        GameEvents.TriggerGameEnd();
    }

    /// <summary>
    /// NetworkGameManagerからの接続通知を受け取る
    /// </summary>
    private void OnClientJoined(NetworkRunner runner, PlayerRef player, bool isMasterClient)
    {
        Debug.Log($"GameController: Client joined - Player: {player.PlayerId}, IsMaster: {isMasterClient}");

    }

    /// <summary>
    /// NetworkGameManagerからのプレイヤースポーン通知を受け取る
    /// </summary>
    private void OnPlayerSpawned(PlayerAvatar playerAvatar)
    {
        Debug.Log($"GameController: Player {playerAvatar.playerId} spawned");
        
        // PlayerManagerに直接登録を指示
        if (playerManager != null)
        {
            playerManager.RegisterPlayerAvatar(playerAvatar);
        }
        
        // スポーン後に再度ゲーム状態をチェック
        if (networkGameManager != null && networkGameManager.NetworkRunner != null)
        {
            CheckPlayerCountAndUpdateGameState(networkGameManager.NetworkRunner);
        }
    }

    /// <summary>
    /// NetworkGameManagerからのゲーム終了要求を受け取る
    /// </summary>
    private void OnGameEndRequested()
    {
        Debug.Log("GameController: Game end requested from NetworkGameManager");
        EndGame();
    }

    private void CheckPlayerCountAndUpdateGameState(NetworkRunner runner)
    {
        int playerCount = runner.SessionInfo.PlayerCount;
        int registeredPlayers = playerManager != null ? playerManager.PlayerCount : 0;
        
        // 実際に登録されたプレイヤー数でゲーム状態を判定（ネットワーク上の接続数ではなく）
        if (registeredPlayers >= MAX_PLAYERS && CurrentGameState == GameState.WaitingForPlayers)
        {
            // 二人揃ったのでゲーム開始
            CurrentGameState = GameState.InGame;
            Debug.Log("GameController: All players ready! Starting game...");

            // 全プレイヤーの操作を有効化
            EnableAllPlayersInput(true);
        }
        else if (registeredPlayers < MAX_PLAYERS)
        {
            // プレイヤーが足りない場合は待機状態
            CurrentGameState = GameState.WaitingForPlayers;
            Debug.Log($"GameController: Waiting for players... ({registeredPlayers}/{MAX_PLAYERS})");

            // 全プレイヤーの操作を無効化
            EnableAllPlayersInput(false);
        }
    }

    private void EnableAllPlayersInput(bool enabled)
    {
        if (playerManager != null)
        {
            playerManager.SetAllPlayersInputEnabled(enabled);
        }
        else
        {
            Debug.LogError("GameController: PlayerManager is null!");
        }
    }

    // プレイヤー離脱時の処理（GameLauncherから呼び出される）
    public void OnPlayerLeft(int playerId)
    {
        if (playerManager != null)
        {
            playerManager.UnregisterPlayerAvatar(playerId);
            Debug.Log($"GameController: Player {playerId} left the game");

            // プレイヤー数が足りなくなったら待機状態に戻す
            if (playerManager.PlayerCount < MAX_PLAYERS && CurrentGameState == GameState.InGame)
            {
                CurrentGameState = GameState.WaitingForPlayers;
                EnableAllPlayersInput(false);
            }
        }
    }

    private void OnPlayerScoreChanged(int playerId, int newScore)
    {
        // GameEventsを通じてUIに伝達
        GameEvents.TriggerPlayerScoreChanged(playerId, newScore);
    }

    // テスト用メソッド：手動でスコアを変更
    void Update()
    {
        // デバッグ用：ゲーム強制終了
        if (Input.GetKeyDown(KeyCode.F9))
        {
            Debug.Log("Force ending game...");
            // NetworkGameManager経由で強制終了
            if (networkGameManager != null)
            {
                networkGameManager.RequestGameEnd();
            }
            else
            {
                EndGame();
            }
        }
    }

    private void OnChangeState(GameState newState)
    {
        // 状態が変わったときの処理をここに記述します
        switch (newState)
        {
            case GameState.WaitingForPlayers:
                Debug.Log("Waiting for players to join...");
                
                // GameEventsを通じてUIに伝達
                GameEvents.TriggerGameStateChanged(newState);
                
                // ゲーム状態をリセット
                ResetGameState();
                break;
            case GameState.InGame:
                Debug.Log("Game started!");
                
                // GameEventsを通じてUIに伝達
                GameEvents.TriggerGameStateChanged(newState);
                
                EnableAllPlayersInput(true);
                break;
            case GameState.GameOver:
                Debug.Log("Game Over!");
                // 勝者決定はEndGame()で既に実行済み
                break;
        }
    }

    private void ResetGameState()
    {
        gameEnded = false;

        // ItemManagerでゲーム状態をリセット
        if (itemManager != null)
        {
            itemManager.ResetItemCount();
        }
    }

    private IEnumerator RestartGameAfterDelay()
    {
        yield return new WaitForSeconds(5.0f); // 5秒間結果を表示

        // ゲームを再開（プレイヤーが2人いる場合）
        if (playerManager != null && playerManager.PlayerCount >= MAX_PLAYERS)
        {
            CurrentGameState = GameState.InGame;
            EnableAllPlayersInput(true);
        }
        else
        {
            CurrentGameState = GameState.WaitingForPlayers;
        }
    }

    void OnDestroy()
    {
        // NetworkGameManagerのイベント登録解除
        if (networkGameManager != null)
        {
            networkGameManager.OnClientJoined -= OnClientJoined;
            networkGameManager.OnPlayerSpawned -= OnPlayerSpawned;
            networkGameManager.OnPlayerLeft -= OnPlayerLeft;
            networkGameManager.OnGameEndRequested -= OnGameEndRequested;
        }

        // PlayerManagerのイベント登録解除
        if (playerManager != null)
        {
            playerManager.OnPlayerRegistered -= OnPlayerRegistered;
            playerManager.OnPlayerUnregistered -= OnPlayerUnregistered;
            playerManager.OnPlayerScoreChanged -= OnPlayerScoreChanged;
            playerManager.OnPlayerCountChanged -= OnPlayerCountChanged;
        }

        // GameRuleProcessorのイベント登録解除
        if (gameRuleProcessor != null)
        {
            gameRuleProcessor.OnGameEndTriggered -= EndGame;
        }
    }
}
