using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Player2Data : MonoBehaviour
{
    public string playerCharacter2;
    public string PlayerCharWeapon2;
    public static bool IsPlayer2;

    // This match's picks. Set by the menus and read by the spawned fighter, so the
    // character prefab assets themselves are never edited at runtime.
    [System.NonSerialized] public GameObject weaponPrefab;
    [System.NonSerialized] public GameObject offhandPrefab;
    [System.NonSerialized] public RuntimeAnimatorController animatorController;
}
