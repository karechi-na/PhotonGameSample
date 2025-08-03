using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

[RequireComponent(typeof(PlayerManager))]
public class GameRuleProcessor : MonoBehaviour
{
    // イベントの発火
    public event System.Action OnGameEndTriggered; // ゲーム終了をトリガー

    private PlayerManager playerManager; // PlayerManagerの参照
    private bool isWaitingForScoreUpdate = false; // スコア更新待ちフラグ
    private float scoreUpdateTimeout = 3.0f; // スコア更新のタイムアウト（2.0秒→3.0秒に延長）
    private bool isProcessingWinnerDetermination = false; // 勝者決定処理中フラグ
    
    // 全スコア更新完了を追跡するための新しいフィールド
    private HashSet<int> completedScoreUpdates = new HashSet<int>();
    private int totalPlayerCount = 2; // プレイヤー数（動的に更新される）

    void Awake()
    {
        // PlayerManagerの参照を取得
        playerManager = GetComponent<PlayerManager>();
        if (playerManager == null)
        {
            // GameControllerでのみエラー出力
        }

        // ItemManagerからのイベント購読（全アイテム収集時）
        if (GetComponent<ItemManager>() != null) // GetComponentで取得するか、SerializeFieldでアサイン
        {
            GetComponent<ItemManager>().OnAllItemsCollected += TriggerGameEndByRule;
        }

        // GameControllerからのゲーム終了イベント購読
        GameEvents.OnGameEnd += HandleGameEndedByController;
        
        // スコア更新完了イベント購読
        GameEvents.OnScoreUpdateCompleted += OnScoreUpdateCompleted;
        
        // プレイヤー数変更イベントの購読
        GameEvents.OnPlayerCountChanged += UpdateTotalPlayerCount;
    }

    // アイテム収集によるゲーム終了のトリガー
    public void TriggerGameEndByRule()
    {
        // StateAuthorityを持つプレイヤーが存在するクライアントのみが勝敗判定を実行
        bool hasAuthorityPlayer = false;
        if (playerManager != null)
        {
            foreach (var playerPair in playerManager.AllPlayers)
            {
                if (playerPair.Value != null && playerPair.Value.HasStateAuthority)
                {
                    hasAuthorityPlayer = true;
                    break;
                }
            }
        }
        
        if (!hasAuthorityPlayer)
        {
            Debug.Log("GameRuleProcessor: No authority player on this client - skipping winner determination");
            return;
        }
        
        // ゲーム終了をGameControllerに通知
        OnGameEndTriggered?.Invoke();
        
        // スコア更新の完了を待ってから勝者決定
        isWaitingForScoreUpdate = true;
        
        // 全プレイヤーのスコア更新完了を追跡
        completedScoreUpdates.Clear();
        
        // タイムアウト処理を開始
        StartCoroutine(WaitForScoreUpdateWithTimeout());
    }

    // プレイヤー数の更新
    private void UpdateTotalPlayerCount(int playerCount)
    {
        totalPlayerCount = playerCount;
        Debug.Log($"GameRuleProcessor: Total player count updated to {totalPlayerCount}");
    }

    // GameControllerからゲーム終了の指示を受けた場合
    private void HandleGameEndedByController()
    {
        // StateAuthorityチェック
        bool hasAuthorityPlayer = false;
        if (playerManager != null)
        {
            foreach (var playerPair in playerManager.AllPlayers)
            {
                if (playerPair.Value != null && playerPair.Value.HasStateAuthority)
                {
                    hasAuthorityPlayer = true;
                    break;
                }
            }
        }
        
        if (!hasAuthorityPlayer)
        {
            Debug.Log("GameRuleProcessor: No authority player on this client - skipping winner determination");
            return;
        }
        
        // すでにスコア更新待ち状態の場合は何もしない（重複防止）
        if (isWaitingForScoreUpdate)
        {
            Debug.Log("GameRuleProcessor: Already waiting for score update");
            return;
        }
        
        // スコア更新の完了を待ってから勝者決定
        isWaitingForScoreUpdate = true;
        
        // 全プレイヤーのスコア更新完了を追跡
        completedScoreUpdates.Clear();
        
        // タイムアウト処理を開始
        StartCoroutine(WaitForScoreUpdateWithTimeout());
    }
    
    // スコア更新完了時の処理
    private void OnScoreUpdateCompleted(int playerId, int newScore)
    {
        if (isWaitingForScoreUpdate)
        {
            completedScoreUpdates.Add(playerId);
            
            // 全プレイヤーのスコア更新が完了した場合のみ勝敗判定
            if (completedScoreUpdates.Count >= totalPlayerCount)
            {
                isWaitingForScoreUpdate = false;
                
                // ネットワーク同期のためにより長い遅延
                StartCoroutine(DelayedWinnerDetermination());
            }
        }
    }
    
    // 遅延付きの勝者決定（重複実行防止）
    private System.Collections.IEnumerator DelayedWinnerDetermination()
    {
        // ネットワーク同期のためにより長い遅延
        yield return new WaitForSeconds(0.3f); // 0.1秒 → 0.3秒
        
        // 遅延中に再度待機状態になった場合はスキップ
        if (!isWaitingForScoreUpdate)
        {
            DetermineWinner();
        }
    }
    
