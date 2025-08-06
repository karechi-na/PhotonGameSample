using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;

/// <summary>
/// プレイヤー関連の処理を管理するクラス
/// GameControllerへの直接参照を持たず、イベント駆動で通信
/// </summary>
public class PlayerManager : MonoBehaviour
{
    const int MAX_PLAYERS = 2;

    // プレイヤー管理
    private Dictionary<int, PlayerAvatar> allPlayerAvatars = new Dictionary<int, PlayerAvatar>();

    // イベント定義
    public event Action<PlayerAvatar> OnPlayerRegistered; // プレイヤー登録時
    public event Action<int> OnPlayerUnregistered; // プレイヤー登録解除時
    public event Action<int, int> OnPlayerScoreChanged; // スコア変更時 (playerId, newScore)
    public event Action<int> OnPlayerCountChanged; // プレイヤー数変更時 (playerCount)

    // プロパティ
    public int PlayerCount => allPlayerAvatars.Count;
    public int MaxPlayers => MAX_PLAYERS;
    public bool HasMaxPlayers => PlayerCount >= MAX_PLAYERS;
    public Dictionary<int, PlayerAvatar> AllPlayers => new Dictionary<int, PlayerAvatar>(allPlayerAvatars);

    void Start()
    {
        Debug.Log("PlayerManager: Started");
        
        // 継続的なプレイヤーチェックは開始（フォールバック用）
        StartCoroutine(ContinuousPlayerCheck());
    }

