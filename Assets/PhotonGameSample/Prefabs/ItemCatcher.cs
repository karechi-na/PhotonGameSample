using System;
using System.Collections;
using UnityEngine;

public class ItemCatcher : MonoBehaviour
{
    public Action<Item,PlayerAvatar> ItemCaught; // Event to notify when an item is caught
}
