using Fusion;
using UnityEngine;
using System;
using System.Collections.Generic;

[RequireComponent(typeof(NetworkCharacterController), typeof(PlayerAvatarView), typeof(ItemCatcher))]
public class PlayerAvatar : NetworkBehaviour
{
    [Networked]
    public NetworkString<_16> NickName { get; set; }

    [Networked] public int playerId { get; set; } = 0;
    [Networked, OnChangedRender(nameof(OnScoreChangedRender))] 
    public int Score { get; set; } = 0; // ネットワーク同期されるスコア
    
    private NetworkCharacterController characterController;
    private NetworkMecanimAnimator networkAnimator;
    
    // Spawn位置を保存
    private Vector3 spawnPosition;
    
    // 入力制御フラグ
    private bool inputEnabled = false;
    
    // スコア変更時のイベント
    public event Action<int, int> OnScoreChanged; // (playerId, newScore)

    private int previousScore = 0;
    
    // アイテム取得重複防止用
    private HashSet<int> processedItems = new HashSet<int>();
    
    // デバッグ用：OnItemCaught呼び出し回数をトラッキング
    private int onItemCaughtCallCount = 0;

    // ネットワークプロパティ変更時のコールバック
    private void OnScoreChangedRender()
    {
        OnScoreChanged?.Invoke(playerId, Score);
        previousScore = Score;
        
        // スコア更新完了を通知（ゲーム終了判定で使用）
        GameEvents.TriggerScoreUpdateCompleted(playerId, Score);
    }

    public override void Spawned()
    {
        Debug.Log($"PlayerAvatar: Player {playerId} spawned (Authority: {HasStateAuthority})");
        Debug.Log($"PlayerAvatar: PlayerRef ID: {Object.InputAuthority.PlayerId}");
        Debug.Log($"PlayerAvatar: NickName: {NickName.Value}");
        
        // プレイヤーIDが設定されていない場合はPlayerRefから取得
        if (playerId == 0 && HasStateAuthority)
        {
            playerId = Object.InputAuthority.PlayerId;
            Debug.Log($"PlayerAvatar: Set playerId from InputAuthority: {playerId}");
        }
        
        // Spawn位置を保存
        spawnPosition = transform.position;
        Debug.Log($"PlayerAvatar: Spawn position saved: {spawnPosition}");
        
        characterController = GetComponent<NetworkCharacterController>();
        networkAnimator = GetComponentInChildren<NetworkMecanimAnimator>();

        var view = GetComponent<PlayerAvatarView>();
        view.SetNickName(NickName.Value);
        if (HasStateAuthority)
        {
            view.MakeCameraTarget();
            Debug.Log($"PlayerAvatar: Player {playerId} set as camera target (local player)");
        }

        // ItemCatcherのイベントをサブスクライブ
        var itemCatcher = GetComponent<ItemCatcher>();
        itemCatcher.OnItemCaught += OnItemCaught;

        previousScore = Score;
    }

    private void OnItemCaught(Item item, PlayerAvatar playerAvatar)
    {
        onItemCaughtCallCount++;
        
        // アイテムの重複処理防止チェック
        int itemInstanceId = item.GetInstanceID();
        
        if (processedItems.Contains(itemInstanceId))
        {
            Debug.LogWarning($"PlayerAvatar: Player {playerId} - duplicate item {itemInstanceId} detected, skipping");
            return;
        }
        processedItems.Add(itemInstanceId);

        Debug.Log($"PlayerAvatar: Player {playerId} caught item (value: {item.itemValue})");

        if (HasStateAuthority)
        {
            // 自分がStateAuthorityを持つ場合：直接スコア更新
            int oldScore = Score;
            Score += item.itemValue;
            Debug.Log($"PlayerAvatar: Score updated directly {oldScore} -> {Score}");
        }
        else
        {
            // StateAuthorityを持たない場合：RPC経由でスコア更新を要求
            Debug.Log($"PlayerAvatar: Player {playerId} requesting score update via RPC");
            RPC_UpdateScore(item.itemValue);
        }
    }

    // RPC経由でスコア更新（StateAuthorityを持たないプレイヤー用）
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_UpdateScore(int itemValue)
    {
        Debug.Log($"PlayerAvatar: RPC score update for Player {playerId} (+{itemValue})");
        
        int oldScore = Score;
        Score += itemValue;
        
        Debug.Log($"PlayerAvatar: RPC score updated {oldScore} -> {Score}");
    }

