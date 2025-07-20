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
    
    // 全プレイヤーのアバター参照を保持
    private Dictionary<int, PlayerAvatar> allPlayerAvatars = new Dictionary<int, PlayerAvatar>();
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
    [SerializeField] private TextMeshProUGUI player2ScoreText; // 二人目のプレイヤー用のスコアテキスト
    private PlayerModel localPlayerModel; // ローカルプレイヤーのモデル
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

    void Awake()
    {
        gameLauncher.OnJoindClient += OnjoindClient;
    }

    void Start()
    {
        // UIコンポーネントの確認
        Debug.Log($"GameController UI Check - scoreText: {scoreText}, player2ScoreText: {player2ScoreText}");
        
        // 既存のPlayerAvatarがあれば登録
        StartCoroutine(RegisterExistingPlayers());
    }

    private System.Collections.IEnumerator RegisterExistingPlayers()
    {
        yield return new WaitForSeconds(0.5f); // 少し待ってからプレイヤーを検索
        
        // 既存のプレイヤーを登録
        var existingAvatars = FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None);
        Debug.Log($"Found {existingAvatars.Length} existing PlayerAvatars");
        
        foreach (var avatar in existingAvatars)
        {
            RegisterPlayerAvatar(avatar);
        }

        // 継続的にプレイヤーをチェック（新しいプレイヤーが参加した場合のため）
        StartCoroutine(ContinuousPlayerCheck());
    }

    private System.Collections.IEnumerator ContinuousPlayerCheck()
    {
        while (true)
        {
            yield return new WaitForSeconds(1.0f); // 1秒ごとにチェック
            
            var allAvatars = FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None);
            foreach (var avatar in allAvatars)
            {
                if (avatar != null && !allPlayerAvatars.ContainsKey(avatar.playerId))
                {
                    Debug.Log($"Found new player {avatar.playerId}, registering...");
                    RegisterPlayerAvatar(avatar);
                }
            }
        }
    }

    private void RegisterPlayerAvatar(PlayerAvatar avatar)
    {
        if (avatar != null && !allPlayerAvatars.ContainsKey(avatar.playerId))
        {
            allPlayerAvatars[avatar.playerId] = avatar;
            avatar.OnScoreChanged += OnPlayerScoreChanged;
            Debug.Log($"Registered Player {avatar.playerId} for score updates");
            Debug.Log($"Player {avatar.playerId} current score: {avatar.Score}");
            
            // 即座にUIを初期化
            OnPlayerScoreChanged(avatar.playerId, avatar.Score);
        }
        else if (avatar != null)
        {
            Debug.Log($"Player {avatar.playerId} already registered or avatar is null");
        }
    }

    private void OnjoindClient(NetworkRunner runner, PlayerRef player, bool isMasterClient)
    {
        if (runner.LocalPlayer == player)
        {
            if (isMasterClient)
            {
                this.isMasterClient = true; // Set the flag to true if this client is the master client
                // マスタークライアントに参加したときにアイテムをスポーンする
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

        // スポーンしたプレイヤーのスコア変更イベントをサブスクライブ
        if (spawnedObject != null)
        {
            var playerAvatar = spawnedObject.GetComponent<PlayerAvatar>();
            if (playerAvatar != null)
            {
                RegisterPlayerAvatar(playerAvatar);
            }
        }
    }

    private void OnPlayerScoreChanged(int playerId, int newScore)
    {
        Debug.Log($"OnPlayerScoreChanged called: Player {playerId}, Score {newScore}");
        
        // プレイヤーIDに基づいてUIを更新
        TextMeshProUGUI targetScoreText = null;
        
        if (playerId == 1 && scoreText != null)
        {
            targetScoreText = scoreText;
            Debug.Log("Updating Player 1 score text");
        }
        else if (playerId == 2 && player2ScoreText != null)
        {
            targetScoreText = player2ScoreText;
            Debug.Log("Updating Player 2 score text");
        }
        else
        {
            Debug.LogWarning($"No UI text found for Player {playerId}. scoreText: {scoreText}, player2ScoreText: {player2ScoreText}");
        }

        if (targetScoreText != null)
        {
            targetScoreText.text = $"Player{playerId} Score: {newScore}";
            Debug.Log($"Updated UI for Player {playerId}: {targetScoreText.text}");
        }

        Debug.Log($"Player {playerId} score updated to: {newScore}");
    }

    // テスト用メソッド：手動でスコアを変更
    void Update()
    {
        // デバッグ用：キー入力でスコアを手動変更
        if (Input.GetKeyDown(KeyCode.F1))
        {
            TestScoreUpdate(1);
        }
        if (Input.GetKeyDown(KeyCode.F2))
        {
            TestScoreUpdate(2);
        }
    }

    private void TestScoreUpdate(int playerId)
    {
        Debug.Log($"Manual test: updating score for Player {playerId}");
        OnPlayerScoreChanged(playerId, 999);
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
