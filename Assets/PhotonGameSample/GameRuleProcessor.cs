using UnityEngine;
using System.Collections.Generic;
using System.Linq;

[RequireComponent(typeof(PlayerManager))]
public class GameRuleProcessor : MonoBehaviour
{
    // イベントの発火
    public event System.Action OnGameEndTriggered; // ゲーム終了をトリガー
    public event System.Action<string> OnWinnerDetermined; // 勝者決定を通知

    private PlayerManager playerManager; // PlayerManagerの参照

    void Awake()
    {
        // PlayerManagerの参照を取得
        playerManager = GetComponent<PlayerManager>();
        if (playerManager == null)
        {
            Debug.LogError("GameRuleProcessor: PlayerManager not found!");
        }

        // ItemManagerからのイベント購読（全アイテム収集時）
        if (GetComponent<ItemManager>() != null) // GetComponentで取得するか、SerializeFieldでアサイン
        {
            GetComponent<ItemManager>().OnAllItemsCollected += TriggerGameEndByRule;
        }

        // GameControllerからのゲーム終了イベント購読
        GameEvents.OnGameEnd += HandleGameEndedByController;
    }

    // アイテム収集によるゲーム終了のトリガー
    public void TriggerGameEndByRule()
    {
        Debug.Log("GameRuleProcessor: All items collected, triggering game end.");
        
        // ゲーム終了をGameControllerに通知
        OnGameEndTriggered?.Invoke();
        
        // 同時に勝者決定も実行
        Debug.Log("GameRuleProcessor: All items collected - determining winner");
        DetermineWinner();
    }

    // GameControllerからゲーム終了の指示を受けた場合
    private void HandleGameEndedByController()
    {
        Debug.Log("GameRuleProcessor: Game ended by controller, determining winner.");

        // PlayerManagerから勝者決定
        Debug.Log("GameRuleProcessor: Determining winner after game end");
        DetermineWinner();
    }

    // 勝者決定ロジック
    public void DetermineWinner()
    {
        Debug.Log("GameRuleProcessor: DetermineWinner called");
        
        if (playerManager == null)
        {
            Debug.LogError("GameRuleProcessor: PlayerManager is null!");
            return;
        }

        var (winnerId, highestScore, tiedPlayers) = playerManager.DetermineWinner();
        
        string message;
        if (winnerId == -1)
        {
            message = "引き分けです！";
        }
        else
        {
            // プレイヤー情報を取得してニックネームを表示
            var winnerPlayer = playerManager.GetPlayerAvatar(winnerId);
            string winnerName = winnerPlayer?.NickName.ToString() ?? $"Player {winnerId}";
            message = $"The Winner is {winnerName} !";
        }
        
        Debug.Log($"GameRuleProcessor: Winner determined - {message}");
        
        // ローカルイベントを発火
        GameEvents.TriggerWinnerDetermined(message);
        
        // ネットワーク経由で全クライアントに送信（StateAuthorityを持つプレイヤーから）
        BroadcastWinnerMessageToAllClients(message);
    }

    // RPC経由で勝者メッセージを全クライアントに送信
    private void BroadcastWinnerMessageToAllClients(string message)
    {
        if (playerManager == null) return;
        
        // 最初に見つかったStateAuthorityを持つプレイヤーからRPCを送信
        foreach (var playerPair in playerManager.AllPlayers)
        {
            var playerAvatar = playerPair.Value;
            if (playerAvatar != null && playerAvatar.HasStateAuthority)
            {
                Debug.Log($"GameRuleProcessor: Sending winner message via RPC from Player {playerAvatar.playerId}");
                playerAvatar.RPC_BroadcastWinnerMessage(message);
                return; // 一度送信したら終了
            }
        }
        
        Debug.LogWarning("GameRuleProcessor: No player with StateAuthority found for RPC broadcast");
    }

    void OnDestroy()
    {
        if (GetComponent<ItemManager>() != null)
        {
            GetComponent<ItemManager>().OnAllItemsCollected -= TriggerGameEndByRule;
        }
        GameEvents.OnGameEnd -= HandleGameEndedByController;
    }
}
