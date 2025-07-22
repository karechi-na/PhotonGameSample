using System;

public static class GameEvents
{
    // ゲーム状態変更イベント
    public static event Action<GameState> OnGameStateChanged;
    
    // プレイヤースコア変更イベント
    public static event Action<int, int> OnPlayerScoreChanged; // (playerId, newScore)
    
    // 勝者決定イベント
    public static event Action<string> OnWinnerDetermined; // (winnerMessage)
    
    // プレイヤー数変更イベント
    public static event Action<int> OnPlayerCountChanged; // (playerCount)
    
    // ゲーム終了イベント
    public static event Action OnGameEnd; // ゲーム終了時

    // イベント発火メソッド
    public static void TriggerGameStateChanged(GameState newState)
    {
        OnGameStateChanged?.Invoke(newState);
    }

    public static void TriggerPlayerScoreChanged(int playerId, int newScore)
    {
        OnPlayerScoreChanged?.Invoke(playerId, newScore);
    }

    public static void TriggerWinnerDetermined(string message)
    {
        OnWinnerDetermined?.Invoke(message);
    }

    public static void TriggerPlayerCountChanged(int playerCount)
    {
        OnPlayerCountChanged?.Invoke(playerCount);
    }

    public static void TriggerGameEnd()
    {
        OnGameEnd?.Invoke();
    }
}
