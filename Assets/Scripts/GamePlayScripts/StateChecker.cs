using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public class StateChecker : MonoBehaviour
{
    /****   Game Pad   ****/
    public InputDevice p1Device;
    public InputDevice p2Device;
    public Gamepad p1DevicePad;
    public Gamepad p2DevicePad;


    /****   Game Objects    ****/
    public Image winPanel;
    public Image deathPanel;

    public bool matchP1Won = false;
    public bool matchP2Won = false;
    public bool matchDraw = false;

    public bool HasConfirmedp1 = false;
    public bool HasConfirmedp2 = false;

    public GameObject Wpanel;
    public GameObject Dpanel;
    public GameObject Check1;
    public GameObject Check2;

    public GameObject SelectedChar1;
    public GameObject SelectedChar2;
    public GameObject SelectedChar1Neg;
    public GameObject SelectedChar2Neg;
    public GameObject SelectedChar1Pos;
    public GameObject SelectedChar2Pos;
    public GameObject SelectedWep1;
    public GameObject SelectedWep2;


    GameObject P1;
    GameObject P2;
    Character p1Character;
    Character p2Character;

    public float Timeleft = 10.0f;


    public void Start()
    {
        Check1.SetActive(false);
        Check2.SetActive(false);
        Wpanel.SetActive(false);
        Dpanel.SetActive(false);

        GameInput.RefreshGamepads();

        if (!GameInput.HasPrimaryInput)
        {
            Debug.LogWarning("No controller or keyboard found for Player 1.");
            return;
        }

        p1Device = GameInput.GetDevice(1);
        p2Device = GameInput.GetDevice(2);
        p1DevicePad = GameInput.P1Pad;
        p2DevicePad = GameInput.P2Pad;
    }


    void Update()
    {
        

        if (LevelSelect.canvas.activeSelf == false)
        {
            RefreshPlayers();

            if (p1Character == null || p2Character == null)
            {
                return;
            }

            if (p1Character.playerHealth <= 0 && p2Character.playerHealth > 0)
            {
                matchP2Won = true;
                winPanel.sprite = p1Character.characterDeathPanel;  
                deathPanel.sprite = p2Character.characterWinPanel;
                Timeleft -= Time.deltaTime;
                if (Timeleft < 0)
                {
                    Check1.SetActive(true);
                    Check2.SetActive(true);
                    Wpanel.SetActive(true);
                    Dpanel.SetActive(true);
                }
            }

            if (p1Character.playerHealth > 0 && p2Character.playerHealth <= 0)
            {
                matchP1Won = true;
                winPanel.sprite = p1Character.characterWinPanel;
                deathPanel.sprite = p2Character.characterDeathPanel;
                Timeleft -= Time.deltaTime;
                if (Timeleft < 0)
                {
                    Check1.SetActive(true);
                    Check2.SetActive(true);
                    Wpanel.SetActive(true);
                    Dpanel.SetActive(true);
                }
            }

            if (p1Character.playerHealth <= 0 && p2Character.playerHealth <= 0)
            {
                matchDraw = true;
                winPanel.sprite = p1Character.characterDeathPanel;
                deathPanel.sprite = p2Character.characterDeathPanel;   
                Timeleft -= Time.deltaTime;
                if(Timeleft < 0)
                {
                    Check1.SetActive(true);
                    Check2.SetActive(true);
                    Wpanel.SetActive(true);
                    Dpanel.SetActive(true);
                }
                
            }
            if (Check1.activeSelf == true && Check2.activeSelf == true)
            {
                if (GameInput.IsSoloMode)
                {
                    HasConfirmedp2 = true;
                    Dpanel.SetActive(false);
                }

                if (GameInput.Confirm(1))
                {
                    Wpanel.SetActive(false);
                    Timeleft = 100f;
                    HasConfirmedp1 = true;
                   
                }
                if (GameInput.Confirm(2))
                {
                    Dpanel.SetActive(false);
                    Timeleft = 100f;
                    HasConfirmedp2 = true;
                   
                }
                if (HasConfirmedp1 && HasConfirmedp2)
                {
                    ConfirmSelection();
                }
            }

        }

    }

    private void RefreshPlayers()
    {
        if (p1Character != null && p2Character != null)
        {
            return;
        }

        // Ask the spawner which objects are the fighters. (Searching by tag can return a
        // weapon instead: weapon prefabs carry the P1/P2 tags too.)
        TPSpawn spawner = FindAnyObjectByType<TPSpawn>();
        if (spawner == null)
        {
            return;
        }

        if (p1Character == null && spawner.SpawnedP1 != null)
        {
            P1 = spawner.SpawnedP1;
            p1Character = P1.GetComponent<Character>();
        }

        if (p2Character == null && spawner.SpawnedP2 != null)
        {
            P2 = spawner.SpawnedP2;
            p2Character = P2.GetComponent<Character>();
        }
    }
    
    public void ConfirmSelection()
    {
        // Clear the match by reference rather than by name: names change as bodies get
        // sliced, and two fighters can share a weapon. Everything is switched off before
        // anything is destroyed, so a swing still in progress this frame can't cut a new
        // piece after the cleanup.
        DestroyFighter(P1);
        DestroyFighter(P2);
        Sliceable.DestroyAllPieces();
        // Every cut builds new meshes; free the ones nothing uses any more.
        Resources.UnloadUnusedAssets();
        P1 = null;
        P2 = null;
        p1Character = null;
        p2Character = null;

        Check1.SetActive(false);
        Check2.SetActive(false);
        LevelSelect.Camera.SetActive(true);
        LevelSelect.canvas.SetActive(true);
        LevelSelect.CharacterSelect1.SetActive(false);
        LevelSelect.CharacterSelect2.SetActive(false);
        LevelSelect.WeaponSelect1.SetActive(false);
        LevelSelect.WeaponSelect2.SetActive(false);
        LevelSelect.This.SetActive(true);
        HasConfirmedp1 = false;
        HasConfirmedp2 = false;
        Destroy(LevelSelect.LevelSpawned);


    }

    private static void DestroyFighter(GameObject fighter)
    {
        if (fighter == null)
        {
            return;
        }

        // Weapons follow the hands but aren't children of the fighter, so switch them off and
        // remove them separately.
        CharacterControl fighterControl = fighter.GetComponent<CharacterControl>();
        if (fighterControl != null)
        {
            foreach (GameObject weapon in new[] { fighterControl.Wep, fighterControl.WepOff })
            {
                if (weapon != null)
                {
                    weapon.SetActive(false);
                    Destroy(weapon);
                }
            }
        }

        fighter.SetActive(false);
        Destroy(fighter);
    }
}
