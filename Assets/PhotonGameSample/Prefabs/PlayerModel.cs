using UnityEngine;
using Fusion;
using System;
using UnityEngine.SocialPlatforms.Impl;


public class PlayerModel : NetworkBehaviour
{
    [Networked, OnChangedRender(nameof(OnScoreChangedNetworked))]
    [SerializeField]public int score { get; set; }
    public event Action<int> OnScoreChanged;

    public void OnScoreChangedNetworked()
    {
        OnScoreChanged?.Invoke(score);
    }
}

