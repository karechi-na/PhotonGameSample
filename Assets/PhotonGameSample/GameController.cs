using UnityEngine;
using UnityEngine.SceneManagement;
using Fusion;
using TMPro;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(ItemManager), typeof(PlayerManager), typeof(GameUIManager))]
[RequireComponent(typeof(GameRuleProcessor))]
public class GameController : MonoBehaviour
{
    const int MAX_PLAYERS = 2;
    const int COUNTDOWN_SECONDS = 5;

    private bool gameEnded = false;
    private bool isCountdownRunning = false;
    private Coroutine countdownCoroutine;
    private HashSet<int> playersClickedForRestart = new HashSet<int>();

    [SerializeField] private ItemManager itemManager;
    [SerializeField] private NetworkGameManager networkGameManager;
    [SerializeField] private PlayerManager playerManager;
    private GameUIManager gameUIManager;
    private GameRuleProcessor gameRuleProcessor;

    private PlayerModel localPlayerModel;

    private GameState currentGameState = GameState.WaitingForPlayers;
    public GameState CurrentGameState
    {
        get { return currentGameState; }
        set
        {
            if (currentGameState == value)
            {
                return;
            }
            else
            {
                OnChangeState(value);
                currentGameState = value;
            }
        }
    }

    void Awake()
    {
        gameUIManager = GetComponent<GameUIManager>();

        networkGameManager = GetComponent<NetworkGameManager>();
        if (networkGameManager == null)
        {
            networkGameManager = FindFirstObjectByType<NetworkGameManager>();
        }

        if (networkGameManager != null)
        {
            networkGameManager.OnClientJoined += OnClientJoined;
            networkGameManager.OnPlayerSpawned += OnPlayerSpawned;
            networkGameManager.OnPlayerLeft += OnPlayerLeft;
            networkGameManager.OnGameEndRequested += OnGameEndRequested;
        }

        playerManager = GetComponent<PlayerManager>();
        if (playerManager != null)
        {
            playerManager.OnPlayerRegistered += OnPlayerRegistered;
            playerManager.OnPlayerUnregistered += OnPlayerUnregistered;
            playerManager.OnPlayerScoreChanged += OnPlayerScoreChanged;
            playerManager.OnPlayerCountChanged += OnPlayerCountChanged;
        }

        gameRuleProcessor = GetComponent<GameRuleProcessor>();
        if (gameRuleProcessor != null)
        {
            gameRuleProcessor.OnGameEndTriggered += EndGame;
        }

        GameEvents.OnGameRestartRequested += RestartGame;
        GameEvents.OnGameRestartExecution += ExecuteRestart;
        GameEvents.OnPlayerClickedForRestart += OnPlayerClickedForRestart;
        GameEvents.OnPlayerInputStateChanged += OnPlayerInputStateChanged;
    }

    void Start()
    {
        itemManager = GetComponent<ItemManager>();
        InitializeItemManager();
    }

    private void InitializeItemManager()
    {
        if (itemManager != null)
        {
            itemManager.OnItemCountChanged += OnItemCountChanged;
        }
    }

    private void OnItemCountChanged(int collectedCount, int totalCount)
    {
        // 進捗が全て集まった場合のみ何か処理したい場合はここに記述
    }

    private void OnPlayerRegistered(PlayerAvatar avatar)
    {
        if (itemManager != null)
        {
            itemManager.RegisterPlayer(avatar);
        }
    }

    private void OnPlayerUnregistered(int playerId)
    {
    }

    private void OnPlayerCountChanged(int playerCount)
    {
        GameEvents.TriggerPlayerCountChanged(playerCount);

        if (networkGameManager != null && networkGameManager.NetworkRunner != null)
        {
            CheckPlayerCountAndUpdateGameState(networkGameManager.NetworkRunner);
        }
    }

    private void EndGame()
    {
        if (gameEnded) return;

        gameEnded = true;
        CurrentGameState = GameState.GameOver;

        EnableAllPlayersInput(false);

        playersClickedForRestart.Clear();

        GameEvents.TriggerGameEnd();
    }

