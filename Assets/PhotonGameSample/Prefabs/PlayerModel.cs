using UnityEngine;
using TMPro;
using Fusion;
using System;
using UnityEngine.SocialPlatforms.Impl;


public class PlayerModel : NetworkBehaviour
{
    [Networked, OnChangedRender(nameof(OnScoreChangedNetworked))]
    [SerializeField]public int score { get; set; }
    public TextMeshProUGUI scoreText; // Reference to the UI Text component to display the score
    public event Action<int> OnScoreChanged;

    public void OnScoreChangedNetworked()
    {
        OnScoreChanged?.Invoke(score);
    }
}