    /// <summary>
    /// 定期的にプレイヤーの登録状況をチェックし、未登録のプレイヤーを登録
    /// これにより、ネットワークの遅延や同期の問題を回避
    /// </summary>
    /// <returns></returns>
    /// <remarks>
    /// このメソッドは、プレイヤーがネットワーク上で登録されていない場合に備え、
    /// 定期的に全プレイヤーをチェックし、未登録のプレイヤーを自動的に登録します。
    /// これにより、ネットワークの遅延や同期の問題を回避し、プレイヤーの状態を常に最新に保ちます。
    private IEnumerator ContinuousPlayerCheck()
    {
        int checkCount = 0;
        while (true)
        {
            yield return new WaitForSeconds(2.0f); // より長い間隔（フォールバック用）
            checkCount++;

            var allAvatars = FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None);
            
            foreach (var avatar in allAvatars)
            {
                if (avatar != null && !allPlayerAvatars.ContainsKey(avatar.playerId))
                {
                    Debug.Log($"PlayerManager: Found unregistered player {avatar.playerId}, registering...");
                    RegisterPlayerAvatar(avatar);
                }
            }
        }
    }

    /// <summary>
    /// プレイヤーアバターを登録
    /// </summary>
    public void RegisterPlayerAvatar(PlayerAvatar avatar)
    {
        if (avatar == null)
        {
            Debug.LogError("PlayerManager: RegisterPlayerAvatar called with null avatar!");
            return;
        }
        
        if (!allPlayerAvatars.ContainsKey(avatar.playerId))
        {
            allPlayerAvatars[avatar.playerId] = avatar;
            avatar.OnScoreChanged += HandlePlayerScoreChanged;

            Debug.Log($"PlayerManager: Registered Player {avatar.playerId} (Total: {allPlayerAvatars.Count})");

            // イベント発火
            OnPlayerRegistered?.Invoke(avatar);
            OnPlayerCountChanged?.Invoke(allPlayerAvatars.Count);
            
            // GameEventsも発火
            GameEvents.TriggerPlayerRegistered(avatar.playerId);

            // 初期スコアもイベントで通知
            HandlePlayerScoreChanged(avatar.playerId, avatar.Score);
        }
    }

    /// <summary>
    /// プレイヤーアバターの登録を解除
    /// </summary>
    public void UnregisterPlayerAvatar(int playerId)
    {
        if (allPlayerAvatars.ContainsKey(playerId))
        {
            var avatar = allPlayerAvatars[playerId];
            if (avatar != null)
            {
                avatar.OnScoreChanged -= HandlePlayerScoreChanged;
            }

            allPlayerAvatars.Remove(playerId);
            Debug.Log($"PlayerManager: Unregistered Player {playerId}. Total players: {allPlayerAvatars.Count}");

            // イベント発火
            OnPlayerUnregistered?.Invoke(playerId);
            OnPlayerCountChanged?.Invoke(allPlayerAvatars.Count);
        }
    }

    /// <summary>
    /// プレイヤーのスコア変更を処理
    /// </summary>
    private void HandlePlayerScoreChanged(int playerId, int newScore)
    {
        OnPlayerScoreChanged?.Invoke(playerId, newScore);
        
        // GameEventsは GameController 経由で発火されるため、ここでは削除
        // GameEvents.TriggerPlayerScoreChanged(playerId, newScore);
    }

    /// <summary>
    /// 全プレイヤーの入力状態を設定
    /// </summary>
    public void SetAllPlayersInputEnabled(bool enabled)
    {
        if (allPlayerAvatars.Count == 0)
        {
            Debug.LogWarning("PlayerManager: No players registered!");
            return;
        }
        
        foreach (var avatarPair in allPlayerAvatars)
        {
            var avatar = avatarPair.Value;
            if (avatar != null)
            {
                avatar.SetInputEnabled(enabled);
            }
        }
    }

    /// <summary>
    /// 特定プレイヤーの入力状態を設定
    /// </summary>
    public void SetPlayerInputEnabled(int playerId, bool enabled)
    {
        if (allPlayerAvatars.TryGetValue(playerId, out PlayerAvatar avatar) && avatar != null)
        {
            avatar.SetInputEnabled(enabled);
            Debug.Log($"PlayerManager: Player {playerId} input {(enabled ? "enabled" : "disabled")}");
        }
    }

    /// <summary>
    /// プレイヤーアバターを取得
    /// </summary>
    public PlayerAvatar GetPlayerAvatar(int playerId)
    {
        allPlayerAvatars.TryGetValue(playerId, out PlayerAvatar avatar);
        return avatar;
    }

    /// <summary>
    /// 全プレイヤーのスコア情報を取得
    /// </summary>
    public Dictionary<int, int> GetAllPlayerScores()
    {
        var scores = new Dictionary<int, int>();
        foreach (var avatarPair in allPlayerAvatars)
        {
            var avatar = avatarPair.Value;
            if (avatar != null)
            {
                scores[avatarPair.Key] = avatar.Score;
            }
        }
        return scores;
    }

    /// <summary>
    /// 勝者を決定する（スコア計算のみ、表示は呼び出し元が担当）
    /// </summary>
    public (int winnerId, int highestScore, List<int> tiedPlayers) DetermineWinner()
    {
        Debug.Log("PlayerManager: Determining winner...");
        
        int highestScore = -1;
        int winnerId = -1;
        List<int> tiedPlayers = new List<int>();

        // 全プレイヤーのスコア情報をチェック
        foreach (var avatarPair in allPlayerAvatars)
        {
            var avatar = avatarPair.Value;
            if (avatar != null)
            {
                int score = avatar.Score;
                Debug.Log($"PlayerManager: Player {avatarPair.Key} score: {score}");

                if (score > highestScore)
                {
                    highestScore = score;
                    winnerId = avatarPair.Key;
                    tiedPlayers.Clear();
                    tiedPlayers.Add(winnerId);
                }
                else if (score == highestScore && highestScore >= 0)
                {
                    tiedPlayers.Add(avatarPair.Key);
                }
            }
        }

        // 引き分け判定：複数のプレイヤーが同じ最高スコアの場合
        if (tiedPlayers.Count > 1)
        {
            Debug.Log($"PlayerManager: Tie detected! {tiedPlayers.Count} players with score {highestScore}");
            winnerId = -1; // 引き分けを示す
        }
        else
        {
            Debug.Log($"PlayerManager: Winner is Player {winnerId} with score {highestScore}");
        }
        
        return (winnerId, highestScore, tiedPlayers);
    }

    /// <summary>
    /// デバッグ情報を取得
    /// </summary>
    public string GetDebugInfo()
    {
        return $"Players: {PlayerCount}/{MaxPlayers} - IDs: [{string.Join(", ", allPlayerAvatars.Keys)}]";
    }
    
    /// <summary>
    /// 全プレイヤーのスコアをリセット（ゲーム再開時に使用）
    /// </summary>
    public void ResetAllPlayersScore()
    {
        Debug.Log("PlayerManager: Resetting all players' scores");
        
        foreach (var avatarPair in allPlayerAvatars)
        {
            var avatar = avatarPair.Value;
            if (avatar != null)
            {
                avatar.ResetScore();
            }
        }
        
        Debug.Log($"PlayerManager: Reset complete for {allPlayerAvatars.Count} players");
    }
    
    /// <summary>
    /// 全プレイヤーを初期Spawn位置に戻す（ゲーム再開時に使用）
    /// </summary>
    public void ResetAllPlayersToSpawnPosition()
    {
        Debug.Log("PlayerManager: Resetting all players to spawn position");
        
        foreach (var avatarPair in allPlayerAvatars)
        {
            var avatar = avatarPair.Value;
            if (avatar != null)
            {
                avatar.ResetToSpawnPosition();
            }
        }
        
        Debug.Log($"PlayerManager: Position reset complete for {allPlayerAvatars.Count} players");
    }
}
