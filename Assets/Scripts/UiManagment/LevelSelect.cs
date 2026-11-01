using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
public class LevelSelect : MonoBehaviour
{
    protected int selectedLevelIndex;
    protected int SelectedMusicIndex;
    private Color desiredColor;

    public GameObject SpawnPoint;

    public static GameObject canvas;
    public static GameObject CharacterSelect1;
    public static GameObject CharacterSelect2;
    public static GameObject WeaponSelect1;
    public static GameObject WeaponSelect2;
    public static GameObject This;
    public static GameObject Camera;
    public static GameObject LevelSpawned;
    
    AudioSource AS;
    AudioSource PO;
    public GameObject managerob;
    public GameObject DynamicCamera;
    
    public GameObject MainMenuScreen;
    public GameObject MainMenuCanvas;

    public InputDevice p1Device;
    public InputDevice p2Device;
    public Gamepad p1DevicePad;
    public Gamepad p2DevicePad;

    private bool HasSelectedLevel = false;

    [Header("List of Music")]
    [SerializeField] protected List<AudioClip> MusicList = new List<AudioClip>();
    [Header("List of Levels")]
    [SerializeField] protected List<LevelSelectObject> LevelList = new List<LevelSelectObject>();

    [Header("UI References")]
    [SerializeField] protected TextMeshProUGUI LevelName;
    [SerializeField] protected Image LevelSplash;
    [SerializeField] protected Image backgroundColor;


    [Header("Options")]
    [SerializeField] protected float backgroundColorTransitionSpeed = 5f;


   
    public void Start()
    {
        Camera = GameObject.Find("Main Camera");
        canvas = GameObject.Find("SelectionMenus");
        This = GameObject.Find("StageSelectPanel");
        CharacterSelect1 = GameObject.Find("Player#1CharacterSelectPanel");
        CharacterSelect2 = GameObject.Find("Player#2CharacterSelectPanel");
        WeaponSelect1 = GameObject.Find("Player#1WeaponSelectPanel");
        WeaponSelect2 = GameObject.Find("Player#2WeaponSelectPanel");
        CharacterSelect1.SetActive(false);
        CharacterSelect2.SetActive(false);
        WeaponSelect1.SetActive(false);
        WeaponSelect2.SetActive(false);
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
        UpdateLevelSelectionUI();
    }




    public void Update()
    {
        if (this.gameObject.tag == "LevelSelection")
        {
            backgroundColor.color = Color.Lerp(backgroundColor.color, desiredColor, Time.deltaTime * backgroundColorTransitionSpeed);
        }
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

        if (GameInput.Cancel(1))
        {
            gameObject.SetActive(false);
            canvas.SetActive(false);
            MainMenuScreen.SetActive(true);
            MainMenuCanvas.SetActive(true);
        }
        if (GameInput.LeftBumper(1))
        {
            LeftBumper();
        }
        if (GameInput.RightBumper(1))
        {
            RightBumper();
        }

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

        if (GameInput.Cancel(2))
        {
            gameObject.SetActive(false);
            canvas.SetActive(false);
            MainMenuScreen.SetActive(true);
            MainMenuCanvas.SetActive(true);
        }
        if (GameInput.LeftBumper(2))
        {
            LeftBumper();
        }
        if (GameInput.RightBumper(2))
        {
            RightBumper();
        }

    }



    public void ConfirmSelection()
    {
        LevelSpawned = Instantiate(LevelList[selectedLevelIndex].selectedLevel, SpawnPoint.transform.position, SpawnPoint.transform.rotation);
        HasSelectedLevel = true;
        managerob = GameObject.Find("PlayerManager");
        DynamicCamera = GameObject.Find("CameraManager");
        managerob.SetActive(false);
        DynamicCamera.SetActive(false);
        This.SetActive(false);
        CharacterSelect1.SetActive(true);
        CharacterSelect2.SetActive(true);

    }
    public void LeftBumper()
    {
        SelectedMusicIndex--;
        if (SelectedMusicIndex < 0)
        {
            SelectedMusicIndex = MusicList.Count - 1;
        }
        AS = canvas.GetComponent<AudioSource>();
        AS.clip = MusicList[SelectedMusicIndex];
    }
    public void RightBumper()
    {
        SelectedMusicIndex++;
        if (SelectedMusicIndex == MusicList.Count)
        {
            SelectedMusicIndex = 0;
        }
        AS = canvas.GetComponent<AudioSource>();
        AS.clip = MusicList[SelectedMusicIndex];
    }
    public void LeftArrow()
    {
        selectedLevelIndex--;
        if (selectedLevelIndex < 0)
        {
            selectedLevelIndex = LevelList.Count - 1;
        }
        UpdateLevelSelectionUI();

    }
    public void RightArrow()
    {
        selectedLevelIndex++;
        if (selectedLevelIndex == LevelList.Count)
        {
            selectedLevelIndex = 0;
        }
        UpdateLevelSelectionUI();
    }
    private void UpdateLevelSelectionUI()
    {
        LevelSplash.sprite = LevelList[selectedLevelIndex].splash;
        LevelName.text = LevelList[selectedLevelIndex].LevelName;
        desiredColor = LevelList[selectedLevelIndex].LevelColor;
    }

    [System.Serializable]
    public class LevelSelectObject
    {
        public Sprite splash;
        public string LevelName;
        public Color LevelColor;
        public GameObject selectedLevel;
        
    }
}
