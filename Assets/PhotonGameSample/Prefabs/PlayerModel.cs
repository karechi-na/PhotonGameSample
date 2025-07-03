using UnityEngine;

[System.Serializable]
public class PlayerModel
{
    public readonly string playerName;
    public readonly GameObject PlayerGameObject;
    private int _score = 0;
    public int score
    {
        get
        {
            return _score;
        }
        set
        {
            if (value != _score)
            {
                _score = value;
                if (OnScoreChanged != null)
                {
                    OnScoreChanged?.Invoke(_score);
                }
            }
        }
    }
    public event System.Action<int> OnScoreChanged;
    public PlayerModel(string playerName, GameObject playerGameObject)
    {
        this.playerName = playerName;
        PlayerGameObject = playerGameObject;
        this._score = 0;
    }
}

