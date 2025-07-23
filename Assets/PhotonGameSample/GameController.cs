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
    const int COUNTDOWN_SECONDS = 5; // カウントダウン時間（秒）

    // ゲーム終了管理
    private bool gameEnded = false;
    
    // カウントダウン管理
    private bool isCountdownRunning = false;
    private Coroutine countdownCoroutine;
    
    // クリック待機管理
    private HashSet<int> playersClickedForRestart = new HashSet<int>();

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
        
        // ゲーム再開イベント購読
        GameEvents.OnGameRestartRequested += RestartGame;
        GameEvents.OnGameRestartExecution += ExecuteRestart;
        GameEvents.OnPlayerClickedForRestart += OnPlayerClickedForRestart;
        GameEvents.OnPlayerInputStateChanged += OnPlayerInputStateChanged;
        
        Debug.Log("GameController: Initialized and events subscribed");
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

        // *** 重要：プレイヤークリック待機をリセット ***
        Debug.Log($"GameController: EndGame - Clearing player click list (was: [{string.Join(", ", playersClickedForRestart)}])");
        playersClickedForRestart.Clear();
        Debug.Log("GameController: EndGame - Player click list cleared");

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
                masterPlayerAvatar.NotifyGameRestart();
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
        Debug.Log("=== GameController: ExecuteRestart() called ===");
        Debug.Log($"GameController: EXECUTE RESTART CALL STACK: {System.Environment.StackTrace}");
        Debug.Log($"GameController: Current game state before restart: {CurrentGameState}");
        Debug.Log($"GameController: Player count: {(playerManager != null ? playerManager.PlayerCount : 0)}");
        Debug.Log($"GameController: IsMasterClient: {(networkGameManager != null ? networkGameManager.IsMasterClient : false)}");
        
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
            // マスタークライアントがRPCでアイテムリセットを通知
            if (networkGameManager != null && networkGameManager.IsMasterClient)
            {
                Debug.Log("GameController: Master client sending item reset RPC");
                
                // StateAuthorityを持つPlayerAvatarを探す（マスタークライアントのPlayerAvatar）
                PlayerAvatar[] playerAvatars = FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None);
                PlayerAvatar masterPlayerAvatar = null;
                Debug.Log($"GameController: Found {playerAvatars.Length} PlayerAvatars for item reset");
                
                foreach (var avatar in playerAvatars)
                {
                    Debug.Log($"GameController: PlayerAvatar {avatar.playerId} - HasStateAuthority: {avatar.HasStateAuthority}");
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
                    Debug.LogWarning("GameController: No master PlayerAvatar found - using local item reset");
                    Debug.Log("GameController: Calling itemManager.ResetAllItemsViaRPC() as fallback");
                    itemManager.ResetAllItemsViaRPC();
                    Debug.Log("GameController: Local item reset completed");
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
        Debug.Log($"=== GameController: OnPlayerClickedForRestart ENTRY ===");
        Debug.Log($"GameController: *** RECEIVED PLAYER ID: {playerId} *** - CALL #{System.DateTime.Now:HH:mm:ss.fff}");
        Debug.Log($"GameController: Current game state: {CurrentGameState}");
        Debug.Log($"GameController: Players already clicked: [{string.Join(", ", playersClickedForRestart)}]");
        Debug.Log($"GameController: playersClickedForRestart.Count = {playersClickedForRestart.Count}");
        Debug.Log($"GameController: MAX_PLAYERS = {MAX_PLAYERS}");
        Debug.Log($"GameController: networkGameManager = {(networkGameManager != null ? "exists" : "null")}");
        if (networkGameManager != null)
        {
            Debug.Log($"GameController: IsMasterClient = {networkGameManager.IsMasterClient}");
            Debug.Log($"GameController: This client info - IsMasterClient: {networkGameManager.IsMasterClient}");
        }
        
        // 既にクリック済みの場合は無視
        if (playersClickedForRestart.Contains(playerId))
        {
            Debug.Log($"GameController: Player {playerId} already clicked, ignoring");
            Debug.Log($"GameController: Current click list before ignoring: [{string.Join(", ", playersClickedForRestart)}]");
            Debug.Log($"GameController: This is a DUPLICATE CLICK for Player {playerId}");
            return;
        }
        
        // クリックリストに追加
        playersClickedForRestart.Add(playerId);
        
        Debug.Log($"GameController: Player {playerId} added to click list. Total: {playersClickedForRestart.Count}/{MAX_PLAYERS}");
        Debug.Log($"GameController: Click list now contains: [{string.Join(", ", playersClickedForRestart)}]");
        
        // 全プレイヤーがクリックしたらゲーム再開
        Debug.Log($"GameController: Checking if all players clicked - Current: {playersClickedForRestart.Count}, Required: {MAX_PLAYERS}");
        if (playersClickedForRestart.Count >= MAX_PLAYERS)
        {
            Debug.Log("=== GameController: ALL PLAYERS CLICKED ===");
            Debug.Log($"GameController: *** CONDITION MET *** Total clicks: {playersClickedForRestart.Count}/{MAX_PLAYERS}");
            Debug.Log($"GameController: Clicked players: [{string.Join(", ", playersClickedForRestart)}]");
            Debug.Log($"GameController: networkGameManager = {(networkGameManager != null ? "not null" : "null")}");
            
            if (networkGameManager != null)
            {
                Debug.Log($"GameController: IsMasterClient = {networkGameManager.IsMasterClient}");
            }
            
            // マスタークライアントのみが再開処理を実行
            if (networkGameManager != null && networkGameManager.IsMasterClient)
            {
                Debug.Log("GameController: *** THIS IS MASTER CLIENT - CALLING RestartGame() ***");
                RestartGame();
            }
            else
            {
                Debug.Log("GameController: *** THIS IS NOT MASTER CLIENT - WAITING FOR RPC ***");
            }
        }
        else
        {
            Debug.Log($"GameController: Still waiting for {MAX_PLAYERS - playersClickedForRestart.Count} more players to click");
            Debug.Log($"GameController: Current condition: {playersClickedForRestart.Count} >= {MAX_PLAYERS} = {playersClickedForRestart.Count >= MAX_PLAYERS}");
        }
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
            // 二人揃ったのでカウントダウン開始
            Debug.Log("GameController: All players ready! Starting countdown...");
            StartGameCountdown();
        }
        else if (registeredPlayers < MAX_PLAYERS)
        {
            // プレイヤーが足りない場合は待機状態
            if (CurrentGameState == GameState.CountdownToStart)
            {
                // カウントダウン中にプレイヤーが離脱した場合、カウントダウンを停止
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
        if (isCountdownRunning) return;
        
        Debug.Log($"GameController: StartGameCountdown called - IsMasterClient: {(networkGameManager != null ? networkGameManager.IsMasterClient : false)}");
        
        // マスタークライアントのみがカウントダウンを開始
        if (networkGameManager != null && networkGameManager.IsMasterClient)
        {
            isCountdownRunning = true;
            CurrentGameState = GameState.CountdownToStart;
            
            // マスタークライアントのPlayerAvatarを取得してカウントダウン開始状態を同期
            PlayerAvatar masterPlayerAvatar = GetMasterPlayerAvatar();
            if (masterPlayerAvatar != null)
            {
                masterPlayerAvatar.NotifyGameStateChanged(GameState.CountdownToStart);
            }
            
            countdownCoroutine = StartCoroutine(CountdownCoroutine());
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
        // マスタークライアントのPlayerAvatarを取得
        PlayerAvatar masterPlayerAvatar = GetMasterPlayerAvatar();
        
        for (int i = COUNTDOWN_SECONDS; i > 0; i--)
        {
            Debug.Log($"GameController: Game starting in {i} seconds...");
            
            // ローカルでイベント発火
            GameEvents.TriggerCountdownUpdate(i);
            
            // RPCで他のクライアントにも送信
            if (masterPlayerAvatar != null)
            {
                masterPlayerAvatar.NotifyCountdownUpdate(i);
            }
            
            yield return new WaitForSeconds(1f);
        }
        
        // カウントダウン完了、ゲーム開始
        isCountdownRunning = false;
        CurrentGameState = GameState.InGame;
        Debug.Log("GameController: Game started!");
        
        // ローカルでゲーム状態変更
        GameEvents.TriggerGameStateChanged(GameState.InGame);
        
        // RPCで他のクライアントにゲーム開始を通知
        if (masterPlayerAvatar != null)
        {
            masterPlayerAvatar.NotifyGameStart();
        }
        
        // ローカルで全プレイヤーの操作を有効化
        EnableAllPlayersInput(true);
        
        // RPCで他のクライアントの操作も有効化
        if (masterPlayerAvatar != null)
        {
            masterPlayerAvatar.NotifyEnableAllPlayersInput(true);
        }
    }
    
    // マスタークライアントのPlayerAvatarを取得
    private PlayerAvatar GetMasterPlayerAvatar()
    {
        if (playerManager != null)
        {
            foreach (var playerPair in playerManager.AllPlayers)
            {
                var player = playerPair.Value;
                if (player != null && player.HasStateAuthority)
                {
                    return player;
                }
            }
        }
        return null;
    }

    // プレイヤー操作状態変更時の処理
    private void OnPlayerInputStateChanged(bool enabled)
    {
        Debug.Log($"GameController: OnPlayerInputStateChanged called - enabled: {enabled}");
        EnableAllPlayersInput(enabled);
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
        
        // ゲーム再開イベント購読解除
        GameEvents.OnGameRestartRequested -= RestartGame;
        GameEvents.OnGameRestartExecution -= ExecuteRestart;
        GameEvents.OnPlayerClickedForRestart -= OnPlayerClickedForRestart;
        GameEvents.OnPlayerInputStateChanged -= OnPlayerInputStateChanged;
    }
}
