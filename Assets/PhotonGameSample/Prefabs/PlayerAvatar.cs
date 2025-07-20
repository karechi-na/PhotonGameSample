using Fusion;
using UnityEngine;
using System;

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

    // ネットワークプロパティ変更時のコールバック
    private void OnScoreChangedRender()
    {
        Debug.Log($"OnScoreChangedRender called for Player {playerId}: {previousScore} -> {Score}");
        Debug.Log($"OnScoreChanged event subscribers: {OnScoreChanged?.GetInvocationList()?.Length ?? 0}");
        OnScoreChanged?.Invoke(playerId, Score);
        previousScore = Score;
    }

    public override void Spawned()
    {
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
        Debug.Log($"=== OnItemCaught called ===");
        Debug.Log($"Player {playerId} ({NickName.Value}) caught item");
        Debug.Log($"Item value: {item.itemValue}");
        Debug.Log($"HasStateAuthority: {HasStateAuthority}");
        Debug.Log($"Current Score before: {Score}");
        
        if (HasStateAuthority)
        {
            // スコアを加算（ネットワーク同期される）
            int oldScore = Score;
            Score += item.itemValue;
            Debug.Log($"=== SCORE UPDATED === Player {playerId} caught item! Score: {oldScore} -> {Score}");
        }
        else
        {
            Debug.Log($"Player {playerId} does not have state authority - score not updated");
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
        Debug.Log($"Player {playerId} input set to: {enabled}");
    }
}