using Fusion;
using UnityEngine;

public class Item : NetworkBehaviour // NetworkBehaviourを継承します
{
    private Vector3 startPosition;
    private Vector3 endPosition;
    public float speed = 1.0f;
    [SerializeField]public int itemValue { get; private set; } = 1;  // アイテムの値を定義します
    [SerializeField] private Vector3 target = Vector3.forward * 5.0f;

    // 位置をネットワークで同期
    [Networked]
    public Vector3 NetworkedPosition { get; set; }  // NetworkedPositionプロパティを定義します
    
    // アイテムがアクティブかどうかをネットワークで同期
    [Networked, OnChangedRender(nameof(OnItemActiveChanged))]
    public bool IsItemActive { get; set; } = true;

    public override void Spawned()  // Start()の代わり。Spawnedメソッドは、オブジェクトがスポーンされたときに呼び出されます
    {
        // 初期位置を保存
        startPosition = transform.position;
        endPosition = startPosition + target;

        // StateAuthorityのみが位置を制御
        if (Object.HasStateAuthority)
        {
            NetworkedPosition = startPosition;
            IsItemActive = true; // 初期状態はアクティブ
        }
        else
        {
            // クライアントは即座に同期位置に移動
            transform.position = NetworkedPosition;
        }
        
        // 初期状態を設定
        UpdateVisibility();
    }

    public override void FixedUpdateNetwork()
    {
        // アクティブでない場合は移動処理をスキップ
        if (!IsItemActive) return;
        
        if (Object.HasStateAuthority)
        {
            // tは0～1の間を往復する
            float t = Mathf.PingPong(Runner.SimulationTime * speed, 1.0f);
            // 線形補間で位置を更新
            NetworkedPosition = Vector3.Lerp(startPosition, endPosition, t);
        }

        // すべてのクライアントで同期位置に移動
        transform.position = NetworkedPosition;
    }
    
    // アイテムの表示状態を更新
    private void UpdateVisibility()
    {
        gameObject.SetActive(IsItemActive);
    }
    
    // ネットワーク同期時のコールバック：アイテムのアクティブ状態が変更された時
    private void OnItemActiveChanged()
    {
        UpdateVisibility();
    }
    
    /// <summary>
    /// アイテムをリセット（ゲーム再開時に使用）
    /// </summary>
    public void ResetItem()
    {
        if (Object.HasStateAuthority)
        {
            IsItemActive = true;
            NetworkedPosition = startPosition;
            
            // 確実に全クライアントに同期するためのRPC送信
            RPC_ActivateItem();
        }
    }
    
    /// <summary>
    /// アイテムを強制的にリセット（権限チェックなし）
    /// </summary>
    public void ForceResetItem()
    {
        // 権限チェックなしで直接リセット
        IsItemActive = true;
        NetworkedPosition = startPosition;
        
        // GameObjectのアクティブ状態を更新
        gameObject.SetActive(true);
        
        Debug.Log($"Item '{name}' reset to active state");
    }
    void OnTriggerEnter(Collider other)
    {
        // アイテムがアクティブでない場合は処理しない
        if (!IsItemActive) return;
        
        // StateAuthorityを持つクライアントでのみアイテム処理を実行
        if (!Object.HasStateAuthority) return;

        // アイテムがキャッチされたときの処理
        if (other.TryGetComponent(out ItemCatcher itemCatcher))
        {
            Debug.Log($"Item '{name}' caught by {other.name}");
            
            // アイテムキャッチャーのイベントを呼び出す
            itemCatcher.ItemCought(this);
            
            // アイテムを非アクティブにする（削除ではなく非表示）
            IsItemActive = false;
            
            // 確実に全クライアントに同期するためのRPC送信
            RPC_DeactivateItem();
        }
    }
    
    // アイテム非アクティブ化のRPC
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_DeactivateItem()
    {
        gameObject.SetActive(false);
    }
    
    // アイテムアクティブ化のRPC
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ActivateItem()
    {
        gameObject.SetActive(true);
    }
    
    /// <summary>
    /// アイテムを非アクティブにする（ItemManagerから呼び出し）
    /// </summary>
    public void DeactivateItem()
    {
        if (Object.HasStateAuthority)
        {
            IsItemActive = false;
            RPC_DeactivateItem();
        }
    }
}
