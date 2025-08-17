using System;
using TMPro;
using UnityEngine;

public static class GameEvents
{
    static public TextMeshProUGUI TraceWindow; // デバッグ用トレースウィンドウ

    // ゲーム状態変更イベント
    public static event Action<GameState> OnGameStateChanged;

    // プレイヤースコア変更イベント
    public static event Action<int, int> OnPlayerScoreChanged; // (playerId, newScore)

    // 勝者決定イベント
    public static event Action<string> OnWinnerDetermined; // (winnerMessage)

    // プレイヤー数変更イベント
    public static event Action<int> OnPlayerCountChanged; // (playerCount)

    // プレイヤー登録イベント
    public static event Action<int> OnPlayerRegistered; // (playerId)

    // ゲーム終了イベント
    public static event Action OnGameEnd; // ゲーム終了時

    // スコア更新完了イベント
    public static event Action<int, int> OnScoreUpdateCompleted; // (playerId, newScore)

    // カウントダウンイベント
    public static event Action<int> OnCountdownUpdate; // (remainingSeconds)

    // ゲーム再開要求イベント
    public static event Action OnGameRestartRequested;

    // ゲーム再開実行イベント（RPC経由）
    public static event Action OnGameRestartExecution;

    // プレイヤーがクリックしたイベント（両方のクライアントでクリックを待つために使用）
    public static event Action<int> OnPlayerClickedForRestart; // (playerId)

    // プレイヤー操作状態変更イベント
    public static event Action<bool> OnPlayerInputStateChanged; // (enabled)

    // アイテムリセットイベント（RPC経由）
    public static event Action OnItemsReset;

    // アイテムシーン再ロード完了イベント（シーン再ロード型リスタート用）
    public static event Action OnItemsSceneReloaded;
    // ハードリセット要求（全ランタイム破棄→再起動）
    public static event Action OnHardResetRequested;
    // ハードリセット直前フック（クリーンアップ用）
    public static event Action OnHardResetPreCleanup;

    // --- イベント登録/解除用ラッパー ---
    public static void AddGameStateChangedListener(Action<GameState> listener)
    {
        OnGameStateChanged += listener;
        AppendTrace($"[Event登録] OnGameStateChanged += {listener.Method.Name}");
    }

    public static void RemoveGameStateChangedListener(Action<GameState> listener)
    {
        OnGameStateChanged -= listener;
        AppendTrace($"[Event解除] OnGameStateChanged -= {listener.Method.Name}");
    }

    // --- イベント発火メソッド ---
    public static void TriggerGameStateChanged(GameState newState)
    {
        AppendTrace($"[Event発火] OnGameStateChanged({newState})");
        OnGameStateChanged?.Invoke(newState);
    }

    public static void TriggerPlayerScoreChanged(int playerId, int newScore)
    {
        AppendTrace($"[Event発火] OnPlayerScoreChanged(playerId={playerId}, newScore={newScore})");
        OnPlayerScoreChanged?.Invoke(playerId, newScore);
    }

    public static void TriggerWinnerDetermined(string message)
    {
        AppendTrace($"[Event発火] OnWinnerDetermined(message={message})");
        OnWinnerDetermined?.Invoke(message);
    }

    public static void TriggerPlayerCountChanged(int playerCount)
    {
        AppendTrace($"[Event発火] OnPlayerCountChanged(playerCount={playerCount})");
        OnPlayerCountChanged?.Invoke(playerCount);
    }

    public static void TriggerPlayerRegistered(int playerId)
    {
        AppendTrace($"[Event発火] OnPlayerRegistered(playerId={playerId})");
        OnPlayerRegistered?.Invoke(playerId);
    }

    public static void TriggerGameEnd()
    {
        AppendTrace("[Event発火] OnGameEnd()");
        OnGameEnd?.Invoke();
    }

    public static void TriggerScoreUpdateCompleted(int playerId, int newScore)
    {
        AppendTrace($"[Event発火] OnScoreUpdateCompleted(playerId={playerId}, newScore={newScore})");
        OnScoreUpdateCompleted?.Invoke(playerId, newScore);
    }

    public static void TriggerCountdownUpdate(int remainingSeconds)
    {
        AppendTrace($"[Event発火] OnCountdownUpdate(remainingSeconds={remainingSeconds})");
        OnCountdownUpdate?.Invoke(remainingSeconds);
    }

    public static void TriggerGameRestartRequested()
    {
        AppendTrace("[Event発火] OnGameRestartRequested()");
        OnGameRestartRequested?.Invoke();
    }

    public static void TriggerGameRestartExecution()
    {
        AppendTrace("[Event発火] OnGameRestartExecution()");
        UnityEngine.Debug.Log("GameEvents: ゲーム再開実行をトリガーします");
        OnGameRestartExecution?.Invoke();
        UnityEngine.Debug.Log("GameEvents: OnGameRestartExecution event fired");
    }

    public static void TriggerPlayerClickedForRestart(int playerId)
    {
        AppendTrace($"[Event発火] OnPlayerClickedForRestart(playerId={playerId})");
        OnPlayerClickedForRestart?.Invoke(playerId);
    }

    public static void TriggerPlayerInputStateChanged(bool enabled)
    {
        AppendTrace($"[Event発火] OnPlayerInputStateChanged(enabled={enabled})");
        OnPlayerInputStateChanged?.Invoke(enabled);
    }

    public static void TriggerItemsReset()
    {
        AppendTrace("[Event発火] OnItemsReset()");
        OnItemsReset?.Invoke();
    }

    public static void TriggerItemsSceneReloaded()
    {
        AppendTrace("[Event発火] OnItemsSceneReloaded()");
        OnItemsSceneReloaded?.Invoke();
    }

    public static void TriggerHardResetRequested()
    {
        AppendTrace("[Event発火] OnHardResetRequested()");
        OnHardResetRequested?.Invoke();
    }

    public static void TriggerHardResetPreCleanup()
    {
        AppendTrace("[Event発火] OnHardResetPreCleanup()");
        OnHardResetPreCleanup?.Invoke();
    }

    /// <summary>
    /// すべてのイベントハンドラをクリア（ハードリセット時用）
    /// </summary>
    public static void ClearAllHandlers()
    {
        OnGameStateChanged = null;
        OnPlayerScoreChanged = null;
        OnWinnerDetermined = null;
        OnPlayerCountChanged = null;
        OnPlayerRegistered = null;
        OnGameEnd = null;
        OnScoreUpdateCompleted = null;
        OnCountdownUpdate = null;
        OnGameRestartRequested = null;
        OnGameRestartExecution = null;
        OnPlayerClickedForRestart = null;
        OnPlayerInputStateChanged = null;
        OnItemsReset = null;
        OnItemsSceneReloaded = null;
        OnHardResetRequested = null;
        OnHardResetPreCleanup = null;
        AppendTrace("[Event] Cleared all handlers");
    }

    // --- TraceWindowへの追記ユーティリティ ---
    private static void AppendTrace(string message)
    {
        if (TraceWindow != null)
        {
            TraceWindow.text += $"{DateTime.Now:HH:mm:ss.fff} {message}\n";
        }
    }
}
