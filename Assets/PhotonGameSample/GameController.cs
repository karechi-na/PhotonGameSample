using UnityEngine;
using Fusion;
using TMPro;
using System.Collections.Generic;
/// <summary>
/// GameController is responsible for managing the game state and handling player interactions.
/// Stay within the maximum player limit and manage player models.
/// </summary>
public class GameController : MonoBehaviour
{
    const int MAX_PLAYERS = 2; // Maximum number of players allowed in the game

    private bool isMasterClient = false; // Flag to check if the current client is the master client

    /// <summary>
    /// external references to GameLauncher and ItemManager components.
    /// </summary>
    [SerializeField] private GameLauncher gameLauncher;
    [SerializeField] private ItemManager itemManager;

    /// <summary>
    /// PlayerModel array to hold player data.
    /// </summary>
    [SerializeField] private NetworkBehaviour PlayerModelPrefab;
    [SerializeField] private TextMeshProUGUI[] scoreText;
    [SerializeField] private Dictionary<int, PlayerModel> playerModels;
    /// <summary>
    /// GameState enum defines the possible states of the game.
    /// </summary>
    public enum GameState
    {
        WaitingForPlayers,
        InGame,
        GameOver
    }

    private GameState currentGameState = GameState.WaitingForPlayers;
    public GameState CurrentGameState
    {
        get { return currentGameState; }
        set
        {
            if (currentGameState == value)
            {
                // 状態が変わらない場合は何もしない
                return;
            }
            else
            {
                Debug.Log("Game State Changed: " + currentGameState);
                OnChangeState(value);
                currentGameState = value;
            }

        }
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Awake()
    {
        gameLauncher.OnJoindClient += OnjoindClient;
        playerModels = new Dictionary<int, PlayerModel>();
    }

    private void OnjoindClient(NetworkRunner runner, int playerId, NetworkObject networkObject, bool isMasterClient)
    {
        if (isMasterClient && networkObject != null)
        {
            this.isMasterClient = true; // Set the flag to true if this client is the master client
            // マスタークライアントに参加したときにアイテムをスポーンする
            itemManager.SpawnItem(runner, 0);
        }
        Debug.Log($"CATCH Joined Client: {playerId}, isMasterClient: {isMasterClient}");
        if (this.isMasterClient && !playerModels.ContainsKey(playerId) && playerModels.Count < MAX_PLAYERS)
        {
            // クライアントに参加したときの処理をここに記述します
            playerModels[playerId] = runner.Spawn(PlayerModelPrefab).GetComponent<PlayerModel>();
            playerModels[playerId].transform.SetParent(transform); // Set the parent to GameController for organization
            playerModels[playerId].scoreText = scoreText[playerModels.Count - 1]; // Initialize the player's score
        }
        if (networkObject != null)
        {
            networkObject.GetComponent<ItemCatcher>().OnItemCaught += (item, playerAvatar) =>
            {
                // スコアを更新する
                playerModels[playerId].score += item.itemValue;
            };
            playerModels[playerId].OnScoreChanged += (score) =>
            {
                // スコアが変わったときの処理をここに記述します
                playerModels[playerId].scoreText.text = $"Player {playerId} Score: {score}";
            };
        }

    }
    private void OnChangeState(GameState newState)
    {
        // 状態が変わったときの処理をここに記述します
        switch (newState)
        {
            case GameState.WaitingForPlayers:
                Debug.Log("Waiting for players to join...");
                break;
            case GameState.InGame:
                Debug.Log("Game is now in progress.");
                break;
            case GameState.GameOver:
                Debug.Log("Game Over!");
                break;
        }
    }
}
