using System;
using System.Collections.Generic;
using UnityEngine;

// フェーズ0: サービス化リファクタ前の挙動ベースライン計測用コンポーネント
// シーンに 1 つ配置し GameEvents を購読、再スタートサイクルの重要タイミングをログ出力。
namespace PhotonGameSample.Infrastructure
{
    [DefaultExecutionOrder(-500)]
    public class ServiceRefactorBaselineLogger : MonoBehaviour
    {
        private readonly Dictionary<GameState, DateTime> _stateTimes = new();
        private readonly List<(int playerId, DateTime time)> _restartClicks = new();
        private DateTime _lastGameEnd;
        private int _lastCountdownValue = -1;
        private bool _subscribed;
        private int _restartCycleIndex = 0;

        private void Awake() => TrySubscribe();
        private void OnEnable() => TrySubscribe();
        private void OnDisable() => Unsubscribe();
        private void OnDestroy() => Unsubscribe();

        private void TrySubscribe()
        {
            if (_subscribed) return;
            GameEvents.AddGameStateChangedListener(OnGameStateChanged);
            GameEvents.OnPlayerClickedForRestart += OnPlayerClickedForRestart;
            GameEvents.OnCountdownUpdate += OnCountdownUpdate;
            GameEvents.OnGameRestartExecution += OnGameRestartExecution;
            GameEvents.OnGameEnd += OnGameEnd;
            _subscribed = true;
            Debug.Log("[BaselineLogger] Subscribed GameEvents");
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            GameEvents.RemoveGameStateChangedListener(OnGameStateChanged);
            GameEvents.OnPlayerClickedForRestart -= OnPlayerClickedForRestart;
            GameEvents.OnCountdownUpdate -= OnCountdownUpdate;
            GameEvents.OnGameRestartExecution -= OnGameRestartExecution;
            GameEvents.OnGameEnd -= OnGameEnd;
            _subscribed = false;
            Debug.Log("[BaselineLogger] Unsubscribed GameEvents");
        }

        private void OnGameStateChanged(GameState state)
        {
            _stateTimes[state] = DateTime.UtcNow;
            Debug.Log($"[BaselineLogger] StateChanged -> {state} (cycle={_restartCycleIndex})");

            if (state == GameState.WaitingForRestart)
            {
                _restartClicks.Clear();
                _lastCountdownValue = -1;
            }
        }

        private void OnPlayerClickedForRestart(int playerId)
        {
            _restartClicks.Add((playerId, DateTime.UtcNow));
            Debug.Log($"[BaselineLogger] RestartClick player={playerId} count={_restartClicks.Count}");
        }

        private void OnCountdownUpdate(int remaining)
        {
            if (_lastCountdownValue != -1 && remaining > _lastCountdownValue)
            {
                Debug.LogWarning($"[BaselineLogger] Countdown value increased (prev={_lastCountdownValue}, now={remaining})");
            }
            _lastCountdownValue = remaining;
        }

        private void OnGameEnd()
        {
            _lastGameEnd = DateTime.UtcNow;
            Debug.Log("[BaselineLogger] GameEnd captured");
        }

        private void OnGameRestartExecution()
        {
            _restartCycleIndex++;
            _stateTimes.TryGetValue(GameState.WaitingForRestart, out var waitTs);
            _stateTimes.TryGetValue(GameState.InGame, out var inGameTs);
            var now = DateTime.UtcNow;
            var clicksInfo = string.Join(", ", _restartClicks.ConvertAll(c => $"p{c.playerId}:{(c.time - waitTs).TotalMilliseconds:F0}ms"));
            double endToWaitMs = (_lastGameEnd != default && waitTs != default) ? (waitTs - _lastGameEnd).TotalMilliseconds : -1;
            double waitToInGameMs = (waitTs != default && inGameTs != default) ? (inGameTs - waitTs).TotalMilliseconds : -1;
            Debug.Log($"[BaselineLogger][Cycle {_restartCycleIndex}] Summary clicks=[{clicksInfo}] end->wait={endToWaitMs}ms wait->inGame={waitToInGameMs}ms timestamp={now:O}");
        }
    }
}
