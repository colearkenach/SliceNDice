using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

public class MainStartScript : MonoBehaviour
{
    private bool HasStartedGame = false;


    public InputDevice p1Device;
    public InputDevice p2Device;
    public Gamepad p1DevicePad;
    public Gamepad p2DevicePad;

    public GameObject Canvas;
    public GameObject SelectionMenus;
    public GameObject MainMenuPanel;

    public void Awake()
    {
        SelectionMenus.SetActive(false);
        MainMenuPanel.SetActive(false);
    }
    // Start is called before the first frame update
    public void Start()
    {
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

    // Update is called once per frame
    public void Update()
    {
        if (GameInput.Confirm(1))
        {
            ConfirmSelection();
        }
        if (GameInput.Confirm(2))
        {
            ConfirmSelection();
        }

    }

    public void ConfirmSelection()
    {
        if (!HasStartedGame)
        {
            gameObject.SetActive(false);
            MainMenuPanel.SetActive(true);
        }


    }
}