    // スコア更新完了を待つ（タイムアウト付き）
    private System.Collections.IEnumerator WaitForScoreUpdateWithTimeout()
    {
        float waitTime = 0f;
        while (isWaitingForScoreUpdate && waitTime < scoreUpdateTimeout)
        {
            yield return new WaitForSeconds(0.1f);
            waitTime += 0.1f;
        }
        
        if (isWaitingForScoreUpdate)
        {
            isWaitingForScoreUpdate = false;
            DetermineWinner();
        }
    }

    // 勝者決定ロジック
    public void DetermineWinner()
    {
        // 権威チェック
        bool hasAuthorityPlayer = false;
        if (playerManager != null)
        {
            foreach (var playerPair in playerManager.AllPlayers)
            {
                if (playerPair.Value != null && playerPair.Value.HasStateAuthority)
                {
                    hasAuthorityPlayer = true;
                    break;
                }
            }
        }
        
        if (!hasAuthorityPlayer)
        {
            return;
        }
        
        if (isProcessingWinnerDetermination)
        {
            return;
        }
        
        isProcessingWinnerDetermination = true;
        
        // ネットワーク同期を待つために少し遅延
        StartCoroutine(DetermineWinnerWithDelay());
    }
    
    private System.Collections.IEnumerator DetermineWinnerWithDelay()
    {
        // 1フレーム待機してネットワーク同期を確実にする
        yield return null;
        
        if (playerManager == null)
        {
            yield break;
        }

        var (winnerId, highestScore, tiedPlayers) = playerManager.DetermineWinner();
        
        int actualHighestScore = -1;
        int actualWinnerId = -1;
        foreach (var playerPair in playerManager.AllPlayers)
        {
            var avatar = playerPair.Value;
            if (avatar != null)
            {
                int currentScore = avatar.Score;
                
                if (currentScore > actualHighestScore)
                {
                    actualHighestScore = currentScore;
                    actualWinnerId = playerPair.Key;
                }
            }
        }
        
        if (actualWinnerId != -1 && actualHighestScore != highestScore)
        {
            winnerId = actualWinnerId;
            highestScore = actualHighestScore;
        }
        
        string message;
        if (winnerId == -1)
        {
            // 引き分けの場合：複数プレイヤーの名前を表示
            if (tiedPlayers.Count > 1)
            {
                List<string> tiedPlayerNames = new List<string>();
                int actualTieScore = -1; // 実際の引き分けスコアを取得
                foreach (int playerId in tiedPlayers)
                {
                    var player = playerManager.GetPlayerAvatar(playerId);
                    string playerName = player?.NickName.ToString() ?? $"Player {playerId}";
                    tiedPlayerNames.Add(playerName);
                    if (player != null)
                    {
                        actualTieScore = player.Score; // 最後のプレイヤーのスコア（全員同じはず）
                    }
                }
                message = $"DRAW! ({string.Join(" vs ", tiedPlayerNames)}) Score: {actualTieScore}";
            }
            else
            {
                message = "DRAW!";
            }
        }
        else
        {
            // プレイヤー情報を取得してニックネームを表示
            var winnerPlayer = playerManager.GetPlayerAvatar(winnerId);
            string winnerName = winnerPlayer?.NickName.ToString() ?? $"Player {winnerId}";
            int actualWinnerScore = winnerPlayer?.Score ?? -1;
            
            // 実際のプレイヤーのスコアを使用（PlayerManagerの値ではなく）
            message = $"The Winner is {winnerName} ! Score: {actualWinnerScore}";
        }
        
        // ローカルイベントを発火
        GameEvents.TriggerWinnerDetermined(message);
        
        // ネットワーク経由で全クライアントに送信（StateAuthorityを持つプレイヤーから）
        BroadcastWinnerMessageToAllClients(message);
        
        // ゲーム状態を再開待ちに変更
        StartCoroutine(SetWaitingForRestartAfterDelay());
        
        // 処理完了フラグをリセット
        isProcessingWinnerDetermination = false;
    }

    // RPC経由で勝者メッセージを全クライアントに送信
    private void BroadcastWinnerMessageToAllClients(string message)
    {
        if (playerManager == null) return;
        
        // 現在のクライアントにStateAuthorityを持つプレイヤーがいるかチェック
        PlayerAvatar authorityPlayer = null;
        foreach (var playerPair in playerManager.AllPlayers)
        {
            var playerAvatar = playerPair.Value;
            if (playerAvatar != null && playerAvatar.HasStateAuthority)
            {
                authorityPlayer = playerAvatar;
                break;
            }
        }
        
        if (authorityPlayer != null)
        {
            authorityPlayer.RPC_BroadcastWinnerMessage(message);
        }
    }

    // 勝者発表後に少し待ってから再開待ち状態に変更
    private System.Collections.IEnumerator SetWaitingForRestartAfterDelay()
    {
        // 勝者メッセージを表示する時間を確保（2秒待機）
        yield return new WaitForSeconds(2.0f);
        
        // ゲーム状態を再開待ちに変更
        var gameController = FindFirstObjectByType<GameController>();
        if (gameController != null)
        {
            gameController.CurrentGameState = GameState.WaitingForRestart;
        }
    }

    void OnDestroy()
    {
        if (GetComponent<ItemManager>() != null)
        {
            GetComponent<ItemManager>().OnAllItemsCollected -= TriggerGameEndByRule;
        }
        GameEvents.OnGameEnd -= HandleGameEndedByController;
        GameEvents.OnScoreUpdateCompleted -= OnScoreUpdateCompleted;
        GameEvents.OnPlayerCountChanged -= UpdateTotalPlayerCount;
    }
}
