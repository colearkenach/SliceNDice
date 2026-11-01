using System.Collections.Generic;
using UnityEngine;

// Per-level layout fixes that sit on a level prefab's root:
//  - where each fighter spawns (read by TPSpawn),
//  - extra invisible blocker boxes that smooth over snaggy props or plug gaps in the walls.
// All positions are in metres relative to the level's pivot (the level's own scale is ignored),
// so they line up with world coordinates when the level is spawned at the origin.
public class StageBounds : MonoBehaviour
{
    [System.Serializable]
    public class Blocker
    {
        public string name = "Blocker";
        public Vector3 center;
        public Vector3 size = Vector3.one;
    }

    [Header("Spawns")]
    public Vector3 p1Spawn = new Vector3(-7f, 2f, -2f);
    public Vector3 p2Spawn = new Vector3(0f, 2f, -2f);

    [Header("Invisible blockers")]
    public List<Blocker> blockers = new List<Blocker>();

    [Header("Camera")]
    [Tooltip("Keep the camera inside this box (e.g. the open front of an enclosed stage) so walls never come between it and the fighters.")]
    public bool limitCamera;
    public Vector3 cameraCenter;
    public Vector3 cameraSize = new Vector3(10f, 10f, 10f);

    public Vector3 P1SpawnWorld => ToWorld(p1Spawn);
    public Vector3 P2SpawnWorld => ToWorld(p2Spawn);

    // Moves a world position into the camera box (no change if the stage doesn't limit the camera).
    public Vector3 ClampCamera(Vector3 worldPosition)
    {
        if (!limitCamera)
        {
            return worldPosition;
        }

        Quaternion rotation = transform.rotation;
        Vector3 local = Quaternion.Inverse(rotation) * (worldPosition - transform.position);
        Vector3 half = cameraSize * 0.5f;
        local = new Vector3(
            Mathf.Clamp(local.x, cameraCenter.x - half.x, cameraCenter.x + half.x),
            Mathf.Clamp(local.y, cameraCenter.y - half.y, cameraCenter.y + half.y),
            Mathf.Clamp(local.z, cameraCenter.z - half.z, cameraCenter.z + half.z));
        return transform.position + rotation * local;
    }

    void Awake()
    {
        for (int i = 0; i < blockers.Count; i++)
        {
            CreateBlocker(blockers[i]);
        }
    }

    void CreateBlocker(Blocker blocker)
    {
        GameObject go = new GameObject(blocker.name);
        go.layer = gameObject.layer;
        go.isStatic = true;

        // Kept outside the level's (non-uniformly scaled) hierarchy so the box stays a true box,
        // and cleaned up with the level by OnDestroy below.
        go.transform.SetPositionAndRotation(ToWorld(blocker.center), transform.rotation);
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.size = blocker.size;

        spawned.Add(go);
    }

    readonly List<GameObject> spawned = new List<GameObject>();

    void OnDestroy()
    {
        for (int i = 0; i < spawned.Count; i++)
        {
            if (spawned[i] != null)
            {
                Destroy(spawned[i]);
            }
        }
    }

    Vector3 ToWorld(Vector3 levelPoint)
    {
        return transform.position + transform.rotation * levelPoint;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.35f);
        Matrix4x4 previous = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        for (int i = 0; i < blockers.Count; i++)
        {
            Gizmos.DrawCube(blockers[i].center, blockers[i].size);
        }
        if (limitCamera)
        {
            Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.6f);
            Gizmos.DrawWireCube(cameraCenter, cameraSize);
        }
        Gizmos.matrix = previous;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(P1SpawnWorld, 0.4f);
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(P2SpawnWorld, 0.4f);
    }
}
