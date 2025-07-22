using UnityEngine;
using TMPro;
using System.Collections.Generic;

public class GameUIManager : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI statusWindow;
    [SerializeField] private GameObject playerScorePrefab;
    [SerializeField] private Transform playerScoreParent;

    private Dictionary<int, TextMeshProUGUI> playerScoreTexts = new Dictionary<int, TextMeshProUGUI>();
    private bool winnerMessageDisplayed = false; // 勝者メッセージが表示されているかのフラグ

    void Awake()
    {
        // UIの辞書を初期化
        InitializePlayerScoreTexts();

        // イベント購読
        GameEvents.OnGameStateChanged += UpdateStatusWindow;
        GameEvents.OnPlayerScoreChanged += UpdatePlayerScoreUI;
        GameEvents.OnWinnerDetermined += DisplayWinnerMessage;
        GameEvents.OnPlayerCountChanged += UpdateWaitingStatus;
    }

    void Start()
    {
        // 初期UIの状態を設定
        UpdateStatusWindow(GameState.WaitingForPlayers);
    }

    private void InitializePlayerScoreTexts()
    {
        // プレイヤースコア用のUIテキストが既に存在する場合は辞書に登録
        // 動的にプレイヤーが追加される場合は OnPlayerRegistered で作成
        Debug.Log($"GameUIManager: Initialized player score texts dictionary");
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

    private void UpdatePlayerScoreUI(int playerId, int newScore)
    {
        if (playerScoreTexts.TryGetValue(playerId, out TextMeshProUGUI targetScoreText))
        {
            targetScoreText.text = $"Player{playerId} Score: {newScore}";
            Debug.Log($"GameUIManager: Updated UI for Player {playerId}: {targetScoreText.text}");
        }
        else
        {
            Debug.LogWarning($"GameUIManager: No UI text found for Player {playerId}.");
        }
    }

    private void DisplayWinnerMessage(string message)
    {
        Debug.Log($"GameUIManager: DisplayWinnerMessage called with: {message}");
        if (statusWindow != null)
        {
            statusWindow.text = message;
            winnerMessageDisplayed = true; // フラグを設定
            Debug.Log($"GameUIManager: Winner message displayed and flag set: {message}");
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
    }
}
