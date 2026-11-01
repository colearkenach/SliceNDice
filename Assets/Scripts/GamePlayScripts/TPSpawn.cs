using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;


public class TPSpawn : MonoBehaviour
{
    private const RigidbodyConstraints LiveMovementConstraints = RigidbodyConstraints.FreezeRotation;

    // Which player slot is being spawned right now (0 when not spawning). The fighter's
    // Awake reads it, since some prefabs were saved without the right P1/P2 box ticked.
    public static int SpawningSlot { get; private set; }

    public GameObject P1;
    public GameObject P2;
    public PlayerInputManager manager;
    public InputDevice p1Device;
    public InputDevice p2Device;
    public Gamepad p1DevicePad;
    public Gamepad p2DevicePad;
    public CameraControl control;
    public bool usingAiForP2;

    [Header("Spawn (used when the level has no StageBounds)")]
    [SerializeField] Vector3 p1SpawnPosition = new Vector3(-7f, 2f, -2f);
    [SerializeField] Vector3 p2SpawnPosition = new Vector3(0f, 2f, -2f);
    [SerializeField] Vector3 p1SpawnRotation = new Vector3(0f, 90f, 0f);
    [SerializeField] Vector3 p2SpawnRotation = new Vector3(0f, -90f, 0f);

    public const int NOSPLITSCREEN = -1;
    public const string NOCONTROLSCHEME = null;

    public GameObject SpawnedP1 { get; private set; }
    public GameObject SpawnedP2 { get; private set; }

    void Start()
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
        usingAiForP2 = p2DevicePad == null;

        StageBounds stage = GetComponentInParent<StageBounds>();
        if (stage == null)
        {
            stage = FindAnyObjectByType<StageBounds>();
        }

        Vector3 p1Position = stage != null ? stage.P1SpawnWorld : p1SpawnPosition;
        Vector3 p2Position = stage != null ? stage.P2SpawnWorld : p2SpawnPosition;

        GameObject p1Object;
        GameObject p2Object;

        SpawningSlot = 1;
        try
        {
            manager.playerPrefab = P1.gameObject;
            PlayerInput p1Input = manager.JoinPlayer(0, NOSPLITSCREEN, NOCONTROLSCHEME, p1Device);
            p1Object = p1Input != null ? p1Input.gameObject : GameObject.Find(P1.name + "(Clone)");
        }
        finally
        {
            SpawningSlot = 0;
        }

        SpawningSlot = 2;
        try
        {
            if (usingAiForP2)
            {
                p2Object = Instantiate(P2.gameObject);
                PlayerInput playerInput = p2Object.GetComponent<PlayerInput>();
                if (playerInput != null)
                {
                    playerInput.enabled = false;
                }
            }
            else
            {
                manager.playerPrefab = P2.gameObject;
                PlayerInput p2Input = manager.JoinPlayer(1, NOSPLITSCREEN, NOCONTROLSCHEME, p2Device);
                p2Object = p2Input != null ? p2Input.gameObject : GameObject.Find(P2.name + "(Clone)");
            }
        }
        finally
        {
            SpawningSlot = 0;
        }

        PlacePlayerOnStage(p1Object, p1Position, Quaternion.Euler(p1SpawnRotation), "P1");
        PlacePlayerOnStage(p2Object, p2Position, Quaternion.Euler(p2SpawnRotation), "P2");

        SpawnedP1 = p1Object;
        SpawnedP2 = p2Object;

        control = GameObject.Find("CameraManager").GetComponent<CameraControl>();

        control.Player1 = p1Object;
        control.Player2 = p2Object;

    }

    private void PlacePlayerOnStage(GameObject player, Vector3 spawnPosition, Quaternion rotation, string tag)
    {
        if (player == null)
        {
            return;
        }

        // Some character prefabs are untagged; the match logic finds fighters by tag.
        player.tag = tag;
        player.transform.SetPositionAndRotation(spawnPosition, rotation);
        ResetLivePhysics(player);
        Physics.SyncTransforms();
    }

    private void ResetLivePhysics(GameObject player)
    {
        Rigidbody rootBody = player.GetComponent<Rigidbody>();
        if (rootBody != null)
        {
            rootBody.isKinematic = false;
            rootBody.useGravity = true;
            rootBody.constraints = LiveMovementConstraints;
            rootBody.linearVelocity = Vector3.zero;
            rootBody.angularVelocity = Vector3.zero;
        }

        Rigidbody[] childBodies = player.GetComponentsInChildren<Rigidbody>();
        for (int i = 0; i < childBodies.Length; i++)
        {
            if (childBodies[i] == rootBody)
            {
                continue;
            }

            // Bones are driven by the Animator while the fighter is alive.
            childBodies[i].isKinematic = true;
        }
    }
}
