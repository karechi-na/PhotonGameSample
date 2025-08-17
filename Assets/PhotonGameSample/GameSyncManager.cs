using UnityEngine;
using Fusion;
using PhotonGameSample.Infrastructure; // 追加

/// <summary>
/// ゲーム進行同期管理クラス
/// PlayerAvatarから分離されたゲーム進行関連のRPC機能を担当
/// ゲーム状態、カウントダウン、再開処理などのクライアント間同期を管理
/// </summary>
public class GameSyncManager : NetworkBehaviour
{
    private void Awake()
    {
        ServiceRegistry.Register<GameSyncManager>(this); // フェーズ1登録
    }
    
    /// <summary>
    /// 任意のクライアントからマスターへ「再開クリック」を要求する（集約用）
    /// </summary>
    public void RequestRestartClick(int playerId)
    {
        Debug.Log($"GameSyncManager: RequestRestartClick from client for player {playerId}");
        RPC_RequestRestartClick(playerId);
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
    /// ゲーム状態変更を全クライアントに同期
    /// </summary>
    public void NotifyGameStateChanged(GameState newState)
    {
        Debug.Log($"GameSyncManager: NotifyGameStateChanged called with state: {newState}");
        Debug.Log($"GameSyncManager: HasStateAuthority = {HasStateAuthority}");
        
        if (HasStateAuthority)
        {
            Debug.Log($"GameSyncManager: Sending RPC_NotifyGameStateChanged({newState})");
            RPC_NotifyGameStateChanged(newState);
        }
        else
        {
            Debug.LogWarning($"GameSyncManager: Cannot send RPC - no state authority");
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
    /// ハードリセットを全クライアントに通知（Runner 全面再初期化用）
    /// </summary>
    public void NotifyHardReset()
    {
        if (HasStateAuthority)
        {
            RPC_NotifyHardReset();
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
    /// RPC: 再開クリックをマスター（StateAuthority）に要求（全クライアント→マスター）
    /// マスターは自分でイベント発火し、他クライアントへは通知RPCを配信
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestRestartClick(int clickedPlayerId)
    {
        Debug.Log($"GameSyncManager: RPC_RequestRestartClick received on master for player {clickedPlayerId}");
        // マスターで集約
        GameEvents.TriggerPlayerClickedForRestart(clickedPlayerId);
        // 他クライアントに通知（自分は HasStateAuthority なので RPC_NotifyRestartClick 内でスキップされる）
        RPC_NotifyRestartClick(clickedPlayerId);
    }
    
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
        Debug.Log($"GameSyncManager: RPC_NotifyCountdownUpdate received with remainingSeconds: {remainingSeconds}");
        GameEvents.TriggerCountdownUpdate(remainingSeconds);
    }
    
    /// <summary>
    /// RPC: ゲーム状態変更を全クライアントに通知
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_NotifyGameStateChanged(GameState newState)
    {
        Debug.Log($"GameSyncManager: RPC_NotifyGameStateChanged received with state: {newState}");
        Debug.Log($"GameSyncManager: HasStateAuthority = {HasStateAuthority}, triggering GameEvents.TriggerGameStateChanged({newState})");
        GameEvents.TriggerGameStateChanged(newState);
        Debug.Log($"GameSyncManager: GameEvents.TriggerGameStateChanged({newState}) completed");
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
    /// RPC: ハードリセット要求を全クライアントへ送信
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_NotifyHardReset()
    {
        GameEvents.TriggerHardResetRequested();
    }
    
    /// <summary>
    /// RPC: アイテムリセットを全クライアントに通知
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_NotifyItemsReset()
    {
        GameEvents.TriggerItemsReset();
    }
}
