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
        Debug.Log($"OnScoreChangedRender called for Player {playerId}: {previousScore} -> {Score}" +
                  $"\n  OnScoreChanged event subscribers: {OnScoreChanged?.GetInvocationList()?.Length ?? 0}");
        OnScoreChanged?.Invoke(playerId, Score);
        previousScore = Score;
    }

    public override void Spawned()
    {
        Debug.Log($"🚀 PlayerAvatar.Spawned() called for Player {playerId}" +
                  $"\n  HasStateAuthority: {HasStateAuthority}" +
                  $"\n  NickName: '{NickName.Value}'" +
                  $"\n  Score: {Score}");
        
        characterController = GetComponent<NetworkCharacterController>();
        networkAnimator = GetComponentInChildren<NetworkMecanimAnimator>();

        var view = GetComponent<PlayerAvatarView>();
        view.SetNickName(NickName.Value);
        if (HasStateAuthority)
        {
            view.MakeCameraTarget();
            Debug.Log($"Player {playerId}: Set as camera target (has state authority)");
        }
        else
        {
            Debug.Log($"Player {playerId}: Not camera target (no state authority)");
        }

        // ItemCatcherのイベントをサブスクライブ
        var itemCatcher = GetComponent<ItemCatcher>();
        if (itemCatcher != null)
        {
            itemCatcher.OnItemCaught += OnItemCaught;
        }

        previousScore = Score;
        
        Debug.Log($"✅ PlayerAvatar {playerId} spawned successfully and ready for registration");
    }

    private void OnItemCaught(Item item, PlayerAvatar playerAvatar)
    {
        Debug.Log($"=== OnItemCaught called ==="
            + $"\nPlayer {playerId} ({NickName.Value}) caught item"
            + $"\nItem value: {item.itemValue}"
            + $"\nHasStateAuthority: {HasStateAuthority}"
            + $"\nCurrent Score before: {Score}");
        
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
            
            // 入力があった場合のみログ出力
            if (inputDirection.magnitude > 0.01f)
            {
                Debug.Log($"Player {playerId}: Moving with input {inputDirection}");
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
        Debug.Log($"Player {playerId} input set to: {enabled} (HasStateAuthority: {HasStateAuthority})");
    }
}