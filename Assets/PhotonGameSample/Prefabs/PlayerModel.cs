using UnityEngine;
using TMPro;
using Fusion;
using System;

/// <summary>
/// プレイヤーのスコアなど、ゲーム内の状態を保持するシンプルなデータモデルクラス。
/// </summary>
public class PlayerModel
{
    private int _score;
    [SerializeField]
    public int score
    {
        get => _score;
        private set
        {
            if (_score != value)
            {
                _score = value;
                OnScoreChanged?.Invoke(_score);
            }
        }
    }
    public event Action<int> OnScoreChanged;

    public PlayerModel(int initialScore = 0)
    {
        score = initialScore;
    }

    // スコアを変更する
    public void SetScore(int newScore)
    {
        score = newScore;
    }
    // スコアを加算する
    public void AddScore(int amount)
    {
        score += amount;
    }
}

