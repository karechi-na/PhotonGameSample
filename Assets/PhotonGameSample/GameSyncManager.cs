using Fusion;
using UnityEngine;
using System;

/// <summary>
/// ゲーム進行同期管理クラス
/// PlayerAvatarから分離されたゲーム進行関連のRPC機能を担当
/// ゲーム状態、カウントダウン、再開処理などのクライアント間同期を管理
/// </summary>
[RequireComponent(typeof(ItemManager))]
public class GameSyncManager : NetworkBehaviour
{

    void Awake()
    {
        var netObj = GetComponent<NetworkObject>();
        Debug.Log($"GameSyncManager: Awake called on {gameObject.name}, NetworkObject: {(netObj != null ? "あり" : "なし")}, ObjectId: {(netObj != null ? netObj.Id.ToString() : "N/A")}");
    }

    /// <summary>
    /// 再開クリックを全クライアントに同期
    /// </summary>
    public void NotifyRestartClick(int playerId)
    {
        if (HasStateAuthority)
        {
            // ローカルでイベントを発火
            GameEvents.TriggerPlayerClickedForRestart(playerId);

            // 他のクライアントにRPCを送信
            RPC_NotifyRestartClick(playerId);
        }
    }

    /// <summary>
    /// カウントダウン更新を全クライアントに同期
    /// </summary>
    public void NotifyCountdownUpdate(int remainingSeconds)
    {
        if (HasStateAuthority)
        {
            RPC_NotifyCountdownUpdate(remainingSeconds);
        }
    }

    /// <summary>
    /// ゲーム開始を全クライアントに同期。
    /// </summary>
    public void NotifyGameStart()
    {
        if (HasStateAuthority)
        {
            NotifyGameStateChanged(GameState.InGame);
        }
    }

    /// <summary>
    /// ゲーム状態変更を全クライアントに同期。
    /// </summary>
    public void NotifyGameStateChanged(GameState newState)
    {
        if (HasStateAuthority)
        {
            RPC_NotifyGameStateChanged(newState);
        }
    }

    /// <summary>
    /// プレイヤー操作開放を全クライアントに同期
    /// </summary>
    public void NotifyEnableAllPlayersInput(bool enabled)
    {
        if (HasStateAuthority)
        {
            RPC_NotifyEnableAllPlayersInput(enabled);
        }
    }

    /// <summary>
    /// ゲーム再開処理を全クライアントに同期
    /// </summary>
    public void NotifyGameRestart()
    {
        if (HasStateAuthority)
        {
            RPC_NotifyGameRestart();
        }
    }

    /// <summary>
    /// アイテムリセット通知を全クライアントに同期
    /// </summary>
    public void NotifyItemsReset()
    {
        if (HasStateAuthority)
        {
            RPC_NotifyItemsReset();
        }
    }

    // =============================================================================
    // RPC メソッド群
    // =============================================================================

    /// <summary>
    /// RPC: プレイヤーの再開クリックを全クライアントに通知
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_NotifyRestartClick(int clickedPlayerId)
    {
        // 自分自身のクリックの場合はスキップ（ローカルで既に処理済み）
        if (HasStateAuthority)
        {
            return;
        }

        GameEvents.TriggerPlayerClickedForRestart(clickedPlayerId);
    }

    /// <summary>
    /// RPC: カウントダウン更新を全クライアントに通知
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_NotifyCountdownUpdate(int remainingSeconds)
    {
        GameEvents.TriggerCountdownUpdate(remainingSeconds);
    }

    /// <summary>
    /// RPC: ゲーム状態変更を全クライアントに通知。
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_NotifyGameStateChanged(GameState newState)
    {
        Debug.Log($"[RPC] RPC_NotifyGameStateChanged called on {gameObject.name} with state: {newState}");
        GameEvents.TriggerGameStateChanged(newState);
    }

    /// <summary>
    /// RPC: プレイヤー操作開放を全クライアントに通知
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_NotifyEnableAllPlayersInput(bool enabled)
    {
        GameEvents.TriggerPlayerInputStateChanged(enabled);
    }

    /// <summary>
    /// RPC: ゲーム再開処理を全クライアントに通知
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_NotifyGameRestart()
    {
        GameEvents.TriggerGameRestartExecution();
    }

    /// <summary>
    /// RPC: アイテムリセットを全クライアントに通知
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_NotifyItemsReset()
    {
        GameEvents.TriggerItemsReset();
    }

    // 追加: StateAuthorityへのクリックリクエスト用RPC
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestRestartClick(int playerId)
    {
        // StateAuthority側でのみ実行される
        NotifyRestartClick(playerId);
        Debug.Log($"[RPC] RPC_RequestRestartClick called by player {playerId} on {gameObject.name}");
    }

    public static event Action<GameSyncManager> OnAnyGameSyncManagerSpawned;

    public override void Spawned()
    {
        base.Spawned();
        OnAnyGameSyncManagerSpawned?.Invoke(this);
    }
}
