using Fusion;
using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// 各プレイヤーのアバター（キャラクター）を表し、スコアや入力、ネットワーク同期、各種RPCを管理します。
/// </summary>
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

    /// <summary>
    /// スコア変更時のネットワークコールバック。
    /// </summary>
    private void OnScoreChangedRender()
    {
        OnScoreChanged?.Invoke(playerId, Score);
        previousScore = Score;
        
        // スコア更新完了を通知（ゲーム終了判定で使用）
        GameEvents.TriggerScoreUpdateCompleted(playerId, Score);
    }

    /// <summary>
    /// プレイヤーアバターのスポーン時処理。
    /// </summary>
    public override void Spawned()
    {
        // プレイヤーIDが設定されていない場合はPlayerRefから取得
        if (playerId == 0 && HasStateAuthority)
        {
            playerId = Object.InputAuthority.PlayerId;
        }
        
        // Spawn位置を保存
        spawnPosition = transform.position;
        
        characterController = GetComponent<NetworkCharacterController>();
        networkAnimator = GetComponentInChildren<NetworkMecanimAnimator>();

        var view = GetComponent<PlayerAvatarView>();
        view.SetNickName(NickName.Value);
        if (HasStateAuthority)
        {
            view.MakeCameraTarget();
        }

        // ItemCatcherのイベントをサブスクライブ
        var itemCatcher = GetComponent<ItemCatcher>();
        if (itemCatcher != null)
        {
            itemCatcher.OnItemCaught += OnItemCaught;
        }

        previousScore = Score;
    }

    /// <summary>
    /// アイテム取得時の処理。
    /// </summary>
    /// <param name="item">取得したアイテム</param>
    /// <param name="playerAvatar">取得したプレイヤー</param>
    private void OnItemCaught(Item item, PlayerAvatar playerAvatar)
    {
        onItemCaughtCallCount++;
        
        // アイテムの重複処理防止チェック
        int itemInstanceId = item.GetInstanceID();
        
        if (processedItems.Contains(itemInstanceId))
        {
            return;
        }
        processedItems.Add(itemInstanceId);

        if (HasStateAuthority)
        {
            // 自分がStateAuthorityを持つ場合：直接スコア更新
            int oldScore = Score;
            Score += item.itemValue;
        }
        else
        {
            // StateAuthorityを持たない場合：RPC経由でスコア更新を要求
            RPC_UpdateScore(item.itemValue);
        }
    }

    // RPC経由でスコア更新（StateAuthorityを持たないプレイヤー用）
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_UpdateScore(int itemValue)
    {
        int oldScore = Score;
        Score += itemValue;
    }

    public override void FixedUpdateNetwork()
    {
        // 移動処理（入力が有効な場合のみ）
        if (HasStateAuthority && inputEnabled)
        {
            var cameraRotation = Quaternion.Euler(0f, Camera.main.transform.rotation.eulerAngles.y, 0f);
            var inputDirection = new Vector3(Input.GetAxis("Horizontal"), 0f, Input.GetAxis("Vertical"));
            
            characterController.Move(cameraRotation * inputDirection);
            
            // ジャンプ
            if (Input.GetKey(KeyCode.Space))
            {
                characterController.Jump();
            }
        }

        // アニメーション
        var animator = networkAnimator.Animator;
        var grounded = characterController.Grounded;
        var vy = characterController.Velocity.y;
        animator.SetFloat("Speed", characterController.Velocity.magnitude);
        animator.SetBool("Jump", !grounded && vy > 4f);
        animator.SetBool("Grounded", grounded);
        animator.SetBool("FreeFall", !grounded && vy < -4f);
        animator.SetFloat("MotionSpeed", 1f);
    }

    /// <summary>
    /// 入力の有効/無効を設定。
    /// </summary>
    public void SetInputEnabled(bool enabled)
    {
        inputEnabled = enabled;
    }

    /// <summary>
    /// RPCで勝者メッセージを全クライアントに送信。
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BroadcastWinnerMessage(string winnerMessage)
    {
        // GameEventsを通じて全クライアントに勝者メッセージを配信
        GameEvents.TriggerWinnerDetermined(winnerMessage);
    }
    
    /// <summary>
    /// スコアをリセット（ゲーム再開時に使用）。
    /// </summary>
    public void ResetScore()
    {
        if (HasStateAuthority)
        {
            int oldScore = Score;
            Score = 0;
            // スコア変更をイベントで通知
            GameEvents.TriggerPlayerScoreChanged(playerId, Score);
        }
    }
    
    /// <summary>
    /// プレイヤーをSpawn位置に戻す（ゲーム再開時に使用）。
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
        }
    }
    
    /// <summary>
    /// 再開クリックを全クライアントに同期。
    /// </summary>
    public void NotifyRestartClick()
    {
        if (HasStateAuthority)
        {
            // まずローカルでイベントを発火
            GameEvents.TriggerPlayerClickedForRestart(playerId);
            // その後、他のクライアントにRPCを送信
            RPC_NotifyRestartClick(playerId);
        }
    }
    
    /// <summary>
    /// RPC: プレイヤーの再開クリックを全クライアントに通知。
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_NotifyRestartClick(int clickedPlayerId)
    {
        // 自分自身のクリックの場合はスキップ（ローカルで既に処理済み）
        if (HasStateAuthority && clickedPlayerId == playerId)
        {
            return;
        }
        GameEvents.TriggerPlayerClickedForRestart(clickedPlayerId);
    }
    
    /// <summary>
    /// カウントダウン更新を全クライアントに同期。
    /// </summary>
    public void NotifyCountdownUpdate(int remainingSeconds)
    {
        if (HasStateAuthority)
        {
            RPC_NotifyCountdownUpdate(remainingSeconds);
        }
    }
    
    /// <summary>
    /// RPC: カウントダウン更新を全クライアントに通知。
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_NotifyCountdownUpdate(int remainingSeconds)
    {
        GameEvents.TriggerCountdownUpdate(remainingSeconds);
    }
    
    /// <summary>
    /// ゲーム開始を全クライアントに同期。
    /// </summary>
    public void NotifyGameStart()
    {
        if (HasStateAuthority)
        {
            RPC_NotifyGameStateChanged(GameState.InGame);
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
    /// RPC: ゲーム状態変更を全クライアントに通知。
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_NotifyGameStateChanged(GameState newState)
    {
        GameEvents.TriggerGameStateChanged(newState);
    }
    
    /// <summary>
    /// プレイヤー操作開放を全クライアントに同期。
    /// </summary>
    public void NotifyEnableAllPlayersInput(bool enabled)
    {
        if (HasStateAuthority)
        {
            RPC_NotifyEnableAllPlayersInput(enabled);
        }
    }
    
    /// <summary>
    /// RPC: プレイヤー操作開放を全クライアントに通知。
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_NotifyEnableAllPlayersInput(bool enabled)
    {
        GameEvents.TriggerPlayerInputStateChanged(enabled);
    }
    
    /// <summary>
    /// ゲーム再開処理を全クライアントに同期。
    /// </summary>
    public void NotifyGameRestart()
    {
        if (HasStateAuthority)
        {
            RPC_NotifyGameRestart();
        }
    }
    
    /// <summary>
    /// RPC: ゲーム再開処理を全クライアントに通知。
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_NotifyGameRestart()
    {
        GameEvents.TriggerGameRestartExecution();
    }
    
    /// <summary>
    /// アイテムリセット通知（マスタークライアントから呼び出し）。
    /// </summary>
    public void NotifyItemsReset()
    {
        if (HasStateAuthority)
        {
            RPC_NotifyItemsReset();
        }
    }
    
    /// <summary>
    /// RPC: アイテムリセットを全クライアントに通知。
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_NotifyItemsReset()
    {
        GameEvents.TriggerItemsReset();
    }
}