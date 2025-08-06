using UnityEngine;
using UnityEngine.SceneManagement;
using Fusion;
using TMPro;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(ItemManager), typeof(PlayerManager), typeof(GameUIManager))]
[RequireComponent(typeof(NetworkGameManager))]
[RequireComponent(typeof(GameRuleProcessor))]
/// <summary>
/// ゲーム全体の状態管理とプレイヤーのインタラクションを制御するメインコントローラー。
/// プレイヤー数の管理、ゲーム進行、UIや各種マネージャとの連携を担当します。
/// </summary>
public class GameController : MonoBehaviour
{
    const int MAX_PLAYERS = 2; // Maximum number of players allowed in the game
    const int COUNTDOWN_SECONDS = 5; // カウントダウン時間（秒）

    // ゲーム終了管理
    private bool gameEnded = false;

    // カウントダウン管理
    private bool isCountdownRunning = false;
    private Coroutine countdownCoroutine;

    // クリック待機管理
    private HashSet<int> playersClickedForRestart = new HashSet<int>();

    // フィールド追加
    private bool isProcessingRemoteInputState = false;

    /// <summary>
    /// external references to managers components.
    /// </summary>
    [SerializeField] private ItemManager itemManager;
    [SerializeField] private NetworkGameManager networkGameManager;
    [SerializeField] private PlayerManager playerManager; // PlayerManagerとして直接宣言
    private GameSyncManager gameSyncManager; // GameUIManagerとして直接宣言
    [SerializeField] private GameUIManager gameUIManager; // RequireComponentで取得
    [SerializeField] private GameRuleProcessor gameRuleProcessor; // ゲームルール処理
    // private GameSyncManager gameSyncManager; // ゲーム進行の同期管理 - TODO: 後で有効化

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

        // GameUIManagerの参照を確認
        if (gameUIManager == null)
        {
            Debug.LogError("GameController: GameUIManager not found!");
        }

        // NetworkGameManagerの参照を確認してイベントを登録
        if (networkGameManager == null)
        {
            Debug.LogError("GameController: NetworkGameManager not found!");
        }
        else
        {
            networkGameManager.OnPlayerSpawned += OnPlayerSpawned;
            networkGameManager.OnPlayerLeft += OnPlayerLeft;
            networkGameManager.OnGameEndRequested += OnGameEndRequested;
            // GameSyncManager生成イベントを購読
            networkGameManager.OnGameSyncManagerSpawned += OnGameSyncManagerSpawned;
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

        // ゲーム再開イベント購読
        GameEvents.OnGameRestartRequested += RestartGame;
        GameEvents.OnGameRestartExecution += ExecuteRestart;
        GameEvents.OnPlayerClickedForRestart += OnPlayerClickedForRestart;
        GameEvents.OnPlayerInputStateChanged += OnPlayerInputStateChanged;

        Debug.Log("GameController: Initialized and events subscribed");
    }

    void Start()
    {
        // ItemManagerのイベントを登録
        itemManager.OnItemCountChanged += OnItemCountChanged;
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
        itemManager.RegisterPlayer(avatar);
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

        // まとめてログ出力
        Debug.Log(
            "GameController: Game ended\n"
            + $"GameController: EndGame - Clearing player click list (was: [{string.Join(", ", playersClickedForRestart)}])\n"
            + "GameController: EndGame - Player click list cleared"
        );

        // 全プレイヤーの入力を無効化
        EnableAllPlayersInput(false);

        // プレイヤークリック待機をリセット
        playersClickedForRestart.Clear();

        // GameRuleProcessorに勝者決定を委任
        GameEvents.TriggerGameEnd();
    }