    public override void FixedUpdateNetwork()
    {
        // 移動処理（入力が有効な場合のみ）
        if (HasStateAuthority && inputEnabled)
        {
            var cameraRotation = Quaternion.Euler(0f, Camera.main.transform.rotation.eulerAngles.y, 0f);
            var inputDirection = new Vector3(Input.GetAxis("Horizontal"), 0f, Input.GetAxis("Vertical"));
            
            // 入力があった場合のみログ出力
            if (inputDirection.magnitude > 0.01f)
            {
                // Debug.Log($"Player {playerId}: Moving with input {inputDirection}");
            }
            
            characterController.Move(cameraRotation * inputDirection);
            
            // ジャンプ
            if (Input.GetKey(KeyCode.Space))
            {
                Debug.Log($"Player {playerId}: Jump input detected");
                characterController.Jump();
            }
        }
        else if (!HasStateAuthority && inputEnabled)
        {
            // 権限がない場合の警告（一回だけ表示）
            if (Time.fixedTime % 5.0f < 0.02f) // 5秒ごとに表示
            {
                Debug.LogWarning($"Player {playerId}: Input enabled but no StateAuthority!");
            }
        }

        // アニメーション（ここでは説明を簡単にするため、かなり大雑把な設定になっています）
        var animator = networkAnimator.Animator;
        var grounded = characterController.Grounded;
        var vy = characterController.Velocity.y;
        animator.SetFloat("Speed", characterController.Velocity.magnitude);
        animator.SetBool("Jump", !grounded && vy > 4f);
        animator.SetBool("Grounded", grounded);
        animator.SetBool("FreeFall", !grounded && vy < -4f);
        animator.SetFloat("MotionSpeed", 1f);
    }

    // 入力の有効/無効を設定
    public void SetInputEnabled(bool enabled)
    {
        inputEnabled = enabled;
        Debug.Log($"PlayerAvatar: Player {playerId} input {(enabled ? "ENABLED" : "DISABLED")} (HasStateAuthority: {HasStateAuthority})");
    }

    // RPCで勝者メッセージを全クライアントに送信
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BroadcastWinnerMessage(string winnerMessage)
    {
        Debug.Log($"PlayerAvatar: Winner message received - {winnerMessage}");
        // GameEventsを通じて全クライアントに勝者メッセージを配信
        GameEvents.TriggerWinnerDetermined(winnerMessage);
    }
    
    /// <summary>
    /// スコアをリセット（ゲーム再開時に使用）
    /// </summary>
    public void ResetScore()
    {
        if (HasStateAuthority)
        {
            int oldScore = Score;
            Score = 0;
            Debug.Log($"PlayerAvatar: Score reset for Player {playerId} ({oldScore} -> 0)");
            
            // スコア変更をイベントで通知
            GameEvents.TriggerPlayerScoreChanged(playerId, Score);
        }
    }
    
    /// <summary>
    /// プレイヤーをSpawn位置に戻す（ゲーム再開時に使用）
    /// </summary>
    public void ResetToSpawnPosition()
    {
        if (HasStateAuthority)
        {
            Vector3 oldPosition = transform.position;
            
            // NetworkCharacterControllerを使用している場合は、それを通じて位置を設定
            if (characterController != null)
            {
                characterController.Teleport(spawnPosition);
            }
            else
            {
                transform.position = spawnPosition;
            }
            
            Debug.Log($"PlayerAvatar: Player {playerId} reset to spawn position ({oldPosition} -> {spawnPosition})");
        }
    }
    
    /// <summary>
    /// 再開クリックを全クライアントに同期
    /// </summary>
    public void NotifyRestartClick()
    {
        Debug.Log($"=== PlayerAvatar: NotifyRestartClick() called ===");
        Debug.Log($"PlayerAvatar: Player {playerId} NotifyRestartClick - TIMESTAMP: {System.DateTime.Now:HH:mm:ss.fff}");
        Debug.Log($"PlayerAvatar: Player {playerId} HasStateAuthority: {HasStateAuthority}");
        
        if (HasStateAuthority)
        {
            Debug.Log($"PlayerAvatar: Player {playerId} has StateAuthority - sending RPC_NotifyRestartClick");
            
            // *** 重要：まずローカルでイベントを発火 ***
            Debug.Log($"PlayerAvatar: Player {playerId} - Triggering LOCAL GameEvents first");
            GameEvents.TriggerPlayerClickedForRestart(playerId);
            
            // その後、他のクライアントにRPCを送信
            Debug.Log($"PlayerAvatar: Player {playerId} - Now sending RPC to other clients");
            RPC_NotifyRestartClick(playerId);
            Debug.Log($"PlayerAvatar: Player {playerId} RPC_NotifyRestartClick({playerId}) call completed");
        }
        else
        {
            Debug.LogWarning($"PlayerAvatar: Player {playerId} does not have StateAuthority - cannot send restart click RPC");
            Debug.LogWarning($"PlayerAvatar: Player {playerId} StateAuthority check failed - HasStateAuthority: {HasStateAuthority}");
        }
    }
    
