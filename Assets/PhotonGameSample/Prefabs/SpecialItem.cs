using UnityEngine;

public class SpecialItem : Item
{
    [SerializeField] private int specialValue = 10;

    public override int itemValue => specialValue;
    

}
