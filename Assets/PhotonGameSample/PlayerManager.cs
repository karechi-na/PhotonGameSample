using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;
using PhotonGameSample.Infrastructure; // 追加

/// <summary>
/// プレイヤー関連の処理を管理するクラス
/// GameControllerへの直接参照を持たず、イベント駆動で通信
/// </summary>
public class PlayerManager : MonoBehaviour
{
    const int MAX_PLAYERS = 2;

    // プレイヤー管理
    private Dictionary<int, PlayerAvatar> allPlayerAvatars = new Dictionary<int, PlayerAvatar>();
    private List<PlayerAvatar> pendingIdResolution = new List<PlayerAvatar>(); // playerId<=0 再試行用
    private Dictionary<int, int> nullAvatarStreaks = new Dictionary<int, int>(); // 連続 null 検出回数
    private const int NULL_PRUNE_THRESHOLD = 2; // 2回連続で null なら pruning

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
        ServiceRegistry.Register<PlayerManager>(this); // フェーズ1登録
        
        // 継続的なプレイヤーチェックは開始（フォールバック用）
        StartCoroutine(ContinuousPlayerCheck());
    }

    /// <summary>
    /// シーン再ロード後などで辞書内に Destroy 済み(null) 参照が残るのを防ぐためのプルーニング処理。
    /// </summary>
    private bool PruneNullEntries()
    {
        bool pruned = false;
        var keys = new List<int>(allPlayerAvatars.Keys);
        foreach (var id in keys)
        {
            var val = allPlayerAvatars[id];
            if (val == null)
            {
                nullAvatarStreaks.TryGetValue(id, out int streak);
                streak++;
                nullAvatarStreaks[id] = streak;
                if (streak >= NULL_PRUNE_THRESHOLD)
                {
                    Debug.LogWarning($"PlayerManager: Pruning null avatar entry playerId={id} after {streak} consecutive detections");
                    allPlayerAvatars.Remove(id);
                    nullAvatarStreaks.Remove(id);
                    pruned = true;
                }
            }
            else
            {
                if (nullAvatarStreaks.ContainsKey(id)) nullAvatarStreaks.Remove(id); // 回復
            }
        }
        if (pruned)
        {
            OnPlayerCountChanged?.Invoke(allPlayerAvatars.Count);
        }
        return pruned;
    }

    /// <summary>
    /// 実際にシーン上に存在する PlayerAvatar から辞書を再構築（フルリロード後の再同期用）
    /// </summary>
    public void RebuildFromSceneAvatars()
    {
        Debug.Log("PlayerManager: RebuildFromSceneAvatars start");
        allPlayerAvatars.Clear();
        var avatars = FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None);
        foreach (var av in avatars)
        {
            if (av != null && av.playerId > 0)
            {
                Debug.Log($"PlayerManager: Rebuild registering playerId={av.playerId} instanceID={av.GetInstanceID()}");
                RegisterPlayerAvatar(av);
            }
        }
        Debug.Log($"PlayerManager: RebuildFromSceneAvatars end count={allPlayerAvatars.Count}");
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
            yield return new WaitForSeconds(1.5f); // 少し間隔を延ばす
            checkCount++;

            //Debug.Log($"PlayerManager: ContinuousPlayerCheck #{checkCount} - Current registered players: {allPlayerAvatars.Count}");
            
            // 現在登録されているプレイヤーの詳細を表示
            if (allPlayerAvatars.Count > 0)
            {
                var registeredIds = string.Join(", ", allPlayerAvatars.Keys);
                Debug.Log($"PlayerManager: Currently registered player IDs: [{registeredIds}]");
                
                foreach (var kvp in allPlayerAvatars)
                {
                    var registeredAvatar = kvp.Value;
                    if (registeredAvatar != null)
                    {
                        Debug.Log($"PlayerManager: Registered Player {kvp.Key} - GameObject: {registeredAvatar.gameObject.name}, InstanceID: {registeredAvatar.GetInstanceID()}, Active: {registeredAvatar.gameObject.activeInHierarchy}");
                    }
                    else
                    {
                        Debug.LogWarning($"PlayerManager: Registered Player {kvp.Key} has null avatar reference!");
                    }
                }
            }

            var allAvatars = FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None);
            Debug.Log($"PlayerManager: Found {allAvatars.Length} PlayerAvatars in scene");
            
            // すべてのPlayerAvatarの詳細情報を出力
            for (int i = 0; i < allAvatars.Length; i++)
            {
                var avatar = allAvatars[i];
                if (avatar != null)
                {
                    Debug.Log($"PlayerManager: Avatar[{i}] - GameObject: {avatar.gameObject.name}, playerId: {avatar.playerId}, InstanceID: {avatar.GetInstanceID()}");
                    Debug.Log($"PlayerManager: Avatar[{i}] - HasStateAuthority: {avatar.HasStateAuthority}, HasInputAuthority: {avatar.HasInputAuthority}");
                    Debug.Log($"PlayerManager: Avatar[{i}] - Object.HasStateAuthority: {avatar.Object.HasStateAuthority}, Object.HasInputAuthority: {avatar.Object.HasInputAuthority}");
                    Debug.Log($"PlayerManager: Avatar[{i}] - NickName: '{avatar.NickName.Value}', GameObject.activeInHierarchy: {avatar.gameObject.activeInHierarchy}");
                }
                else
                {
                    Debug.LogWarning($"PlayerManager: Avatar[{i}] is null");
                }
            }
            
            bool foundNewPlayer = false;
            foreach (var avatar in allAvatars)
            {
                if (avatar != null)
                {
                    Debug.Log($"PlayerManager: Checking avatar - GameObject: {avatar.gameObject.name}, playerId: {avatar.playerId}");
                    Debug.Log($"PlayerManager: Avatar NetworkBehaviour info - HasStateAuthority: {avatar.HasStateAuthority}, HasInputAuthority: {avatar.HasInputAuthority}");
                    Debug.Log($"PlayerManager: Avatar Network Status - Object.HasStateAuthority: {avatar.Object.HasStateAuthority}, Object.HasInputAuthority: {avatar.Object.HasInputAuthority}");
                    
                    if (!allPlayerAvatars.ContainsKey(avatar.playerId))
                    {
                        // playerId が 0 の場合は警告を出して登録をスキップ
                        if (avatar.playerId <= 0)
                        {
                            Debug.LogWarning($"PlayerManager: Avatar has invalid playerId: {avatar.playerId}. GameObject: {avatar.gameObject.name}, InstanceID: {avatar.GetInstanceID()}");
                            Debug.LogWarning($"PlayerManager: This might be a network sync issue or invalid spawn. Avatar will be ignored.");
                            
                            // 無効なPlayerAvatarが古いものかどうかチェック
                            if (avatar.NickName.Value == null || avatar.NickName.Value == "")
                            {
                                Debug.LogWarning($"PlayerManager: Avatar has empty NickName, this might be an old/invalid spawn");
                            }
                            if (!pendingIdResolution.Contains(avatar))
                            {
                                pendingIdResolution.Add(avatar);
                                Debug.Log($"PlayerManager: Added avatar InstanceID={avatar.GetInstanceID()} to pendingIdResolution list");
                            }
                            continue;
                        }
                        
                        Debug.Log($"PlayerManager: Found unregistered player {avatar.playerId}, registering...");
                        Debug.Log($"PlayerManager: Avatar details - GameObject: {avatar.gameObject.name}, playerId: {avatar.playerId}, IsNull: {avatar == null}");
                        
                        try
                        {
                            Debug.Log($"PlayerManager: Calling RegisterPlayerAvatar for player {avatar.playerId}");
                            RegisterPlayerAvatar(avatar);
                            Debug.Log($"PlayerManager: RegisterPlayerAvatar completed for player {avatar.playerId}");
                            foundNewPlayer = true;
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogError($"PlayerManager: Exception in RegisterPlayerAvatar for player {avatar.playerId}: {ex.Message}");
                            Debug.LogError($"PlayerManager: Exception stack trace: {ex.StackTrace}");
                        }
                    }
                    else
                    {
                        Debug.Log($"PlayerManager: Player {avatar.playerId} is already registered, skipping");
                    }
                }
                else
                {
                    Debug.LogWarning("PlayerManager: Found null avatar in scene");
                }
            }

            // シーンロード後に破棄されたが辞書に残っている参照を除去
            if (PruneNullEntries())
            {
                Debug.Log("PlayerManager: Null avatar entries pruned");
            }

            // playerId が後から確定したものを再試行
            if (pendingIdResolution.Count > 0)
            {
                for (int i = pendingIdResolution.Count - 1; i >= 0; i--)
                {
                    var av = pendingIdResolution[i];
                    if (av == null)
                    {
                        pendingIdResolution.RemoveAt(i);
                        continue;
                    }
                    if (av.playerId > 0 && !allPlayerAvatars.ContainsKey(av.playerId))
                    {
                        Debug.Log($"PlayerManager: Retrying registration for resolved avatar playerId={av.playerId}");
                        RegisterPlayerAvatar(av);
                        pendingIdResolution.RemoveAt(i);
                    }
                }
            }
            
            if (!foundNewPlayer)
            {
                // Debug.Log($"PlayerManager: No new players found in check #{checkCount}");
            }
        }
    }

    /// <summary>
    /// プレイヤーアバターを登録
    /// </summary>
    public void RegisterPlayerAvatar(PlayerAvatar avatar)
    {
        Debug.Log($"PlayerManager: RegisterPlayerAvatar ENTRY - avatar null check: {avatar == null}");
        
        if (avatar == null)
        {
            Debug.LogError("PlayerManager: RegisterPlayerAvatar called with null avatar!");
            return;
        }

        // Destroy 済み (UnityEngine.Object の == 演算子) を早期弾き
        if (avatar.Equals(null))
        {
            Debug.LogWarning("PlayerManager: Provided avatar reference is destroyed/invalid");
            return;
        }
        
        Debug.Log($"PlayerManager: RegisterPlayerAvatar called for playerId: {avatar.playerId}");
        Debug.Log($"PlayerManager: Current registered players before: [{string.Join(", ", allPlayerAvatars.Keys)}]");
        Debug.Log($"PlayerManager: ContainsKey check for {avatar.playerId}: {allPlayerAvatars.ContainsKey(avatar.playerId)}");
        
        if (!allPlayerAvatars.ContainsKey(avatar.playerId))
        {
            int previousPlayerCount = allPlayerAvatars.Count; // 登録前のプレイヤー数を記録
            
            Debug.Log($"PlayerManager: Adding player {avatar.playerId} to dictionary");
            allPlayerAvatars[avatar.playerId] = avatar;
            
            Debug.Log($"PlayerManager: Subscribing to OnScoreChanged for player {avatar.playerId}");
            avatar.OnScoreChanged += HandlePlayerScoreChanged;

            Debug.Log($"PlayerManager: Registered Player {avatar.playerId} (Total: {allPlayerAvatars.Count})");

            // イベント発火
            OnPlayerRegistered?.Invoke(avatar);
            
            // プレイヤー数が実際に変わった場合のみイベント発火
            if (allPlayerAvatars.Count != previousPlayerCount)
            {
                OnPlayerCountChanged?.Invoke(allPlayerAvatars.Count);
                Debug.Log($"PlayerManager: Player count changed from {previousPlayerCount} to {allPlayerAvatars.Count}");
            }
            else
            {
                Debug.LogWarning($"PlayerManager: Player count did not change - this should not happen!");
            }
            
            // GameEventsも発火
            GameEvents.TriggerPlayerRegistered(avatar.playerId);

            // 初期スコアもイベントで通知
            HandlePlayerScoreChanged(avatar.playerId, avatar.Score);
        }
        else
        {
            Debug.Log($"PlayerManager: Player {avatar.playerId} is already registered, skipping...");
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

    /// <summary>
    /// 安定IDに基づいてプレイヤーを正規スポーン座標へスナップ（NetworkGameManager の spawnPositions を参照）。
    /// 遅延スポーン後や他コンポーネントによる位置ズレを補正する用途。
    /// </summary>
    public void NormalizePlayerPositions()
    {
        var ngm = PhotonGameSample.Infrastructure.ServiceRegistry.GetOrNull<NetworkGameManager>();
        if (ngm == null)
        {
            Debug.LogWarning("PlayerManager: NormalizePlayerPositions skipped (NetworkGameManager not found)");
            return;
        }
        var spField = typeof(NetworkGameManager).GetField("spawnPositions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (spField == null)
        {
            Debug.LogWarning("PlayerManager: spawnPositions field reflection failed");
            return;
        }
        var arr = spField.GetValue(ngm) as UnityEngine.Vector3[];
        if (arr == null || arr.Length == 0)
        {
            Debug.LogWarning("PlayerManager: spawnPositions array empty");
            return;
        }
        foreach (var kv in allPlayerAvatars)
        {
            var av = kv.Value;
            if (av == null) continue;
            int pid = kv.Key;
            int idx = Mathf.Clamp(pid - 1, 0, arr.Length - 1);
            var target = arr[idx];
            // 高さは現在の y を維持（地面差異考慮）
            var current = av.transform.position;
            var desired = new Vector3(target.x, current.y, target.z);
            if ((current - desired).sqrMagnitude > 0.01f && av.HasStateAuthority)
            {
                av.transform.position = desired;
                Debug.Log($"PlayerManager: Normalized Player{pid} position -> {desired}");
            }
        }
    }
}
