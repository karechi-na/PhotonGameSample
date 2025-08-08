using UnityEngine;
using TMPro;
using System.Collections.Generic;

public class GameUIManager : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI statusWindow;
    
    [Header("Player Score UI References")]
    [SerializeField] private TextMeshProUGUI player1ScoreText;
    [SerializeField] private TextMeshProUGUI player2ScoreText;

    private Dictionary<int, TextMeshProUGUI> playerScoreTexts = new Dictionary<int, TextMeshProUGUI>();
    
    // 参照（フォールバック用）
    private NetworkGameManager networkGameManager;
    private GameSyncManager gameSyncManager;
    private PlayerManager playerManager;
    private bool winnerMessageDisplayed = false; // 勝者メッセージが表示されているかのフラグ
    private bool isWaitingForRestart = false; // 再開待ち状態のフラグ
    private bool hasClickedForRestart = false; // 自分がクリックしたかのフラグ
    
    // デバッグ用：UpdatePlayerScoreUI呼び出し回数をトラッキング
    private int updateScoreUICallCount = 0;

    void Awake()
    {
        // UIの辞書を初期化
        InitializePlayerScoreTexts();

    // 参照の取得（存在すれば）
    networkGameManager = FindFirstObjectByType<NetworkGameManager>();
    playerManager = FindFirstObjectByType<PlayerManager>();
    gameSyncManager = FindFirstObjectByType<GameSyncManager>();

        // GameSyncManager のスポーン通知を受けて参照を更新
        if (networkGameManager != null)
        {
            networkGameManager.OnGameSyncManagerSpawned += OnGameSyncManagerSpawned;
        }

        // イベント購読
        GameEvents.OnGameStateChanged += UpdateStatusWindow;
        GameEvents.OnPlayerScoreChanged += UpdatePlayerScoreUI;
        GameEvents.OnWinnerDetermined += DisplayWinnerMessage;
        GameEvents.OnPlayerCountChanged += UpdateWaitingStatus;
        GameEvents.OnPlayerRegistered += CreatePlayerScoreUI; // プレイヤー登録時にUI作成
        GameEvents.OnCountdownUpdate += DisplayCountdown; // カウントダウン表示
    }

    void Start()
    {
        // 初期UIの状態を設定
        UpdateStatusWindow(GameState.WaitingForPlayers);
    }
    
    void Update()
    {
        // ゲーム再開待ち状態でクリックを検知（一度だけ）
        if (isWaitingForRestart && !hasClickedForRestart && Input.GetMouseButtonDown(0))
        {
            RequestGameRestart();
        }
    }

    private void InitializePlayerScoreTexts()
    {
        // SerializeFieldで設定されたプレイヤースコアUIを辞書に登録
        if (player1ScoreText != null)
        {
            playerScoreTexts[1] = player1ScoreText;
            player1ScoreText.text = "Player1 Score: 0";
        }
        else
        {
            Debug.LogWarning("GameUIManager: Player1 score text is not assigned!");
        }

        if (player2ScoreText != null)
        {
            playerScoreTexts[2] = player2ScoreText;
            player2ScoreText.text = "Player2 Score: 0";
        }
        else
        {
            Debug.LogWarning("GameUIManager: Player2 score text is not assigned!");
        }
        
        Debug.Log($"GameUIManager: Initialized {playerScoreTexts.Count} player score UI elements");
    }

    private void UpdateStatusWindow(GameState newState)
    {
        Debug.Log($"GameUIManager: UpdateStatusWindow called with state: {newState}");
        Debug.Log($"GameUIManager: winnerMessageDisplayed = {winnerMessageDisplayed}");
        
        if (statusWindow == null)
        {
            Debug.LogError("GameUIManager: statusWindow is null!");
            return;
        }

        // 勝者メッセージが表示されている場合は、ゲーム状態の更新を無視
        if (winnerMessageDisplayed)
        {
            Debug.Log("GameUIManager: Ignoring state update - winner message is displayed");
            return;
        }

        switch (newState)
        {
            case GameState.WaitingForPlayers:
                statusWindow.text = "Waiting for players...";
                Debug.Log("GameUIManager: Set text to 'Waiting for players...'");
                break;
            case GameState.CountdownToStart:
                // カウントダウン中はカウントダウン表示が優先される
                Debug.Log("GameUIManager: CountdownToStart state - waiting for countdown display");
                break;
            case GameState.InGame:
                statusWindow.text = "Game is running";
                Debug.Log("GameUIManager: Set text to 'Game is running'");
                break;
            case GameState.GameOver:
                // ゲーム終了時は勝者メッセージが先に表示されるべきなので、
                // ここでは何も表示しない
                Debug.Log("GameUIManager: GameOver state - waiting for winner message");
                break;
            case GameState.WaitingForRestart:
                statusWindow.text = "Click anywhere to restart game";
                isWaitingForRestart = true;
                hasClickedForRestart = false; // 新しい再開待ちで毎回リセット
                Debug.Log("GameUIManager: Set text to 'Click anywhere to restart game'");
                break;
        }
    }

    private void UpdateWaitingStatus(int playerCount)
    {
        // GameControllerからの参照を持つか、GameEventsを経由して現在の状態を取得
        if (statusWindow != null)
        {
            statusWindow.text = $"Waiting for players... ({playerCount}/2)";
        }
    }

    private void CreatePlayerScoreUI(int playerId)
    {
        if (playerScoreTexts.ContainsKey(playerId))
        {
            return;
        }

        // SerializeFieldで設定されているはずのUIを確認
        TextMeshProUGUI scoreText = null;
        if (playerId == 1 && player1ScoreText != null)
        {
            scoreText = player1ScoreText;
        }
        else if (playerId == 2 && player2ScoreText != null)
        {
            scoreText = player2ScoreText;
        }

        if (scoreText != null)
        {
            playerScoreTexts[playerId] = scoreText;
            scoreText.text = $"Player{playerId} Score: 0";
            Debug.Log($"GameUIManager: Registered score UI for Player {playerId}");
        }
        else
        {
            Debug.LogError($"GameUIManager: No SerializeField reference found for Player {playerId}!");
        }
    }

    private void UpdatePlayerScoreUI(int playerId, int newScore)
    {
        updateScoreUICallCount++;
        
        if (playerScoreTexts.TryGetValue(playerId, out TextMeshProUGUI targetScoreText))
        {
            targetScoreText.text = $"Player{playerId} Score: {newScore}";
        }
        else
        {
            Debug.LogWarning($"GameUIManager: No UI text found for Player {playerId}");
            // UI作成を試みる
            CreatePlayerScoreUI(playerId);
            
            // 再度更新を試みる
            if (playerScoreTexts.TryGetValue(playerId, out targetScoreText))
            {
                targetScoreText.text = $"Player{playerId} Score: {newScore}";
            }
        }
    }

    private void DisplayWinnerMessage(string message)
    {
        if (statusWindow != null)
        {
            statusWindow.text = message;
            winnerMessageDisplayed = true; // フラグを設定
            
            // 3秒後に再開待ち状態に移行
            StartCoroutine(ShowRestartMessageAfterDelay());
        }
        else
        {
            Debug.LogError("GameUIManager: statusWindow is null!");
        }
    }
    
    // 勝者メッセージ表示後、一定時間後に再開メッセージを表示
    private System.Collections.IEnumerator ShowRestartMessageAfterDelay()
    {
        yield return new WaitForSeconds(3f);
        
        if (statusWindow != null)
        {
            statusWindow.text = "Click anywhere to restart game";
            isWaitingForRestart = true;
        }
    }
    
    // ゲーム再開要求
    private void RequestGameRestart()
    {
        Debug.Log("=== GameUIManager: RequestGameRestart() called ===");
        Debug.Log($"GameUIManager: TIMESTAMP: {System.DateTime.Now:HH:mm:ss.fff}");
        Debug.Log($"GameUIManager: isWaitingForRestart = {isWaitingForRestart}");
        Debug.Log($"GameUIManager: hasClickedForRestart = {hasClickedForRestart}");
        
        if (isWaitingForRestart && !hasClickedForRestart)
        {
            hasClickedForRestart = true;
            Debug.Log("GameUIManager: Setting hasClickedForRestart = true");
            
            // ローカルプレイヤーのIDを取得
            int localPlayerId = GetLocalPlayerId();
            Debug.Log($"GameUIManager: GetLocalPlayerId() returned: {localPlayerId}");
            
            if (localPlayerId > 0)
            {
                Debug.Log($"GameUIManager: Local player {localPlayerId} clicked for restart");
                
                // まずは GameSyncManager 経由でマスターに集約
                if (gameSyncManager == null)
                {
                    // 遅延スポーン対策でクリック時に再解決
                    gameSyncManager = FindFirstObjectByType<GameSyncManager>();
                    Debug.Log($"GameUIManager: Lazy-resolved GameSyncManager: {(gameSyncManager != null)}");
                }

                if (gameSyncManager != null)
                {
                    Debug.Log($"GameUIManager: Using GameSyncManager.RequestRestartClick({localPlayerId})");
                    gameSyncManager.RequestRestartClick(localPlayerId);
                }
                else
                {
                    // 次にローカル PlayerAvatar RPC を試す
                    PlayerAvatar localPlayer = GetLocalPlayerAvatar();
                    Debug.Log($"GameUIManager: GameSyncManager not found. GetLocalPlayerAvatar() returned: {(localPlayer != null ? $"Player {localPlayer.playerId}" : "null")}");
                    if (localPlayer != null)
                    {
                        localPlayer.RPC_NotifyPlayerClickForRestart();
                        Debug.Log($"GameUIManager: Called RPC_NotifyPlayerClickForRestart on Player {localPlayer.playerId}");
                    }
                    else
                    {
                        // 最後のフォールバック：ローカルのみイベント発火
                        Debug.LogWarning("GameUIManager: No GameSyncManager/PlayerAvatar found - using local GameEvents fallback");
                        GameEvents.TriggerPlayerClickedForRestart(localPlayerId);
                    }
                }
                
                // UI表示を更新
                if (statusWindow != null)
                {
                    statusWindow.text = "Waiting for other players to click...";
                    Debug.Log("GameUIManager: Updated UI to 'Waiting for other players to click...'");
                }
            }
            else
            {
                Debug.LogError("GameUIManager: Could not determine local player ID for restart");
                // クリックフラグをリセット（再試行可能にする）
                hasClickedForRestart = false;
            }
        }
        else
        {
            Debug.Log($"GameUIManager: RequestGameRestart conditions not met - isWaitingForRestart: {isWaitingForRestart}, hasClickedForRestart: {hasClickedForRestart}");
        }
    }

    // NetworkGameManager からの GameSyncManager スポーン通知
    private void OnGameSyncManagerSpawned(GameSyncManager spawned)
    {
        gameSyncManager = spawned;
        Debug.Log("GameUIManager: OnGameSyncManagerSpawned - GameSyncManager reference updated");
    }
    
    // ローカルプレイヤーのIDを取得
    private int GetLocalPlayerId()
    {
        // InputAuthorityを持つPlayerAvatarを探す（StateAuthorityではなく）
        PlayerAvatar[] allPlayers = FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None);
        
        Debug.Log($"GameUIManager: Found {allPlayers.Length} players in scene");
        
        foreach (var player in allPlayers)
        {
            if (player != null)
            {
                Debug.Log($"GameUIManager: Player {player.playerId} - HasInputAuthority: {player.HasInputAuthority}, HasStateAuthority: {player.HasStateAuthority}");
                // InputAuthorityを持つプレイヤーがローカルプレイヤー
                if (player.HasInputAuthority)
                {
                    Debug.Log($"GameUIManager: Local player found via InputAuthority - ID: {player.playerId}");
                    return player.playerId;
                }
            }
        }
        
        // フォールバック1: NetworkRunnerのLocalPlayerIdを使用
        if (networkGameManager != null && networkGameManager.NetworkRunner != null)
        {
            var runner = networkGameManager.NetworkRunner;
            int localId = runner.LocalPlayer.PlayerId;
            if (localId > 0)
            {
                Debug.Log($"GameUIManager: Fallback via NetworkRunner.LocalPlayer -> ID {localId}");
                return localId;
            }
        }

        // フォールバック2: PlayerManagerの登録から、HasInputAuthorityのものを探す
        if (playerManager != null)
        {
            foreach (var kv in playerManager.AllPlayers)
            {
                var avatar = kv.Value;
                if (avatar != null && avatar.HasInputAuthority)
                {
                    Debug.Log($"GameUIManager: Fallback via PlayerManager.AllPlayers -> ID {avatar.playerId}");
                    return avatar.playerId;
                }
            }
        }

        // それでも見つからない場合は-1
        Debug.LogError("GameUIManager: No local player with InputAuthority found!");
        return -1;
    }

    // カウントダウン表示
    private void DisplayCountdown(int remainingSeconds)
    {
        Debug.Log($"GameUIManager: DisplayCountdown called with {remainingSeconds} seconds");
        
        if (statusWindow != null)
        {
            statusWindow.text = $"Game starting in {remainingSeconds}...";
            Debug.Log($"GameUIManager: Set countdown text to 'Game starting in {remainingSeconds}...'");
        }
        else
        {
            Debug.LogError("GameUIManager: statusWindow is null in DisplayCountdown!");
        }
    }

    // ローカルプレイヤーのPlayerAvatarを取得
    private PlayerAvatar GetLocalPlayerAvatar()
    {
        // まずシーン上から検索
        PlayerAvatar[] allPlayers = FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None);
        foreach (var player in allPlayers)
        {
            if (player != null && player.HasInputAuthority)
            {
                return player;
            }
        }

        // フォールバック: PlayerManagerの登録から検索
        if (playerManager != null)
        {
            foreach (var kv in playerManager.AllPlayers)
            {
                var avatar = kv.Value;
                if (avatar != null && avatar.HasInputAuthority)
                {
                    return avatar;
                }
            }
        }

        return null;
    }
    
    // 勝者メッセージフラグをリセット（ゲーム再開時に使用）
    public void ResetWinnerMessageFlag()
    {
        winnerMessageDisplayed = false;
        isWaitingForRestart = false;
        hasClickedForRestart = false; // クリック済みフラグもリセット
    }

    void OnDestroy()
    {
        if (networkGameManager != null)
        {
            networkGameManager.OnGameSyncManagerSpawned -= OnGameSyncManagerSpawned;
        }
        GameEvents.OnGameStateChanged -= UpdateStatusWindow;
        GameEvents.OnPlayerScoreChanged -= UpdatePlayerScoreUI;
        GameEvents.OnWinnerDetermined -= DisplayWinnerMessage;
        GameEvents.OnPlayerCountChanged -= UpdateWaitingStatus;
        GameEvents.OnPlayerRegistered -= CreatePlayerScoreUI;
        GameEvents.OnCountdownUpdate -= DisplayCountdown;
    }
}
