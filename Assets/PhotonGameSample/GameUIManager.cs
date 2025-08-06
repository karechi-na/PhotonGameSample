using UnityEngine;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// ゲームのUI表示全般（スコア、状態メッセージ、カウントダウン等）を管理するクラス。
/// ゲーム進行に応じたUI更新やイベント購読を担当します。
/// </summary>
public class GameUIManager : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI statusWindow;

    [Header("Player Score UI References")]
    [SerializeField] private TextMeshProUGUI player1ScoreText;
    [SerializeField] private TextMeshProUGUI player2ScoreText;

    private Dictionary<int, TextMeshProUGUI> playerScoreTexts = new Dictionary<int, TextMeshProUGUI>();
    private bool winnerMessageDisplayed = false; // 勝者メッセージが表示されているかのフラグ
    private bool isWaitingForRestart = false; // 再開待ち状態のフラグ
    private bool hasClickedForRestart = false; // 自分がクリックしたかのフラグ

    // デバッグ用：UpdatePlayerScoreUI呼び出し回数をトラッキング
    private int updateScoreUICallCount = 0;

    /// <summary>
    /// UIの初期化処理を行います。
    /// </summary>
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
        GameEvents.OnCountdownUpdate += DisplayCountdown; // カウントダウン表示
    }

    /// <summary>
    /// UIの初期状態を設定します。
    /// </summary>
    void Start()
    {
        // 初期UIの状態を設定
        UpdateStatusWindow(GameState.WaitingForPlayers);
    }

    /// <summary>
    /// 毎フレームUIの状態を更新します。
    /// </summary>
    void Update()
    {
        // ゲーム再開待ち状態でクリックを検知（一度だけ）
        if (isWaitingForRestart && !hasClickedForRestart && Input.GetMouseButtonDown(0))
        {
            RequestGameRestart();
        }
    }

    /// <summary>
    /// プレイヤースコアUIの辞書を初期化します。
    /// </summary>
    private void InitializePlayerScoreTexts()
    {
        // SerializeFieldで設定されたプレイヤースコアUIを辞書に登録
        if (player1ScoreText != null)
        {
            playerScoreTexts[1] = player1ScoreText;
            player1ScoreText.text = "Player1 Score: 0";
        }

        if (player2ScoreText != null)
        {
            playerScoreTexts[2] = player2ScoreText;
            player2ScoreText.text = "Player2 Score: 0";
        }
    }

    /// <summary>
    /// ゲーム状態に応じてステータスウィンドウを更新します。
    /// </summary>
    /// <param name="newState">新しいゲーム状態</param>
    private void UpdateStatusWindow(GameState newState)
    {
        if (statusWindow == null)
        {
            return;
        }

        // 勝者メッセージが表示されている場合は、ゲーム状態の更新を無視
        if (winnerMessageDisplayed)
        {
            return;
        }

        switch (newState)
        {
            case GameState.WaitingForPlayers:
                statusWindow.text = "Waiting for players...";
                break;
            case GameState.CountdownToStart:
                // カウントダウン中はカウントダウン表示が優先される
                break;
            case GameState.InGame:
                statusWindow.text = "Game is running";
                break;
            case GameState.GameOver:
                // ゲーム終了時は勝者メッセージが先に表示されるべきなので、
                // ここでは何も表示しない
                break;
            case GameState.WaitingForRestart:
                statusWindow.text = "Click anywhere to restart game";
                isWaitingForRestart = true;
                break;
        }
    }

    /// <summary>
    /// プレイヤー数に応じて待機状態を更新します。
    /// </summary>
    /// <param name="playerCount">現在のプレイヤー数</param>
    private void UpdateWaitingStatus(int playerCount)
    {
        // GameControllerからの参照を持つか、GameEventsを経由して現在の状態を取得
        if (statusWindow != null)
        {
            statusWindow.text = $"Waiting for players... ({playerCount}/2)";
        }
    }

    /// <summary>
    /// プレイヤーのスコアUIを作成します。
    /// </summary>
    /// <param name="playerId">プレイヤーID</param>
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
        }
    }

    /// <summary>
    /// プレイヤーのスコアUIを更新します。
    /// </summary>
    /// <param name="playerId">プレイヤーID</param>
    /// <param name="newScore">新しいスコア</param>
    private void UpdatePlayerScoreUI(int playerId, int newScore)
    {
        updateScoreUICallCount++;

        if (playerScoreTexts.TryGetValue(playerId, out TextMeshProUGUI targetScoreText))
        {
            targetScoreText.text = $"Player{playerId} Score: {newScore}";
        }
        else
        {
            // UI作成を試みる
            CreatePlayerScoreUI(playerId);

            // 再度更新を試みる
            if (playerScoreTexts.TryGetValue(playerId, out targetScoreText))
            {
                targetScoreText.text = $"Player{playerId} Score: {newScore}";
            }
        }
    }

    /// <summary>
    /// 勝者メッセージを表示します。
    /// </summary>
    /// <param name="message">勝者メッセージ</param>
    private void DisplayWinnerMessage(string message)
    {
        if (statusWindow != null)
        {
            statusWindow.text = message;
            winnerMessageDisplayed = true; // フラグを設定

            // 3秒後に再開待ち状態に移行
            StartCoroutine(ShowRestartMessageAfterDelay());
        }
    }

    /// <summary>
    /// 勝者メッセージ表示後、一定時間後に再開メッセージを表示します。
    /// </summary>
    private System.Collections.IEnumerator ShowRestartMessageAfterDelay()
    {
        yield return new WaitForSeconds(3f);

        if (statusWindow != null)
        {
            statusWindow.text = "Click anywhere to restart game";
            isWaitingForRestart = true;
        }
    }

    /// <summary>
    /// ゲーム再開要求を送信します。
    /// </summary>
    private void RequestGameRestart()
    {
        if (isWaitingForRestart && !hasClickedForRestart)
        {
            hasClickedForRestart = true;

            // ローカルプレイヤーのIDを取得
            int localPlayerId = GetLocalPlayerId();

            if (localPlayerId > 0)
            {
                GameSyncManager gameSyncManager = GetComponent<GameSyncManager>();
                gameSyncManager?.NotifyRestartClick(localPlayerId);
                // フォールバック：直接GameEventsを使用（ローカルのみ）
                GameEvents.TriggerPlayerClickedForRestart(localPlayerId);

                // UI表示を更新
                if (statusWindow != null)
                {
                    statusWindow.text = "Waiting for other players to click...";
                }
            }
            else
            {
                // クリックフラグをリセット（再試行可能にする）
                hasClickedForRestart = false;
            }
        }
    }

    /// <summary>
    /// ローカルプレイヤーのIDを取得します。
    /// </summary>
    /// <returns>ローカルプレイヤーのID</returns>
    private int GetLocalPlayerId()
    {
        // StateAuthorityを持つPlayerAvatarを探す
        PlayerAvatar[] allPlayers = FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None);

        foreach (var player in allPlayers)
        {
            if (player != null)
            {
                if (player.HasStateAuthority)
                {
                    return player.playerId;
                }
            }
        }

        // 見つからない場合は-1を返す
        return -1;
    }

    /// <summary>
    /// カウントダウンを表示します。
    /// </summary>
    /// <param name="remainingSeconds">残り秒数</param>
    private void DisplayCountdown(int remainingSeconds)
    {
        if (statusWindow != null)
        {
            statusWindow.text = $"Game starting in {remainingSeconds}...";
        }
    }

    /// <summary>
    /// ローカルプレイヤーのPlayerAvatarを取得します。
    /// </summary>
    /// <returns>ローカルプレイヤーのPlayerAvatar</returns>
    private PlayerAvatar GetLocalPlayerAvatar()
    {
        PlayerAvatar[] allPlayers = FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None);

        foreach (var player in allPlayers)
        {
            if (player != null && player.HasStateAuthority)
            {
                return player;
            }
        }
        return null;
    }

    /// <summary>
    /// 勝者メッセージ表示フラグをリセットします。
    /// </summary>
    public void ResetWinnerMessageFlag()
    {
        winnerMessageDisplayed = false;
        isWaitingForRestart = false;
        hasClickedForRestart = false; // クリック済みフラグもリセット
    }

    void OnDestroy()
    {
        GameEvents.OnGameStateChanged -= UpdateStatusWindow;
        GameEvents.OnPlayerScoreChanged -= UpdatePlayerScoreUI;
        GameEvents.OnWinnerDetermined -= DisplayWinnerMessage;
        GameEvents.OnPlayerCountChanged -= UpdateWaitingStatus;
        GameEvents.OnPlayerRegistered -= CreatePlayerScoreUI;
        GameEvents.OnCountdownUpdate -= DisplayCountdown;
    }
}
