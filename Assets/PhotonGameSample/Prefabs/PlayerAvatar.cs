using Fusion;
using UnityEngine;
using System;
using System.Collections.Generic;

public class PlayerAvatar : NetworkBehaviour
{
    [Networked]
    public NetworkString<_16> NickName { get; set; }

    [Networked] public int playerId { get; set; } = 0;
    [Networked, OnChangedRender(nameof(OnScoreChangedRender))] 
    public int Score { get; set; } = 0; // ネットワーク同期されるスコア
    
    private NetworkCharacterController characterController;
    private NetworkMecanimAnimator networkAnimator;
    
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
        int scoreDiff = Score - previousScore;
        Debug.Log($"PlayerAvatar: Player {playerId} score {previousScore} -> {Score} (diff: {scoreDiff:+#;-#;0})");
        
        OnScoreChanged?.Invoke(playerId, Score);
        previousScore = Score;
        
        // スコア更新完了を通知（ゲーム終了判定で使用）
        GameEvents.TriggerScoreUpdateCompleted(playerId, Score);
    }

    public override void Spawned()
    {
        Debug.Log($"PlayerAvatar: Player {playerId} spawned (Authority: {HasStateAuthority})");
        
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
    }

    // RPCで勝者メッセージを全クライアントに送信
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BroadcastWinnerMessage(string winnerMessage)
    {
        Debug.Log($"PlayerAvatar: Winner message received - {winnerMessage}");
        // GameEventsを通じて全クライアントに勝者メッセージを配信
        GameEvents.TriggerWinnerDetermined(winnerMessage);
    }
}