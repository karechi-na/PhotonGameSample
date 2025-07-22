using UnityEngine;
using UnityEngine.SceneManagement;
using Fusion;
using TMPro;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(ItemManager), typeof(PlayerManager))]
/// <summary>
/// GameController is responsible for managing the game state and handling player interactions.
/// Stay within the maximum player limit and manage player models.
/// </summary>
public class GameController : MonoBehaviour
{
    const int MAX_PLAYERS = 2; // Maximum number of players allowed in the game

    // プレイヤーIDとスコアUIの対応を保持
    private Dictionary<int, TextMeshProUGUI> playerScoreTexts = new Dictionary<int, TextMeshProUGUI>();

    // ゲーム終了管理
    private bool gameEnded = false;

    [SerializeField] private TextMeshProUGUI statusWindow;

    /// <summary>
    /// external references to managers components.
    /// </summary>
    [SerializeField] private ItemManager itemManager;
    [SerializeField] private NetworkGameManager networkGameManager;
    [SerializeField] private PlayerManager playerManager; // PlayerManagerとして直接宣言

    /// <summary>
    /// PlayerModel UI components
    /// </summary>
    [SerializeField] private TextMeshProUGUI scoreText1;
    [SerializeField] private TextMeshProUGUI scoreText2; // 二人目のプレイヤー用のスコアテキスト
    private PlayerModel localPlayerModel; // ローカルプレイヤーのモデル
    /// <summary>
    /// GameState enum defines the possible states of the game.
    /// </summary>
    public enum GameState
    {
        WaitingForPlayers,
        InGame,
        GameOver
    }

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
        Debug.Log("GameController: Awake() called");
        
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
            Debug.Log("GameController: ✅ NetworkGameManager events registered");
        }
        else
        {
            Debug.LogError("GameController: ❌ NetworkGameManager not found!");
        }

        // PlayerManagerの参照を取得してイベントを登録
        playerManager = GetComponent<PlayerManager>();
        
        if (playerManager != null)
        {
            playerManager.OnPlayerRegistered += OnPlayerRegistered;
            playerManager.OnPlayerUnregistered += OnPlayerUnregistered;
            playerManager.OnPlayerScoreChanged += OnPlayerScoreChanged;
            playerManager.OnPlayerCountChanged += OnPlayerCountChanged;
            Debug.Log("GameController: ✅ PlayerManager events registered");
        }
        else
        {
            Debug.LogError("GameController: ❌ PlayerManager not found!");
        }
        
        Debug.Log("GameController: Awake() completed");
    }

    void Start()
    {
        itemManager = GetComponent<ItemManager>();
        // UIコンポーネントの確認
        Debug.Log($"GameController UI Check - scoreText: {scoreText1}, player2ScoreText: {scoreText2}");

        // UIの辞書を初期化
        InitializePlayerScoreTexts();

        // ItemManagerの初期化
        InitializeItemManager();
    }

    private void InitializeItemManager()
    {
        if (itemManager != null)
        {
            // ItemManagerのイベントを登録
            itemManager.OnAllItemsCollected += OnAllItemsCollected;
            itemManager.OnItemCountChanged += OnItemCountChanged;
            Debug.Log("ItemManager events registered");
        }
        else
        {
            Debug.LogWarning("ItemManager is not assigned!");
        }
    }

    private void OnItemCountChanged(int collectedCount, int totalCount)
    {
        Debug.Log($"Item progress: {collectedCount}/{totalCount}");
        // 必要に応じてUIを更新
    }

    private void OnAllItemsCollected()
    {
        Debug.Log("GameController: All items collected event received");
        
        // 直接ゲーム終了を実行（全クライアントで独立して判定）
        EndGame();
    }

    // PlayerManagerからのイベントハンドラー
    private void OnPlayerRegistered(PlayerAvatar avatar)
    {
        Debug.Log($"GameController: Player {avatar.playerId} registered via PlayerManager");
        
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
        Debug.Log($"🎯 GameController: Player count changed to {playerCount}");
        
        // ゲーム状態をチェック（これが主要なゲーム状態管理トリガー）
        if (networkGameManager != null && networkGameManager.NetworkRunner != null)
        {
            CheckPlayerCountAndUpdateGameState(networkGameManager.NetworkRunner);
        }
        else
        {
            Debug.LogWarning("GameController: NetworkRunner not available for state check");
        }
    }

    private void InitializePlayerScoreTexts()
    {
        // プレイヤーIDとUIテキストの対応を設定
        if (scoreText1 != null)
        {
            playerScoreTexts[1] = scoreText1;
            Debug.Log("Registered scoreText for Player 1");
        }

        if (scoreText2 != null)
        {
            playerScoreTexts[2] = scoreText2;
            Debug.Log("Registered player2ScoreText for Player 2");
        }

        Debug.Log($"Total UI texts registered: {playerScoreTexts.Count}");
    }

    private IEnumerator RegisterExistingPlayers()
    {
        // PlayerManagerが自動的に処理するため、このメソッドは不要
        yield break;
    }

    private IEnumerator ContinuousPlayerCheck()
    {
        // PlayerManagerが自動的に処理するため、このメソッドは不要
        yield break;
    }

    private void EndGame()
    {
        Debug.Log($"GameController: EndGame called. gameEnded={gameEnded}");
        
        if (gameEnded) return;

        gameEnded = true;
        CurrentGameState = GameState.GameOver;
        Debug.Log("GameController: Game state changed to GameOver");

        // 全プレイヤーの入力を無効化
        EnableAllPlayersInput(false);

        // 勝者を決定
        DetermineWinner();
    }

    private void DetermineWinner()
    {
        Debug.Log("=== GameController: DetermineWinner called ===");
        
        if (playerManager == null)
        {
            Debug.LogError("GameController: PlayerManager is null, cannot determine winner");
            return;
        }

        // PlayerManagerから勝者情報を取得
        var winnerResult = playerManager.DetermineWinner();
        int winnerId = winnerResult.winnerId;
        int highestScore = winnerResult.highestScore;
        List<int> tiedPlayers = winnerResult.tiedPlayers;

        // 勝者の表示
        string resultMessage;
        if (tiedPlayers.Count > 1)
        {
            resultMessage = $"Draw! Players {string.Join(", ", tiedPlayers)} tied with {highestScore} points!";
        }
        else
        {
            resultMessage = $"Winner: Player {winnerId} with {highestScore} points!";
        }

        Debug.Log($"GameController: {resultMessage}");

        if (statusWindow != null)
        {
            statusWindow.text = resultMessage;
        }
    }

    /// <summary>
    /// NetworkGameManagerからの接続通知を受け取る
    /// </summary>
    private void OnClientJoined(NetworkRunner runner, PlayerRef player, bool isMasterClient)
    {
        Debug.Log($"GameController: Client joined - Player: {player.PlayerId}, IsMaster: {isMasterClient}");
        
        // プレイヤー数をチェック（ただし、実際のスポーンまで待つ）
        // CheckPlayerCountAndUpdateGameState(runner); // この時点ではスポーンされていないのでコメントアウト
        Debug.Log($"GameController: Player {player.PlayerId} joined, waiting for spawn to complete...");
    }

    /// <summary>
    /// NetworkGameManagerからのプレイヤースポーン通知を受け取る
    /// </summary>
    private void OnPlayerSpawned(PlayerAvatar playerAvatar)
    {
        Debug.Log($"🎯 GameController: Player spawned - ID: {playerAvatar.playerId}");
        Debug.Log($"GameController: Player {playerAvatar.playerId} - HasStateAuthority: {playerAvatar.HasStateAuthority}");
        Debug.Log($"GameController: Player {playerAvatar.playerId} - NickName: '{playerAvatar.NickName.Value}'");
        
        // PlayerManagerに直接登録を指示
        if (playerManager != null)
        {
            Debug.Log($"GameController: Manually registering Player {playerAvatar.playerId} to PlayerManager");
            playerManager.RegisterPlayerAvatar(playerAvatar);
        }
        else
        {
            Debug.LogError("GameController: PlayerManager is null when trying to register spawned player!");
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
        
        Debug.Log($"==== GameController: CheckPlayerCountAndUpdateGameState ====");
        Debug.Log($"GameController: Network player count: {playerCount}, Registered avatars: {registeredPlayers}");
        Debug.Log($"GameController: Current game state: {CurrentGameState}");
        
        if (playerManager != null)
        {
            Debug.Log($"GameController: PlayerManager debug info: {playerManager.GetDebugInfo()}");
        }

        // 実際に登録されたプレイヤー数でゲーム状態を判定（ネットワーク上の接続数ではなく）
        if (registeredPlayers >= MAX_PLAYERS && CurrentGameState == GameState.WaitingForPlayers)
        {
            // 二人揃ったのでゲーム開始
            CurrentGameState = GameState.InGame;
            Debug.Log("GameController: All players registered and spawned! Starting game...");

            // 全プレイヤーの操作を有効化
            EnableAllPlayersInput(true);
        }
        else if (registeredPlayers < MAX_PLAYERS)
        {
            // プレイヤーが足りない場合は待機状態
            CurrentGameState = GameState.WaitingForPlayers;
            Debug.Log($"GameController: Waiting for more players to spawn... (Network: {playerCount}/{MAX_PLAYERS}, Registered: {registeredPlayers}/{MAX_PLAYERS})");

            // 全プレイヤーの操作を無効化
            EnableAllPlayersInput(false);
        }
        
        Debug.Log($"GameController: State check complete. Final state: {CurrentGameState}");
        Debug.Log($"==== CheckPlayerCountAndUpdateGameState finished ====");
    }

    private void EnableAllPlayersInput(bool enabled)
    {
        Debug.Log($"==== GameController: EnableAllPlayersInput called with enabled={enabled} ====");
        if (playerManager != null)
        {
            Debug.Log($"GameController: PlayerManager found, total players: {playerManager.PlayerCount}");
            Debug.Log($"GameController: About to call SetAllPlayersInputEnabled({enabled})");
            playerManager.SetAllPlayersInputEnabled(enabled);
            Debug.Log($"GameController: SetAllPlayersInputEnabled({enabled}) completed");
        }
        else
        {
            Debug.LogError("GameController: PlayerManager is null!");
        }
        Debug.Log($"==== GameController: EnableAllPlayersInput finished ====");
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
        Debug.Log($"OnPlayerScoreChanged called: Player {playerId}, Score {newScore}");

        // Dictionaryから該当するUIテキストを取得
        if (playerScoreTexts.TryGetValue(playerId, out TextMeshProUGUI targetScoreText))
        {
            targetScoreText.text = $"Player{playerId} Score: {newScore}";
            Debug.Log($"Updated UI for Player {playerId}: {targetScoreText.text}");
        }
        else
        {
            Debug.LogWarning($"No UI text found for Player {playerId}. Available players: {string.Join(", ", playerScoreTexts.Keys)}");
        }

        Debug.Log($"Player {playerId} score updated to: {newScore}");
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
                if (statusWindow != null)
                {
                    int currentPlayers = playerManager != null ? playerManager.PlayerCount : 0;
                    statusWindow.text = $"Waiting for players... ({currentPlayers}/{MAX_PLAYERS})";
                }
                // ゲーム状態をリセット
                ResetGameState();
                break;
            case GameState.InGame:
                Debug.Log("Game is now in progress.");
                if (statusWindow != null)
                {
                    statusWindow.text = "Game in Progress!";
                }
                Debug.Log("OnChangeState: About to enable all players input...");
                EnableAllPlayersInput(true);
                break;
            case GameState.GameOver:
                Debug.Log("Game Over!");
                // 勝者決定はEndGame()で既に実行済み
                StartCoroutine(RestartGameAfterDelay());
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
    }
}
