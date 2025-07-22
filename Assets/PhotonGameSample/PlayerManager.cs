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
        Debug.Log("PlayerManager: Start() called");
        Debug.Log("PlayerManager: Waiting for NetworkGameManager to spawn players...");
        
        // FindObjectsByTypeによる自動検索は行わず、
        // NetworkGameManager経由でのRegisterPlayerAvatarの呼び出しのみに依存
        
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
            
            // 5回に1回詳細ログを表示（ログが多すぎるのを防ぐため）
            bool showDetailedLog = (checkCount % 5 == 0);
            
            if (showDetailedLog)
            {
                Debug.Log($"PlayerManager: ContinuousPlayerCheck #{checkCount} (FALLBACK) - Found {allAvatars.Length} total avatars, {allPlayerAvatars.Count} registered");
            }
            
            foreach (var avatar in allAvatars)
            {
                if (avatar != null && !allPlayerAvatars.ContainsKey(avatar.playerId))
                {
                    Debug.Log($"PlayerManager: 🔍 FALLBACK - Found unregistered player {avatar.playerId} (HasStateAuthority: {avatar.HasStateAuthority}, NickName: '{avatar.NickName.Value}'), registering...");
                    RegisterPlayerAvatar(avatar);
                }
            }
            
            // 登録状況を定期報告（MAX_PLAYERSに満たない場合のみ）
            if (allPlayerAvatars.Count > 0 && allPlayerAvatars.Count < MAX_PLAYERS && showDetailedLog)
            {
                Debug.Log($"PlayerManager: Current registered players: [{string.Join(", ", allPlayerAvatars.Keys)}] - Still looking for more players...");
            }
        }
    }

    /// <summary>
    /// プレイヤーアバターを登録
    /// </summary>
    public void RegisterPlayerAvatar(PlayerAvatar avatar)
    {
        Debug.Log($"PlayerManager: RegisterPlayerAvatar called for avatar with ID {avatar?.playerId}");
        
        if (avatar == null)
        {
            Debug.LogError("PlayerManager: RegisterPlayerAvatar called with null avatar!");
            return;
        }
        
        Debug.Log($"PlayerManager: Attempting to register Player {avatar.playerId}" +
                  $"\n  HasStateAuthority: {avatar.HasStateAuthority}" +
                  $"\n  NickName: '{avatar.NickName.Value}'" +
                  $"\n  Current Score: {avatar.Score}" +
                  $"\n  Already registered? {allPlayerAvatars.ContainsKey(avatar.playerId)}");
        
        if (!allPlayerAvatars.ContainsKey(avatar.playerId))
        {
            allPlayerAvatars[avatar.playerId] = avatar;
            avatar.OnScoreChanged += HandlePlayerScoreChanged;

            Debug.Log($"PlayerManager: ✅ Successfully registered Player {avatar.playerId}" +
                      $"\n  Total players: {allPlayerAvatars.Count}" +
                      $"\n  Current score: {avatar.Score}");

            // イベント発火
            OnPlayerRegistered?.Invoke(avatar);
            OnPlayerCountChanged?.Invoke(allPlayerAvatars.Count);
            
            // GameEventsも発火
            GameEvents.TriggerPlayerRegistered(avatar.playerId);

            // 初期スコアもイベントで通知
            HandlePlayerScoreChanged(avatar.playerId, avatar.Score);
        }
        else
        {
            Debug.Log($"PlayerManager: Player {avatar.playerId} already registered - skipping");
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
        Debug.Log($"PlayerManager: Player {playerId} score changed to {newScore} - forwarding to GameController");
        OnPlayerScoreChanged?.Invoke(playerId, newScore);
        
        // GameEventsは GameController 経由で発火されるため、ここでは削除
        // GameEvents.TriggerPlayerScoreChanged(playerId, newScore);
    }

    /// <summary>
    /// 全プレイヤーの入力状態を設定
    /// </summary>
    public void SetAllPlayersInputEnabled(bool enabled)
    {
        Debug.Log($"==== PlayerManager: SetAllPlayersInputEnabled called with enabled={enabled} ====" +
                  $"\n  Total players to update: {allPlayerAvatars.Count}" +
                  $"\n  Registered player IDs: [{string.Join(", ", allPlayerAvatars.Keys)}]");
        
        if (allPlayerAvatars.Count == 0)
        {
            Debug.LogWarning("PlayerManager: No players registered! Cannot enable/disable input.");
            return;
        }
        
        foreach (var avatarPair in allPlayerAvatars)
        {
            var avatar = avatarPair.Value;
            if (avatar != null)
            {
                Debug.Log($"PlayerManager: Updating Player {avatarPair.Key}" +
                          $"\n  HasStateAuthority: {avatar.HasStateAuthority}" +
                          $"\n  Input {(enabled ? "enabled" : "disabled")}");
                avatar.SetInputEnabled(enabled);
            }
            else
            {
                Debug.LogWarning($"PlayerManager: Player {avatarPair.Key} avatar is null!");
            }
        }
        
        Debug.Log($"PlayerManager: SetAllPlayersInputEnabled completed for {allPlayerAvatars.Count} players" +
                  $"\n==== PlayerManager: SetAllPlayersInputEnabled finished ====");
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
        Debug.Log("=== PlayerManager: DetermineWinner called ===" +
                  $"\n  Total registered players: {allPlayerAvatars.Count}");
        
        // まず現在のプレイヤースコア情報を確認
        foreach (var kvp in allPlayerAvatars)
        {
            var avatar = kvp.Value;
            if (avatar != null)
            {
                Debug.Log($"PlayerManager: Pre-check - Player {kvp.Key} current score: {avatar.Score}" +
                          $"\n  HasStateAuthority: {avatar.HasStateAuthority}" +
                          $"\n  IsSpawned: {avatar.Object?.IsValid}" +
                          $"\n  Unity Frame: {Time.frameCount}, Time: {Time.time:F3}s");
            }
        }
        
        int highestScore = -1;
        int winnerId = -1;
        List<int> tiedPlayers = new List<int>();

        // 全プレイヤーの詳細なスコア情報をログ出力
        foreach (var avatarPair in allPlayerAvatars)
        {
            var avatar = avatarPair.Value;
            if (avatar != null)
            {
                int score = avatar.Score;
                Debug.Log($"=== PlayerManager: Player {avatarPair.Key} final score: {score} ===" +
                          $"\n  HasStateAuthority: {avatar.HasStateAuthority}" +
                          $"\n  NickName: {avatar.NickName.Value}" +
                          $"\n  Unity Frame: {Time.frameCount}, Time: {Time.time:F3}s");

                if (score > highestScore)
                {
                    Debug.Log($"PlayerManager: Score {score} > current highest {highestScore} - updating highest");
                    highestScore = score;
                    winnerId = avatarPair.Key;
                    tiedPlayers.Clear();
                    tiedPlayers.Add(winnerId);
                    Debug.Log($"PlayerManager: New highest score: Player {winnerId} with {highestScore} points");
                }
                else if (score == highestScore && highestScore >= 0) // 0点以上で同点の場合
                {
                    Debug.Log($"PlayerManager: Score {score} == current highest {highestScore} - adding to tied players");
                    tiedPlayers.Add(avatarPair.Key);
                    Debug.Log($"PlayerManager: Tie detected: Player {avatarPair.Key} also has {score} points");
                }
                else
                {
                    Debug.Log($"PlayerManager: Score {score} <= current highest {highestScore} - no change");
                }
            }
            else
            {
                Debug.LogWarning($"PlayerManager: Player {avatarPair.Key} avatar is null!");
            }
        }

        Debug.Log($"PlayerManager: Final calculation" +
                  $"\n  Highest Score: {highestScore}" +
                  $"\n  Winner: {winnerId}" +
                  $"\n  Tied Players: [{string.Join(", ", tiedPlayers)}]");

        // 引き分け判定：複数のプレイヤーが同じ最高スコアの場合
        if (tiedPlayers.Count > 1)
        {
            Debug.Log($"PlayerManager: Tie detected! {tiedPlayers.Count} players have the same highest score: {highestScore}");
            winnerId = -1; // 引き分けを示す
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
}