    // RPC: プレイヤーの再開クリックを全クライアントに通知
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_NotifyRestartClick(int clickedPlayerId)
    {
        Debug.Log($"=== PlayerAvatar: RPC_NotifyRestartClick received ===");
        Debug.Log($"PlayerAvatar: *** RPC RECEIVED FOR PLAYER ID: {clickedPlayerId} *** - TIMESTAMP: {System.DateTime.Now:HH:mm:ss.fff}");
        Debug.Log($"PlayerAvatar: This PlayerAvatar ID: {playerId}");
        Debug.Log($"PlayerAvatar: HasStateAuthority: {HasStateAuthority}");
        
        // 自分自身のクリックの場合はスキップ（ローカルで既に処理済み）
        if (HasStateAuthority && clickedPlayerId == playerId)
        {
            Debug.Log($"PlayerAvatar: Skipping RPC processing for local player {clickedPlayerId} - already processed locally");
            return;
        }
        
        Debug.Log($"PlayerAvatar: Processing RPC for remote player {clickedPlayerId}");
        Debug.Log($"PlayerAvatar: About to trigger GameEvents.TriggerPlayerClickedForRestart({clickedPlayerId})");
        GameEvents.TriggerPlayerClickedForRestart(clickedPlayerId);
        Debug.Log($"PlayerAvatar: GameEvents.TriggerPlayerClickedForRestart({clickedPlayerId}) completed");
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
    
    // RPC: カウントダウン更新を全クライアントに通知
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_NotifyCountdownUpdate(int remainingSeconds)
    {
        Debug.Log($"PlayerAvatar: RPC_NotifyCountdownUpdate received - {remainingSeconds} seconds remaining");
        GameEvents.TriggerCountdownUpdate(remainingSeconds);
    }
    
    /// <summary>
    /// ゲーム開始を全クライアントに同期
    /// </summary>
    public void NotifyGameStart()
    {
        if (HasStateAuthority)
        {
            RPC_NotifyGameStateChanged(GameState.InGame);
        }
    }
    
    /// <summary>
    /// ゲーム状態変更を全クライアントに同期
    /// </summary>
    public void NotifyGameStateChanged(GameState newState)
    {
        if (HasStateAuthority)
        {
            RPC_NotifyGameStateChanged(newState);
        }
    }
    
    // RPC: ゲーム状態変更を全クライアントに通知
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_NotifyGameStateChanged(GameState newState)
    {
        Debug.Log($"PlayerAvatar: RPC_NotifyGameStateChanged received - New state: {newState}");
        GameEvents.TriggerGameStateChanged(newState);
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
    
    // RPC: プレイヤー操作開放を全クライアントに通知
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_NotifyEnableAllPlayersInput(bool enabled)
    {
        Debug.Log($"PlayerAvatar: RPC_NotifyEnableAllPlayersInput received - enabled: {enabled}");
        
        // GameControllerのEnableAllPlayersInputを直接呼び出すことはできないので、
        // GameEventsを使用してイベントを発火
        GameEvents.TriggerPlayerInputStateChanged(enabled);
    }
    
    /// <summary>
    /// ゲーム再開処理を全クライアントに同期
    /// </summary>
    public void NotifyGameRestart()
    {
        if (HasStateAuthority)
        {
            Debug.Log($"PlayerAvatar: Player {playerId} sending RPC_NotifyGameRestart");
            RPC_NotifyGameRestart();
        }
        else
        {
            Debug.LogWarning($"PlayerAvatar: Player {playerId} does not have StateAuthority - cannot send restart RPC");
        }
    }
    
    // RPC: ゲーム再開処理を全クライアントに通知
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_NotifyGameRestart()
    {
        Debug.Log($"=== PlayerAvatar: RPC_NotifyGameRestart received ===");
        Debug.Log($"PlayerAvatar: Player {playerId} received restart RPC - triggering game restart execution - CALL #{System.DateTime.Now:HH:mm:ss.fff}");
        Debug.Log($"PlayerAvatar: About to call GameEvents.TriggerGameRestartExecution()");
        GameEvents.TriggerGameRestartExecution();
        Debug.Log($"PlayerAvatar: GameEvents.TriggerGameRestartExecution() completed");
    }
    
    // アイテムリセット通知（マスタークライアントから呼び出し）
    public void NotifyItemsReset()
    {
        Debug.Log($"=== PlayerAvatar: NotifyItemsReset() called ===");
        Debug.Log($"PlayerAvatar: Player {playerId} NotifyItemsReset - HasStateAuthority: {HasStateAuthority}");
        
        if (HasStateAuthority)
        {
            Debug.Log($"PlayerAvatar: Player {playerId} has StateAuthority - sending RPC to reset all items");
            RPC_NotifyItemsReset();
            Debug.Log($"PlayerAvatar: Player {playerId} item reset RPC sent successfully");
        }
        else
        {
            Debug.LogWarning($"PlayerAvatar: Player {playerId} does not have StateAuthority - cannot send item reset RPC");
        }
    }
    
    // RPC: アイテムリセットを全クライアントに通知
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_NotifyItemsReset()
    {
        Debug.Log($"=== PlayerAvatar: RPC_NotifyItemsReset received ===");
        Debug.Log($"PlayerAvatar: Player {playerId} received item reset RPC - TIMESTAMP: {System.DateTime.Now:HH:mm:ss.fff}");
        Debug.Log($"PlayerAvatar: About to trigger GameEvents.TriggerItemsReset()");
        GameEvents.TriggerItemsReset();
        Debug.Log($"PlayerAvatar: GameEvents.TriggerItemsReset() completed");
    }
}