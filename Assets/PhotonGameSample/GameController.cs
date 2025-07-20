using UnityEngine;
using UnityEngine.SceneManagement;
using Fusion;
using TMPro;
using System.Collections;
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
    /// Player spawn positions in the game world.
    /// </summary>
    [SerializeField]
    private Vector3[] spawnPosition
    = {
            new Vector3(-5, 2, 0),
            new Vector3(5, 2, 0),
    };

    [SerializeField] private TextMeshProUGUI statusWindow;

    /// <summary>
    /// external references to GameLauncher and ItemManager components.
    /// </summary>
    [SerializeField] private GameLauncher gameLauncher;
    [SerializeField] private ItemManager itemManager;

    /// <summary>
    /// PlayerModel
    /// </summary>
    [SerializeField] private TextMeshProUGUI scoreText;
    [SerializeField] private PlayerModel playerModel;
    [SerializeField] private PlayerAvatar playerAvatarPrefab;
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
    }

    private void OnjoindClient(NetworkRunner runner, PlayerRef player, bool isMasterClient)
    {
        if (runner.LocalPlayer == player)
        {
            if (isMasterClient)
            {
                this.isMasterClient = true; // Set the flag to true if this client is the master client
                // マスタークライアントに参加したときにアイテムをスポーンする
                // itemManager.SpawnItem(runner, 0);
                if (runner.IsSceneAuthority)
                {
                    runner.LoadScene(SceneRef.FromIndex(1), LoadSceneMode.Additive);
                }
            }

            // シーンが完全に読み込まれるまで少し待ってからスポーンする
            StartCoroutine(SpawnPlayerAfterDelay(runner, player));
        }
    }

    private System.Collections.IEnumerator SpawnPlayerAfterDelay(NetworkRunner runner, PlayerRef player)
    {
        // フレームを少し待つ
        yield return new WaitForEndOfFrame();
        yield return new WaitForSeconds(0.1f);

        var playerIndex = runner.SessionInfo.PlayerCount - 1;
        var spawnedPosition = spawnPosition[playerIndex % spawnPosition.Length];
        
        // 自分自身のアバターをスポーンする
        var spawnedObject = runner.Spawn(playerAvatarPrefab, spawnedPosition, Quaternion.identity, 
            onBeforeSpawned: (_, networkObject) =>
            {
                // プレイヤー名をネットワークプロパティで設定する
                var playerAvatar = networkObject.GetComponent<PlayerAvatar>();
                playerAvatar.NickName = $"Player{player.PlayerId}";
                playerAvatar.playerId = player.PlayerId;
            });

        //PalyerModelの初期化
        playerModel = new PlayerModel();
        if (spawnedObject != null)
        {
            var itemCatcher = spawnedObject.GetComponent<ItemCatcher>();
            if (itemCatcher != null)
            {
                itemCatcher.OnItemCaught += (item, playerAvatar) =>
                {
                    playerModel.AddScore(item.itemValue);
                };
            }
        }

        playerModel.OnScoreChanged += (score) =>
        {
            scoreText.text = $"Score: {score}";
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