    private void RestartGame()
    {
        if (networkGameManager != null && networkGameManager.IsMasterClient)
        {
            PlayerAvatar[] playerAvatars = FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None);
            PlayerAvatar masterPlayerAvatar = null;
            foreach (var avatar in playerAvatars)
            {
                if (avatar.HasStateAuthority)
                {
                    masterPlayerAvatar = avatar;
                    break;
                }
            }
            if (masterPlayerAvatar != null)
            {
                masterPlayerAvatar.NotifyGameRestart();
            }
            else
            {
                ExecuteRestart();
            }
        }
    }

    private void ExecuteRestart()
    {
        playersClickedForRestart.Clear();
        gameEnded = false;

        if (gameUIManager != null)
        {
            gameUIManager.ResetWinnerMessageFlag();
        }

        if (playerManager != null)
        {
            playerManager.ResetAllPlayersToSpawnPosition();
        }

        if (itemManager != null)
        {
            if (networkGameManager != null && networkGameManager.IsMasterClient)
            {
                PlayerAvatar[] playerAvatars = FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None);
                PlayerAvatar masterPlayerAvatar = null;
                foreach (var avatar in playerAvatars)
                {
                    if (avatar.HasStateAuthority)
                    {
                        masterPlayerAvatar = avatar;
                        break;
                    }
                }
                if (masterPlayerAvatar != null)
                {
                    masterPlayerAvatar.NotifyItemsReset();
                }
                else
                {
                    itemManager.ResetAllItemsViaRPC();
                }
            }
        }

        if (playerManager != null)
        {
            playerManager.ResetAllPlayersScore();
        }

        if (playerManager != null && playerManager.PlayerCount >= MAX_PLAYERS)
        {
            StartGameCountdown();
        }
        else
        {
            CurrentGameState = GameState.WaitingForPlayers;
        }
    }

    private void OnPlayerClickedForRestart(int playerId)
    {
        if (playersClickedForRestart.Contains(playerId))
        {
            return;
        }

        playersClickedForRestart.Add(playerId);

        if (playersClickedForRestart.Count >= MAX_PLAYERS)
        {
            if (networkGameManager != null && networkGameManager.IsMasterClient)
            {
                RestartGame();
            }
        }
    }

    private void OnClientJoined(NetworkRunner runner, PlayerRef player, bool isMasterClient)
    {
    }

    private void OnPlayerSpawned(PlayerAvatar playerAvatar)
    {
        if (playerManager != null)
        {
            playerManager.RegisterPlayerAvatar(playerAvatar);
        }

        if (networkGameManager != null && networkGameManager.NetworkRunner != null)
        {
            CheckPlayerCountAndUpdateGameState(networkGameManager.NetworkRunner);
        }
    }

    private void OnGameEndRequested()
    {
        EndGame();
    }

    private void CheckPlayerCountAndUpdateGameState(NetworkRunner runner)
    {
        int playerCount = runner.SessionInfo.PlayerCount;
        int registeredPlayers = playerManager != null ? playerManager.PlayerCount : 0;

        if (registeredPlayers >= MAX_PLAYERS && CurrentGameState == GameState.WaitingForPlayers)
        {
            StartGameCountdown();
        }
        else if (registeredPlayers < MAX_PLAYERS)
        {
            if (CurrentGameState == GameState.CountdownToStart)
            {
                StopGameCountdown();
            }
            CurrentGameState = GameState.WaitingForPlayers;
            EnableAllPlayersInput(false);
        }
    }

    private void StartGameCountdown()
    {
        if (isCountdownRunning) return;

        if (networkGameManager != null && networkGameManager.IsMasterClient)
        {
            isCountdownRunning = true;
            CurrentGameState = GameState.CountdownToStart;

            PlayerAvatar masterPlayerAvatar = GetMasterPlayerAvatar();
            if (masterPlayerAvatar != null)
            {
                masterPlayerAvatar.NotifyGameStateChanged(GameState.CountdownToStart);
            }

            countdownCoroutine = StartCoroutine(CountdownCoroutine());
        }
    }

    private void StopGameCountdown()
    {
        if (!isCountdownRunning) return;

        isCountdownRunning = false;
        if (countdownCoroutine != null)
        {
            StopCoroutine(countdownCoroutine);
            countdownCoroutine = null;
        }
    }

    private System.Collections.IEnumerator CountdownCoroutine()
    {
        PlayerAvatar masterPlayerAvatar = GetMasterPlayerAvatar();

        for (int i = COUNTDOWN_SECONDS; i > 0; i--)
        {
            GameEvents.TriggerCountdownUpdate(i);

            if (masterPlayerAvatar != null)
            {
                masterPlayerAvatar.NotifyCountdownUpdate(i);
            }

            yield return new WaitForSeconds(1f);
        }

        isCountdownRunning = false;
        CurrentGameState = GameState.InGame;

        GameEvents.TriggerGameStateChanged(GameState.InGame);

        if (masterPlayerAvatar != null)
        {
            masterPlayerAvatar.NotifyGameStart();
        }

        EnableAllPlayersInput(true);

        if (masterPlayerAvatar != null)
        {
            masterPlayerAvatar.NotifyEnableAllPlayersInput(true);
        }
    }

    private PlayerAvatar GetMasterPlayerAvatar()
    {
        if (playerManager != null)
        {
            foreach (var playerPair in playerManager.AllPlayers)
            {
                var player = playerPair.Value;
                if (player != null && player.HasStateAuthority)
                {
                    return player;
                }
            }
        }
        return null;
    }

    private void OnPlayerInputStateChanged(bool enabled)
    {
        EnableAllPlayersInput(enabled);
    }

    private void EnableAllPlayersInput(bool enabled)
    {
        if (playerManager != null)
        {
            playerManager.SetAllPlayersInputEnabled(enabled);
        }
    }

    public void OnPlayerLeft(int playerId)
    {
        if (playerManager != null)
        {
            playerManager.UnregisterPlayerAvatar(playerId);

            if (playerManager.PlayerCount < MAX_PLAYERS && CurrentGameState == GameState.InGame)
            {
                CurrentGameState = GameState.WaitingForPlayers;
                EnableAllPlayersInput(false);
            }
        }
    }

    private void OnPlayerScoreChanged(int playerId, int newScore)
    {
        GameEvents.TriggerPlayerScoreChanged(playerId, newScore);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F9))
        {
            if (networkGameManager != null)
            {
                networkGameManager.RequestGameEnd();
            }
            else
            {
                EndGame();
            }
        }
    }

    private void OnChangeState(GameState newState)
    {
        switch (newState)
        {
            case GameState.WaitingForPlayers:
                GameEvents.TriggerGameStateChanged(newState);
                ResetGameState();
                break;
            case GameState.InGame:
                GameEvents.TriggerGameStateChanged(newState);
                EnableAllPlayersInput(true);
                break;
            case GameState.GameOver:
                break;
        }
    }

    private void ResetGameState()
    {
        gameEnded = false;
        playersClickedForRestart.Clear();

        if (itemManager != null)
        {
            itemManager.ResetItemCount();
        }
    }

    private IEnumerator RestartGameAfterDelay()
    {
        yield return new WaitForSeconds(5.0f);

        if (playerManager != null && playerManager.PlayerCount >= MAX_PLAYERS)
        {
            CurrentGameState = GameState.InGame;
            EnableAllPlayersInput(true);
        }
        else
        {
            CurrentGameState = GameState.WaitingForPlayers;
        }
    }

    void OnDestroy()
    {
        if (networkGameManager != null)
        {
            networkGameManager.OnClientJoined -= OnClientJoined;
            networkGameManager.OnPlayerSpawned -= OnPlayerSpawned;
            networkGameManager.OnPlayerLeft -= OnPlayerLeft;
            networkGameManager.OnGameEndRequested -= OnGameEndRequested;
        }

        if (playerManager != null)
        {
            playerManager.OnPlayerRegistered -= OnPlayerRegistered;
            playerManager.OnPlayerUnregistered -= OnPlayerUnregistered;
            playerManager.OnPlayerScoreChanged -= OnPlayerScoreChanged;
            playerManager.OnPlayerCountChanged -= OnPlayerCountChanged;
        }

        if (gameRuleProcessor != null)
        {
            gameRuleProcessor.OnGameEndTriggered -= EndGame;
        }

        GameEvents.OnGameRestartRequested -= RestartGame;
        GameEvents.OnGameRestartExecution -= ExecuteRestart;
        GameEvents.OnPlayerClickedForRestart -= OnPlayerClickedForRestart;
        GameEvents.OnPlayerInputStateChanged -= OnPlayerInputStateChanged;
    }
}
