using UnityEngine;
using Fusion;
using TMPro;
/// <summary>
/// GameController is responsible for managing the game state and handling player interactions.
/// </summary>
public class GameController: MonoBehaviour
{
    const int MAX_PLAYERS = 2; // Maximum number of players allowed in the game

    /// <summary>
    /// external references to GameLauncher and ItemManager components.
    /// </summary>
    [SerializeField]private GameLauncher gameLauncher;
    [SerializeField]private ItemManager itemManager;
    [SerializeField] private TextMeshProUGUI[] scoreText;
    /// <summary>
    /// GameState enum defines the possible states of the game.
    /// </summary>
    public enum GameState
    {
        WaitingForPlayers,
        InGame,
        GameOver
    }

    private GameState currentGameState  = GameState.WaitingForPlayers;
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

    [SerializeField]private PlayerModel[] players = new PlayerModel[MAX_PLAYERS];

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Awake()
    {
        gameLauncher.OnJoinedMasterClient += OnJoinedMasterClient;
        gameLauncher.OnJoindClient += OnjoindClient;
    }

    private void OnJoinedMasterClient(NetworkRunner runner, int playerId,NetworkObject networkObject)
    {
        // マスタークライアントに参加したときの処理をここに記述します
        Debug.Log("CATCH Joined Master Client");
        // マスタークライアントに参加したときにアイテムをスポーンする
        itemManager.SpawnItem(runner, 0);
        players[playerId] = new PlayerModel(networkObject.GetComponent<PlayerAvatar>().NickName.ToString(), networkObject.gameObject);
        networkObject.GetComponent<PlayerAvatar>().playerId = playerId;

        players[playerId].OnScoreChanged += (score) =>
        {
            // スコアが変わったときの処理をここに記述します
            scoreText[playerId].text = $"Player {playerId} Score: {score}";
        };

    }
    private void OnjoindClient(NetworkRunner runner, int playerId, NetworkObject networkObject)
    {
        // クライアントに参加したときの処理をここに記述します
        players[playerId] = new PlayerModel(networkObject.GetComponent<PlayerAvatar>().NickName.ToString(), networkObject.gameObject);
        players[playerId].OnScoreChanged += (score) =>
        {
            // スコアが変わったときの処理をここに記述します
            scoreText[playerId].text = $"Player {playerId} Score: {score}";
        };

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