    // ゲーム再開処理
    private void RestartGame()
    {
        Debug.Log("=== GameController: RestartGame() called ===");
        Debug.Log($"GameController: RESTART CALL STACK: {System.Environment.StackTrace}");
        Debug.Log($"GameController: IsMasterClient: {(networkGameManager != null ? networkGameManager.IsMasterClient : false)}");

        // マスタークライアントがRPCで全クライアントに再開実行を通知
        if (networkGameManager != null && networkGameManager.IsMasterClient)
        {
            Debug.Log("GameController: This client is MasterClient - sending RPC for restart");

            // StateAuthorityを持つPlayerAvatarを探す（マスタークライアントのPlayerAvatar）
            PlayerAvatar[] playerAvatars = FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None);
            Debug.Log($"GameController: Found {playerAvatars.Length} PlayerAvatars in scene");

            PlayerAvatar masterPlayerAvatar = null;
            foreach (var avatar in playerAvatars)
            {
                Debug.Log($"GameController: PlayerAvatar {avatar.playerId} - HasStateAuthority: {avatar.HasStateAuthority}");
                if (avatar.HasStateAuthority)
                {
                    masterPlayerAvatar = avatar;
                    Debug.Log($"GameController: Found master PlayerAvatar - ID: {avatar.playerId}");
                    break;
                }
            }

            if (masterPlayerAvatar != null)
            {
                Debug.Log($"GameController: Using master PlayerAvatar {masterPlayerAvatar.playerId} to send restart RPC");
                gameSyncManager.NotifyGameRestart();
                Debug.Log("GameController: RPC sent via master PlayerAvatar for game restart");
            }
            else
            {
                Debug.LogWarning("GameController: No PlayerAvatar with StateAuthority found - executing restart locally");
                ExecuteRestart();
            }
        }
        else
        {
            Debug.Log("GameController: This client is NOT MasterClient - waiting for RPC");
        }
    }

    // ゲーム再開処理を実行
    private void ExecuteRestart()
    {
        // まとめてログ出力
        Debug.Log(
            "=== GameController: ExecuteRestart() called ===\n"
            + $"GameController: EXECUTE RESTART CALL STACK: {System.Environment.StackTrace}\n"
            + $"GameController: Current game state before restart: {CurrentGameState}\n"
            + $"GameController: Player count: {(playerManager != null ? playerManager.PlayerCount : 0)}\n"
            + $"GameController: IsMasterClient: {(networkGameManager != null ? networkGameManager.IsMasterClient : false)}"
        );

        // プレイヤークリック待機をリセット
        playersClickedForRestart.Clear();
        Debug.Log("GameController: Cleared player click list");

        // ゲーム状態をリセット
        gameEnded = false;

        // UIの勝者メッセージフラグをリセット
        if (gameUIManager != null)
        {
            gameUIManager.ResetWinnerMessageFlag();
        }

        // プレイヤーを初期位置に戻す
        if (playerManager != null)
        {
            playerManager.ResetAllPlayersToSpawnPosition();
        }

        // アイテムマネージャーをリセット（RPC経由）
        if (itemManager != null)
        {
            if (networkGameManager != null && networkGameManager.IsMasterClient)
            {
                // まとめてログ出力
                Debug.Log(
                    "GameController: Master client sending item reset RPC\n"
                    + $"GameController: Found {FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None).Length} PlayerAvatars for item reset"
                );

                PlayerAvatar[] playerAvatars = FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None);
                PlayerAvatar masterPlayerAvatar = null;

                foreach (var avatar in playerAvatars)
                {
                    if (avatar.HasStateAuthority)
                    {
                        masterPlayerAvatar = avatar;
                        Debug.Log($"GameController: Found master PlayerAvatar for item reset - ID: {avatar.playerId}");
                        break;
                    }
                }

                if (masterPlayerAvatar != null)
                {
                    Debug.Log($"GameController: Using master PlayerAvatar {masterPlayerAvatar.playerId} for item reset RPC");
                    masterPlayerAvatar.NotifyItemsReset();
                    Debug.Log("GameController: Item reset RPC sent via master PlayerAvatar");
                }
                else
                {
                    Debug.LogWarning("GameController: No master PlayerAvatar found - using local item reset\n"
                        + "GameController: Calling itemManager.ResetAllItemsViaRPC() as fallback\n"
                        + "GameController: Local item reset completed");
                    itemManager.ResetAllItemsViaRPC();
                }
            }
            else
            {
                Debug.Log("GameController: Not master client - waiting for item reset RPC");
            }
        }
        else
        {
            Debug.LogError("GameController: itemManager is null - cannot reset items");
        }

        // プレイヤーのスコアをリセット
        if (playerManager != null)
        {
            playerManager.ResetAllPlayersScore();
        }

        // カウントダウンからゲーム開始
        if (playerManager != null && playerManager.PlayerCount >= MAX_PLAYERS)
        {
            StartGameCountdown();
        }
        else
        {
            CurrentGameState = GameState.WaitingForPlayers;
        }
    }

    // プレイヤーがクリックした時の処理
    private void OnPlayerClickedForRestart(int playerId)
    {
        // 既にクリック済みの場合は無視
        if (playersClickedForRestart.Contains(playerId))
        {
            Debug.Log($"GameController: Player {playerId} already clicked, ignoring");
            return;
        }

        // クリックリストに追加
        playersClickedForRestart.Add(playerId);
        Debug.Log($"GameController: Click list now contains: [{string.Join(", ", playersClickedForRestart)}]");

        // 全プレイヤーがクリックしたらゲーム再開
        if (playersClickedForRestart.Count >= MAX_PLAYERS)
        {
            Debug.Log(
                "=== GameController: ALL PLAYERS CLICKED ===\n"
                + $"GameController: *** CONDITION MET *** Total clicks: {playersClickedForRestart.Count}/{MAX_PLAYERS}\n"
                + $"GameController: Clicked players: [{string.Join(", ", playersClickedForRestart)}]\n"
                + $"GameController: networkGameManager = {(networkGameManager != null ? "not null" : "null")}");

            // マスタークライアントのみが再開処理を実行
            if (networkGameManager.IsMasterClient)
            {
                RestartGame();
            }
        }
        else
        {
            Debug.Log(
                $"GameController: Still waiting for {MAX_PLAYERS - playersClickedForRestart.Count} more players to click\n"
                + $"GameController: Current condition: {playersClickedForRestart.Count} >= {MAX_PLAYERS} = {playersClickedForRestart.Count >= MAX_PLAYERS}");
        }
    }

    /// <summary>
    /// NetworkGameManagerからのプレイヤースポーン通知を受け取る
    /// </summary>
    private void OnPlayerSpawned(PlayerAvatar playerAvatar)
    {
        Debug.Log($"GameController: Player {playerAvatar.playerId} spawned");

        // GameSyncManagerの参照をここでセット
        if (gameSyncManager == null && networkGameManager != null)
        {
            gameSyncManager = networkGameManager.GameSyncManagerInstance;
            if (gameSyncManager != null)
                Debug.Log("GameController: GameSyncManager instance set in OnPlayerSpawned");
            else
                Debug.LogWarning("GameController: GameSyncManager instance is still null in OnPlayerSpawned");
        }

        // PlayerManagerに直接登録を指示
        playerManager.RegisterPlayerAvatar(playerAvatar);

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

    void OnEnable()
    {
        GameSyncManager.OnAnyGameSyncManagerSpawned += OnGameSyncManagerSpawned;
    }
    void OnDisable()
    {
        GameSyncManager.OnAnyGameSyncManagerSpawned -= OnGameSyncManagerSpawned;
    }
    private void OnGameSyncManagerSpawned(GameSyncManager instance)
    {
        gameSyncManager = instance;
        Debug.Log("GameController: GameSyncManager reference set via static event.");
    }

    private void CheckPlayerCountAndUpdateGameState(NetworkRunner runner)
    {
        int playerCount = runner.SessionInfo.PlayerCount;
        int registeredPlayers = playerManager != null ? playerManager.PlayerCount : 0;

        // 実際に登録されたプレイヤー数でゲーム状態を判定（ネットワーク上の接続数ではなく）
        if (registeredPlayers >= MAX_PLAYERS && CurrentGameState == GameState.WaitingForPlayers)
        {
            // 二人揃ったのでカウントダウン開始
            Debug.Log("GameController: All players ready! Starting countdown...");
            StartGameCountdown();
        }
        else if (registeredPlayers < MAX_PLAYERS)
        {
            // プレイヤーが足りない場合は待機状態
            if (CurrentGameState == GameState.CountdownToStart)
            {
                // カウンドダウン中にプレイヤーが離脱した場合、カウントダウンを停止
                StopGameCountdown();
            }
            CurrentGameState = GameState.WaitingForPlayers;
            Debug.Log($"GameController: Waiting for players... ({registeredPlayers}/{MAX_PLAYERS})");

            // 全プレイヤーの操作を無効化
            EnableAllPlayersInput(false);
        }
    }

    // カウントダウン開始
    private void StartGameCountdown()
    {
        Debug.Log($"[DEBUG] StartGameCountdown: isCountdownRunning={isCountdownRunning}, IsMasterClient={networkGameManager.IsMasterClient}, gameSyncManager={(gameSyncManager != null ? "not null" : "null")}, CurrentGameState={CurrentGameState}");
        if (isCountdownRunning)
            return;
        if (networkGameManager.IsMasterClient)
        {
            isCountdownRunning = true;
            CurrentGameState = GameState.CountdownToStart;
            if (gameSyncManager != null)
            {
                gameSyncManager.NotifyGameStateChanged(GameState.CountdownToStart);
            }
            Debug.Log("[DEBUG] StartGameCountdown: StartCoroutine(CountdownCoroutine()) called");
            countdownCoroutine = StartCoroutine(CountdownCoroutine());
        }
        else
        {
            Debug.Log("[DEBUG] StartGameCountdown: Not master client, skipping countdown start");
        }
    }

    // カウントダウン停止
    private void StopGameCountdown()
    {
        if (!isCountdownRunning) return;

        isCountdownRunning = false;
        if (countdownCoroutine != null)
        {
            StopCoroutine(countdownCoroutine);
            countdownCoroutine = null;
        }
        Debug.Log("GameController: Countdown stopped");
    }

    // カウントダウンコルーチン
    private System.Collections.IEnumerator CountdownCoroutine()
    {
        Debug.Log("[DEBUG] CountdownCoroutine started");
        for (int i = COUNTDOWN_SECONDS; i > 0; i--)
        {
            Debug.Log($"GameController: Game starting in {i} seconds...");

            // ローカルでイベント発火
            GameEvents.TriggerCountdownUpdate(i);

            // GameSyncManager経由で全クライアントに通知
            if (gameSyncManager != null)
            {
                gameSyncManager.NotifyCountdownUpdate(i);
            }

            yield return new WaitForSeconds(1f);
        }

        isCountdownRunning = false;
        CurrentGameState = GameState.InGame;
        Debug.Log("GameController: Game started!");

        EnableAllPlayersInput(true);

        if (gameSyncManager != null)
        {
            gameSyncManager.NotifyEnableAllPlayersInput(true);
            gameSyncManager.NotifyGameStart();
        }
    }


    // プレイヤー操作状態変更時の処理
    private void OnPlayerInputStateChanged(bool enabled)
    {
        // イベント経由で呼ばれた場合は再帰防止フラグを立てる
        isProcessingRemoteInputState = true;
        EnableAllPlayersInput(enabled);
        isProcessingRemoteInputState = false;
    }

    private void EnableAllPlayersInput(bool enabled)
    {
        if (playerManager != null)
        {
            playerManager.SetAllPlayersInputEnabled(enabled);

            // 再帰防止: ローカル発火時のみ通知
            if (!isProcessingRemoteInputState && gameSyncManager != null)
            {
                gameSyncManager.NotifyEnableAllPlayersInput(enabled);
            }
        }
        else
        {
            Debug.LogError("GameController: PlayerManager is null!");
        }
    }

    // プレイヤー離脱時の処理（GameLauncherから呼び出される）
    public void OnPlayerLeft(int playerId)
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
        switch (newState)
        {
            case GameState.WaitingForPlayers:
                Debug.Log("Waiting for players to join...");

                // GameEventsを通じてUIに伝達
                GameEvents.TriggerGameStateChanged(newState);
                EnableAllPlayersInput(false);
                // ゲーム状態をリセット
                ResetGameState();
                break;
            case GameState.CountdownToStart:
                EnableAllPlayersInput(false);
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
                EnableAllPlayersInput(false);
                break;
            case GameState.WaitingForRestart:
                EnableAllPlayersInput(false);
                break;
        }
    }

    private void ResetGameState()
    {
        gameEnded = false;

        // *** 重要：プレイヤークリック待機をリセット ***
        Debug.Log($"GameController: ResetGameState - Clearing player click list (was: [{string.Join(", ", playersClickedForRestart)}])");
        playersClickedForRestart.Clear();
        Debug.Log("GameController: ResetGameState - Player click list cleared");

        // ItemManagerでゲーム状態をリセット
        if (itemManager != null)
        {
            itemManager.ResetItemCount();
        }
    }

    void OnDestroy()
    {
        if (networkGameManager != null)
        {
            networkGameManager.OnPlayerSpawned -= OnPlayerSpawned;
            networkGameManager.OnPlayerLeft -= OnPlayerLeft;
            networkGameManager.OnGameEndRequested -= OnGameEndRequested;
            networkGameManager.OnGameSyncManagerSpawned -= OnGameSyncManagerSpawned;
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

        // ゲーム再開イベント購読解除
        GameEvents.OnGameRestartRequested -= RestartGame;
        GameEvents.OnGameRestartExecution -= ExecuteRestart;
        GameEvents.OnPlayerClickedForRestart -= OnPlayerClickedForRestart;
        GameEvents.OnPlayerInputStateChanged -= OnPlayerInputStateChanged;
    }
}

