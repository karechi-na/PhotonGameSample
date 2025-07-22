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
    private bool winnerMessageDisplayed = false; // 勝者メッセージが表示されているかのフラグ
    
    // デバッグ用：UpdatePlayerScoreUI呼び出し回数をトラッキング
    private int updateScoreUICallCount = 0;

    void Awake()
    {
        // UIの辞書を初期化
        InitializePlayerScoreTexts();

        // イベント購読
        GameEvents.OnGameStateChanged += UpdateStatusWindow;
        GameEvents.OnPlayerScoreChanged += UpdatePlayerScoreUI;
        GameEvents.OnWinnerDetermined += DisplayWinnerMessage;
        GameEvents.OnPlayerCountChanged += UpdateWaitingStatus;
        GameEvents.OnPlayerRegistered += CreatePlayerScoreUI; // プレイヤー登録時にUI作成
    }

    void Start()
    {
        // 初期UIの状態を設定
        UpdateStatusWindow(GameState.WaitingForPlayers);
    }

    private void InitializePlayerScoreTexts()
    {
        // SerializeFieldで設定されたプレイヤースコアUIを辞書に登録
        if (player1ScoreText != null)
        {
            playerScoreTexts[1] = player1ScoreText;
            player1ScoreText.text = "Player1 Score: 0";
            Debug.Log("GameUIManager: Registered Player1 score UI");
        }
        else
        {
            Debug.LogWarning("GameUIManager: Player1 score text is not assigned!");
        }

        if (player2ScoreText != null)
        {
            playerScoreTexts[2] = player2ScoreText;
            player2ScoreText.text = "Player2 Score: 0";
            Debug.Log("GameUIManager: Registered Player2 score UI");
        }
        else
        {
            Debug.LogWarning("GameUIManager: Player2 score text is not assigned!");
        }
        
        Debug.Log($"GameUIManager: Initialized {playerScoreTexts.Count} player score UI elements");
    }

    private void UpdateStatusWindow(GameState newState)
    {
        if (statusWindow == null) return;

        // 勝者メッセージが表示されている場合は、ゲーム状態の更新を無視
        if (winnerMessageDisplayed)
        {
            Debug.Log($"GameUIManager: Winner message displayed, ignoring state change to {newState}");
            return;
        }

        switch (newState)
        {
            case GameState.WaitingForPlayers:
                statusWindow.text = "Waiting for players...";
                Debug.Log("GameUIManager: Waiting for players");
                break;
            case GameState.InGame:
                statusWindow.text = "Game is running";
                Debug.Log("GameUIManager: Game in progress");
                break;
            case GameState.GameOver:
                Debug.Log("GameUIManager: Game over - not updating status window text");
                // ゲーム終了時は勝者メッセージが先に表示されるべきなので、
                // ここでは何も表示しない
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

    // プレイヤー登録時の処理（SerializeField使用時は事前作成済みなので確認のみ）
    private void CreatePlayerScoreUI(int playerId)
    {
        Debug.Log($"GameUIManager: Checking score UI for Player {playerId}");
        
        if (playerScoreTexts.ContainsKey(playerId))
        {
            Debug.Log($"GameUIManager: Score UI for Player {playerId} already exists");
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
            Debug.Log($"GameUIManager: ✅ Registered score UI for Player {playerId}");
        }
        else
        {
            Debug.LogError($"GameUIManager: No SerializeField reference found for Player {playerId}! " +
                          $"Please assign player{playerId}ScoreText in the inspector.");
        }
    }

    private void UpdatePlayerScoreUI(int playerId, int newScore)
    {
        updateScoreUICallCount++;
        Debug.Log($"=== GameUIManager.UpdatePlayerScoreUI #{updateScoreUICallCount} === Player {playerId}, Score {newScore}" +
                  $"\n  Unity Frame: {Time.frameCount}, Time: {Time.time:F3}s" +
                  $"\n  Available score UIs: [{string.Join(", ", playerScoreTexts.Keys)}]" +
                  $"\n  Stack trace: {System.Environment.StackTrace}");
        
        if (playerScoreTexts.TryGetValue(playerId, out TextMeshProUGUI targetScoreText))
        {
            string oldText = targetScoreText.text;
            targetScoreText.text = $"Player{playerId} Score: {newScore}";
            Debug.Log($"GameUIManager: ✅ Updated UI #{updateScoreUICallCount} for Player {playerId}: '{oldText}' -> '{targetScoreText.text}'");
        }
        else
        {
            Debug.LogWarning($"GameUIManager: ❌ No UI text found for Player {playerId}. " +
                           $"Available players: [{string.Join(", ", playerScoreTexts.Keys)}]");
            // UI作成を試みる
            Debug.Log($"GameUIManager: Attempting to create missing UI for Player {playerId}");
            CreatePlayerScoreUI(playerId);
            
            // 再度更新を試みる
            if (playerScoreTexts.TryGetValue(playerId, out targetScoreText))
            {
                targetScoreText.text = $"Player{playerId} Score: {newScore}";
                Debug.Log($"GameUIManager: ✅ Created and updated UI for Player {playerId}: {targetScoreText.text}");
            }
        }
    }

    private void DisplayWinnerMessage(string message)
    {
        Debug.Log($"GameUIManager: DisplayWinnerMessage called with: {message}");
        if (statusWindow != null)
        {
            statusWindow.text = message;
            winnerMessageDisplayed = true; // フラグを設定
            
            // 引き分けかどうかを判定してログ出力
            if (message.Contains("引き分け"))
            {
                Debug.Log($"GameUIManager: ✅ Tie game message displayed: {message}");
            }
            else
            {
                Debug.Log($"GameUIManager: ✅ Winner message displayed: {message}");
            }
        }
        else
        {
            Debug.LogError("GameUIManager: statusWindow is null!");
        }
    }

    // 勝者メッセージフラグをリセット（ゲーム再開時に使用）
    public void ResetWinnerMessageFlag()
    {
        winnerMessageDisplayed = false;
        Debug.Log("GameUIManager: Winner message flag reset");
    }

    void OnDestroy()
    {
        GameEvents.OnGameStateChanged -= UpdateStatusWindow;
        GameEvents.OnPlayerScoreChanged -= UpdatePlayerScoreUI;
        GameEvents.OnWinnerDetermined -= DisplayWinnerMessage;
        GameEvents.OnPlayerCountChanged -= UpdateWaitingStatus;
        GameEvents.OnPlayerRegistered -= CreatePlayerScoreUI;
    }
}
