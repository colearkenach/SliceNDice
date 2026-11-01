using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

public class SelectionScreen : MonoBehaviour
{
    protected int selectedCharacterIndex;
    private Color desiredColor;

    public static bool HasSelectedCharacter1 = false;
    public static bool HasSelectedCharacter2 = false;
    public bool P1;
    public bool P2;

    public GameObject canvas;
    public LevelSelect LS;
    public GameObject LevelSelectionObject;

    public static GameObject SelectedCharacter1;
    public static GameObject SelectedCharacter2;
    TPSpawn spawnman;

    public InputDevice p1Device;
    public InputDevice p2Device;
    public Gamepad p1DevicePad;
    public Gamepad p2DevicePad;

    [Header("List of Characters")]
    [SerializeField] public List<CharacterSelectObject> characterList = new List<CharacterSelectObject>();

    [Header("UI References")]
    [SerializeField] public TextMeshProUGUI characterName;
    [SerializeField] public Image characterSplash;
    [SerializeField] public Image backgroundColor;
   

    [Header("Options")]
    [SerializeField] protected float backgroundColorTransitionSpeed = 5f;



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
        UpdateCharacterSelectionUI();
    }




    public void Update()
    {
        if (this.gameObject.tag == "CharacterSelection")
        {
            backgroundColor.color = Color.Lerp(backgroundColor.color, desiredColor, Time.deltaTime * backgroundColorTransitionSpeed);
        }

        if (GameInput.Cancel(1))
        {
            HasSelectedCharacter1 = false;
            HasSelectedCharacter2 = false;
            Destroy(LevelSelect.LevelSpawned);
            LevelSelect.CharacterSelect1.SetActive(false);
            LevelSelect.CharacterSelect2.SetActive(false);
            LS.gameObject.SetActive(true);
        }
        if (GameInput.Cancel(2))
        {
            HasSelectedCharacter1 = false;
            HasSelectedCharacter2 = false;
            Destroy(LevelSelect.LevelSpawned);
            LevelSelect.CharacterSelect1.SetActive(false);
            LevelSelect.CharacterSelect2.SetActive(false);
            LS.gameObject.SetActive(true);
        }

        if (P1)
        {
            if (GameInput.MenuLeft(1))
            {
                LeftArrow();
            }
            if (GameInput.MenuRight(1))
            {
                RightArrow();
            }
            if (GameInput.Confirm(1))
            {
                ConfirmSelection();
            }

        }
        if (P2)
        {
            if (GameInput.MenuLeft(2))
            {
                LeftArrow();
            }
            if (GameInput.MenuRight(2))
            {
                RightArrow();
            }
            if (GameInput.Confirm(2))
            {
                ConfirmSelection();
            }

        }

    }

  

    public void ConfirmSelection()
    {
        spawnman = LS.managerob.GetComponent<TPSpawn>();
        if (P1)
        {
            spawnman.P1 = characterList[selectedCharacterIndex].selectedchar;
            SelectedCharacter1 = characterList[selectedCharacterIndex].selectedchar;
            HasSelectedCharacter1 = true;

            if (GameInput.IsSoloMode && !HasSelectedCharacter2)
            {
                SelectionScreen p2Selection = LevelSelect.CharacterSelect2.GetComponent<SelectionScreen>();
                p2Selection.ConfirmSelection();
            }
                
        }

        if (P2)
        {
            spawnman.P2 = characterList[selectedCharacterIndex].selectedchar;
            SelectedCharacter2 = characterList[selectedCharacterIndex].selectedchar;
            HasSelectedCharacter2 = true;
        }

        if (HasSelectedCharacter1 && HasSelectedCharacter2)
        {
            LevelSelect.WeaponSelect1.SetActive(true);
            LevelSelect.WeaponSelect2.SetActive(true);
            LevelSelect.CharacterSelect1.gameObject.SetActive(false);
            LevelSelect.CharacterSelect2.gameObject.SetActive(false);
            HasSelectedCharacter1 = false;
            HasSelectedCharacter2 = false;
            
        }
        

    }
    public void LeftArrow()
    {
        if (P1)
        {
            selectedCharacterIndex--;
            if (selectedCharacterIndex < 0)
            {
                selectedCharacterIndex = characterList.Count - 1;
            }

            UpdateCharacterSelectionUI();
        }
        if (P2)
        {
            selectedCharacterIndex--;
            if (selectedCharacterIndex < 0)
            {
                selectedCharacterIndex = characterList.Count - 1;
            }

            UpdateCharacterSelectionUI();
        }

    }
    public void RightArrow()
    {
        if (P1)
        {
            selectedCharacterIndex++;
            if (selectedCharacterIndex == characterList.Count)
            {
                selectedCharacterIndex = 0;
            }
            UpdateCharacterSelectionUI();
        }
        if (P2)
        {
            selectedCharacterIndex++;
            if (selectedCharacterIndex == characterList.Count)
            {
                selectedCharacterIndex = 0;
            }
            UpdateCharacterSelectionUI();
        }

    }
    private void UpdateCharacterSelectionUI()
    {
        characterSplash.sprite = characterList[selectedCharacterIndex].splash;
        characterName.text = characterList[selectedCharacterIndex].characterName;
        desiredColor = characterList[selectedCharacterIndex].characterColor;
    }

    [System.Serializable]
    public class CharacterSelectObject
    {
        public Sprite splash;
        public string characterName;
        public Color characterColor;
        public GameObject selectedchar;
      
    }
}
