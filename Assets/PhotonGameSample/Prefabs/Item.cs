using Fusion;
using UnityEngine;

public class Item : NetworkBehaviour    // ItemクラスはNetworkBehaviourを継承します
{
    private Vector3 startPosition;
    private Vector3 endPosition;
    public float speed = 1.0f;
    [SerializeField]public int itemValue { get; private set; } = 1;  // アイテムの値を定義します
    [SerializeField] private Vector3 target = Vector3.forward * 5.0f;

    // 位置をネットワークで同期
    [Networked]
    public Vector3 NetworkedPosition { get; set; }  // NetworkedPositionプロパティを定義します

    public override void Spawned()  // Start()の代わり。Spawnedメソッドは、オブジェクトがスポーンされたときに呼び出されます
    {
        // 初期位置を保存
        startPosition = transform.position;
        endPosition = startPosition + target;

        // StateAuthorityのみが位置を制御
        if (Object.HasStateAuthority)
        {
            NetworkedPosition = startPosition;
        }
        else
        {
            // クライアントは即座に同期位置に移動
            transform.position = NetworkedPosition;
        }
    }

    public override void FixedUpdateNetwork()
    {
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
    void OnTriggerEnter(Collider other)
    {
        Debug.Log($"Item caught by {other.name}");

        // アイテムがキャッチされたときの処理
        if (other.TryGetComponent(out ItemCatcher itemCatcher))
        {
            // アイテムキャッチャーのイベントを呼び出す
            itemCatcher.ItemCought(this);
            gameObject.SetActive(false); // アイテムを非アクティブにする

            // アイテムを削除
            Runner.Despawn(Object);
        }
    }
}
