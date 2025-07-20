using UnityEngine;
using UnityEngine.SceneManagement;
using Fusion;
using TMPro;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(ItemManager))]
/// <summary>
/// GameController is responsible for managing the game state and handling player interactions.
/// Stay within the maximum player limit and manage player models.
/// </summary>
public class GameController : MonoBehaviour
{
    const int MAX_PLAYERS = 2; // Maximum number of players allowed in the game

    // 全プレイヤーのアバター参照を保持
    private Dictionary<int, PlayerAvatar> allPlayerAvatars = new Dictionary<int, PlayerAvatar>();

    // プレイヤーIDとスコアUIの対応を保持
    private Dictionary<int, TextMeshProUGUI> playerScoreTexts = new Dictionary<int, TextMeshProUGUI>();

    // ゲーム終了管理
    private bool gameEnded = false;

    [SerializeField] private TextMeshProUGUI statusWindow;

    /// <summary>
    /// external references to ItemManager and NetworkGameManager components.
    /// </summary>
    [SerializeField] private ItemManager itemManager;
    [SerializeField]private NetworkGameManager networkGameManager;


    /// <summary>
    /// PlayerModel UI components
    /// </summary>
    [SerializeField] private TextMeshProUGUI scoreText1;
    [SerializeField] private TextMeshProUGUI ScoreText2; // 二人目のプレイヤー用のスコアテキスト
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
            Debug.Log("GameController: NetworkGameManager events registered");
        }
        else
        {
            Debug.LogError("GameController: NetworkGameManager not found!");
        }
    }

    void Start()
    {
        itemManager = GetComponent<ItemManager>();
        // UIコンポーネントの確認
        Debug.Log($"GameController UI Check - scoreText: {scoreText1}, player2ScoreText: {ScoreText2}");

        // UIの辞書を初期化
        InitializePlayerScoreTexts();

        // ItemManagerの初期化
        InitializeItemManager();

        // 既存のPlayerAvatarがあれば登録
        StartCoroutine(RegisterExistingPlayers());
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

    private void InitializePlayerScoreTexts()
    {
        // プレイヤーIDとUIテキストの対応を設定
        if (scoreText1 != null)
        {
            playerScoreTexts[1] = scoreText1;
            Debug.Log("Registered scoreText for Player 1");
        }

        if (ScoreText2 != null)
        {
            playerScoreTexts[2] = ScoreText2;
            Debug.Log("Registered player2ScoreText for Player 2");
        }

        Debug.Log($"Total UI texts registered: {playerScoreTexts.Count}");
    }

    private IEnumerator RegisterExistingPlayers()
    {
        yield return new WaitForSeconds(0.5f); // 少し待ってからプレイヤーを検索

        // 既存のプレイヤーを登録
        var existingAvatars = FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None);
        Debug.Log($"Found {existingAvatars.Length} existing PlayerAvatars");

        foreach (var avatar in existingAvatars)
        {
            RegisterPlayerAvatar(avatar);
        }

        // 継続的にプレイヤーをチェック（新しいプレイヤーが参加した場合のため）
        StartCoroutine(ContinuousPlayerCheck());
    }

    private IEnumerator ContinuousPlayerCheck()
    {
        while (true)
        {
            yield return new WaitForSeconds(1.0f); // 1秒ごとにチェック

            var allAvatars = FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None);
            foreach (var avatar in allAvatars)
            {
                if (avatar != null && !allPlayerAvatars.ContainsKey(avatar.playerId))
                {
                    Debug.Log($"Found new player {avatar.playerId}, registering...");
                    RegisterPlayerAvatar(avatar);
                }
            }
        }
    }

    private void RegisterPlayerAvatar(PlayerAvatar avatar)
    {
        if (avatar != null && !allPlayerAvatars.ContainsKey(avatar.playerId))
        {
            allPlayerAvatars[avatar.playerId] = avatar;
            avatar.OnScoreChanged += OnPlayerScoreChanged;

            // 初期状態では入力を無効化
            avatar.SetInputEnabled(CurrentGameState == GameState.InGame);

            Debug.Log($"GameController: Registered Player {avatar.playerId} for score updates. Total players: {allPlayerAvatars.Count}");
            Debug.Log($"GameController: Player {avatar.playerId} current score: {avatar.Score}");

            // 即座にUIを初期化
            OnPlayerScoreChanged(avatar.playerId, avatar.Score);

            // ItemManagerにプレイヤーを登録（ItemManagerが直接ItemCatcherイベントを処理）
            if (itemManager != null)
            {
                itemManager.RegisterPlayer(avatar);
            }
            
            // プレイヤー登録後にゲーム状態を再確認
            Debug.Log($"GameController: Current game state after registration: {CurrentGameState}");
        }
        else if (avatar != null)
        {
            Debug.Log($"GameController: Player {avatar.playerId} already registered or avatar is null");
        }
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
        Debug.Log("=== DetermineWinner called ===");
        int highestScore = -1;
        int winnerId = -1;
        List<int> tiedPlayers = new List<int>();

        // まず全プレイヤーの詳細なスコア情報をログ出力
        Debug.Log($"Total registered players: {allPlayerAvatars.Count}");
        foreach (var avatarPair in allPlayerAvatars)
        {
            var avatar = avatarPair.Value;
            if (avatar != null)
            {
                int score = avatar.Score;
                Debug.Log($"=== Player {avatarPair.Key} final score: {score} ===");
                Debug.Log($"Player {avatarPair.Key} HasStateAuthority: {avatar.HasStateAuthority}");
                Debug.Log($"Player {avatarPair.Key} NickName: {avatar.NickName.Value}");

                if (score > highestScore)
                {
                    highestScore = score;
                    winnerId = avatarPair.Key;
                    tiedPlayers.Clear();
                    tiedPlayers.Add(winnerId);
                    Debug.Log($"New highest score: Player {winnerId} with {highestScore} points");
                }
                else if (score == highestScore)
                {
                    tiedPlayers.Add(avatarPair.Key);
                    Debug.Log($"Tie detected: Player {avatarPair.Key} also has {score} points");
                }
            }
            else
            {
                Debug.LogWarning($"Player {avatarPair.Key} avatar is null!");
            }
        }

        Debug.Log($"Final calculation - Highest Score: {highestScore}, Winner: {winnerId}, Tied Players: [{string.Join(", ", tiedPlayers)}]");

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

        Debug.Log(resultMessage);

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
        
        // プレイヤー数をチェックしてゲーム状態を更新
        CheckPlayerCountAndUpdateGameState(runner);
    }

    /// <summary>
    /// NetworkGameManagerからのプレイヤースポーン通知を受け取る
    /// </summary>
    private void OnPlayerSpawned(PlayerAvatar playerAvatar)
    {
        Debug.Log($"GameController: Player spawned - {playerAvatar.playerId}");
        RegisterPlayerAvatar(playerAvatar);
        
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
        Debug.Log($"GameController: Current player count: {playerCount}, Registered avatars: {allPlayerAvatars.Count}");

        if (playerCount >= MAX_PLAYERS && CurrentGameState == GameState.WaitingForPlayers)
        {
            // 二人揃ったのでゲーム開始
            CurrentGameState = GameState.InGame;
            Debug.Log("GameController: All players joined! Starting game...");

            // 全プレイヤーの操作を有効化
            EnableAllPlayersInput(true);
        }
        else if (playerCount < MAX_PLAYERS)
        {
            // プレイヤーが足りない場合は待機状態
            CurrentGameState = GameState.WaitingForPlayers;
            Debug.Log($"GameController: Waiting for more players... ({playerCount}/{MAX_PLAYERS})");

            // 全プレイヤーの操作を無効化
            EnableAllPlayersInput(false);
        }
    }

    private void EnableAllPlayersInput(bool enabled)
    {
        foreach (var avatarPair in allPlayerAvatars)
        {
            var avatar = avatarPair.Value;
            if (avatar != null)
            {
                avatar.SetInputEnabled(enabled);
                Debug.Log($"Player {avatarPair.Key} input {(enabled ? "enabled" : "disabled")}");
            }
        }
    }

    // プレイヤー離脱時の処理（GameLauncherから呼び出される）
    public void OnPlayerLeft(int playerId)
    {
        if (allPlayerAvatars.ContainsKey(playerId))
        {
            Debug.Log($"Player {playerId} left the game");

            // プレイヤーを辞書から削除
            allPlayerAvatars.Remove(playerId);

            // プレイヤー数が足りなくなったら待機状態に戻す
            if (allPlayerAvatars.Count < MAX_PLAYERS && CurrentGameState == GameState.InGame)
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
        // デバッグ用：キー入力でスコアを手動変更
        if (Input.GetKeyDown(KeyCode.F1))
        {
            TestScoreUpdate(1);
        }
        if (Input.GetKeyDown(KeyCode.F2))
        {
            TestScoreUpdate(2);
        }

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

    private void TestScoreUpdate(int playerId)
    {
        Debug.Log($"Manual test: updating score for Player {playerId}");
        OnPlayerScoreChanged(playerId, 999);
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
                    statusWindow.text = $"Waiting for players... ({allPlayerAvatars.Count}/{MAX_PLAYERS})";
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

    private System.Collections.IEnumerator RestartGameAfterDelay()
    {
        yield return new WaitForSeconds(5.0f); // 5秒間結果を表示

        // ゲームを再開（プレイヤーが2人いる場合）
        if (allPlayerAvatars.Count >= MAX_PLAYERS)
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
    }
}
