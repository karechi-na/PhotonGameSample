using System;
using System.Collections;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

public class GameLauncher : MonoBehaviour, INetworkRunnerCallbacks
{
    [SerializeField]
    private NetworkRunner networkRunnerPrefab;
    [SerializeField]
    private NetworkPrefabRef playerAvatarPrefab;

    [SerializeField]
    private Vector3[] spawnPosition
        = { 
            new Vector3(-5, 2, 0),
            new Vector3(5, 2, 0),
    };
    [SerializeField] private Quaternion spawnRotation = Quaternion.identity;


    [SerializeField]
    private NetworkPrefabRef itemPrefab;

    private async void Start()
    {
        var networkRunner = Instantiate(networkRunnerPrefab);
        // GameLauncherを、NetworkRunnerのコールバック対象に追加する
        networkRunner.AddCallbacks(this);
        var result = await networkRunner.StartGame(new StartGameArgs
        {
            GameMode = GameMode.Shared
        });
    }

    void INetworkRunnerCallbacks.OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    void INetworkRunnerCallbacks.OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    void INetworkRunnerCallbacks.OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {


        // セッションへ参加したプレイヤーが自分自身かどうかを判定する
        if (player == runner.LocalPlayer)
        {
            // アバターの初期位置を計算する
            var playerIndex = runner.SessionInfo.PlayerCount - 1;
            var spawnedPosition = spawnPosition[playerIndex % spawnPosition.Length];
            // 自分自身のアバターをスポーンする
            runner.Spawn(playerAvatarPrefab, spawnedPosition, Quaternion.identity, onBeforeSpawned: (_, networkObject) =>
            {
                // プレイヤー名のネットワークプロパティの初期値として、ランダムな名前を設定する
                networkObject.GetComponent<PlayerAvatar>().NickName = $"Player{UnityEngine.Random.Range(0, 10000)}";
            });
            // マスタークライアント（ホスト）のみアイテムをスポーン
            if (runner.IsSharedModeMasterClient)
            {
                var spawnd=runner.Spawn(itemPrefab, new Vector3(0.0f, 0.0f, 5.0f), Quaternion.identity);
                Debug.Log($"Item Spawned:{spawnd}");
            }
        }
    }
    void INetworkRunnerCallbacks.OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
    void INetworkRunnerCallbacks.OnInput(NetworkRunner runner, NetworkInput input) { }
    void INetworkRunnerCallbacks.OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    void INetworkRunnerCallbacks.OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    void INetworkRunnerCallbacks.OnConnectedToServer(NetworkRunner runner) { }
    void INetworkRunnerCallbacks.OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    void INetworkRunnerCallbacks.OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    void INetworkRunnerCallbacks.OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    void INetworkRunnerCallbacks.OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    void INetworkRunnerCallbacks.OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    void INetworkRunnerCallbacks.OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    void INetworkRunnerCallbacks.OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    void INetworkRunnerCallbacks.OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    void INetworkRunnerCallbacks.OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    void INetworkRunnerCallbacks.OnSceneLoadDone(NetworkRunner runner) { }
    void INetworkRunnerCallbacks.OnSceneLoadStart(NetworkRunner runner) { }
}