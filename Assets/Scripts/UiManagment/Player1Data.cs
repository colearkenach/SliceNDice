using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Player1Data : MonoBehaviour
{
    public string playerCharacter1;
    public string PlayerCharWeapon1;
    public static bool IsPlayer1;

    // This match's picks. Set by the menus and read by the spawned fighter, so the
    // character prefab assets themselves are never edited at runtime.
    [System.NonSerialized] public GameObject weaponPrefab;
    [System.NonSerialized] public GameObject offhandPrefab;
    [System.NonSerialized] public RuntimeAnimatorController animatorController;
}
