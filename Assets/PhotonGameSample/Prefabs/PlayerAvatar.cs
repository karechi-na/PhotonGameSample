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
        // レプリケーションで値が届いた側（通常は非 StateAuthority）で UI へイベント連携
        // 既に権限側で GameEvents.TriggerPlayerScoreChanged 済みの場合でも二重発火は UI 上問題ないが、
        // 重複検出したい場合は previousScore で差分チェック可能。
        if (!HasStateAuthority)
        {
            Debug.Log($"PlayerAvatar[{playerId}] OnScoreChangedRender replicated Score={Score}");
            OnScoreChanged?.Invoke(playerId, Score);
            GameEvents.TriggerPlayerScoreChanged(playerId, Score);
        }
        previousScore = Score;
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
        Debug.Log($"PlayerAvatar[{playerId}] OnItemCaught call#{onItemCaughtCallCount} item='{item.name}' HasStateAuthority={HasStateAuthority} HasInputAuthority={HasInputAuthority} local?={Object.HasInputAuthority}");
        // 重複処理防止
        int itemInstanceId = item.GetInstanceID();
        if (processedItems.Contains(itemInstanceId))
        {
            Debug.Log($"PlayerAvatar[{playerId}] Item {item.name} already processed, skipping");
            return;
        }
        processedItems.Add(itemInstanceId);

        if (HasStateAuthority)
        {
            ApplyScoreDelta(item.itemValue, reason:$"OnItemCaught-authority item={item.name}");
        }
        else
        {
            Debug.Log($"PlayerAvatar[{playerId}] forwarding score delta via RPC_UpdateScore value={item.itemValue}");
            RPC_UpdateScore(item.itemValue);
        }
    }

    private void ApplyScoreDelta(int delta, string reason)
    {
        int before = Score;
        Score = Score + delta;
        Debug.Log($"PlayerAvatar[{playerId}] ApplyScoreDelta +{delta} ({reason}) before={before} after={Score} HasStateAuthority={HasStateAuthority}");
        // 権限側で即イベント発火（UI 反映）
        GameEvents.TriggerPlayerScoreChanged(playerId, Score);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_UpdateScore(int itemValue)
    {
        Debug.Log($"PlayerAvatar[{playerId}] RPC_UpdateScore received value={itemValue} (StateAuthority={HasStateAuthority})");
        ApplyScoreDelta(itemValue, reason:"RPC_UpdateScore");
    }

    /// <summary>
    /// プレイヤーのクリックをStateAuthorityに通知するRPC
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_NotifyPlayerClickForRestart()
    {
        // StateAuthorityでのみ実行される
        // 実際にクリックしたプレイヤーのIDを使用（this.playerId）
        Debug.Log($"PlayerAvatar: RPC_NotifyPlayerClickForRestart called - clicked player ID: {playerId}");
        if (HasStateAuthority)
        {
            // このPlayerAvatarの持ち主（playerId）がクリックしたとして処理
            GameEvents.TriggerPlayerClickedForRestart(playerId);
        }
    }

    /// <summary>
    /// ゲーム再開処理を全クライアントに通知するRPC
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_NotifyGameRestart()
    {
        GameEvents.TriggerGameRestartExecution();
    }

    /// <summary>
    /// ゲーム再開処理を開始する（StateAuthorityのみ）
    /// </summary>
    public void NotifyGameRestart()
    {
        if (HasStateAuthority)
        {
            RPC_NotifyGameRestart();
        }
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
            processedItems.Clear(); // ラウンド再開時に取得済みアイテム履歴をクリア
            Debug.Log($"PlayerAvatar[{playerId}] ResetScore old={oldScore} new={Score} processedItems cleared");
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