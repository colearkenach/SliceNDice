using UnityEngine;

// A weapon in a fighter's hand. It isn't parented to the hand bone: it copies the hand's
// position and rotation every frame instead, so it never inherits the bone's (animated,
// sometimes uneven) scale, which used to skew broad weapons like the cleaver.
public class HeldWeapon : MonoBehaviour
{
    public Transform grip;          // The hand point it's held at.

    Vector3 offsetPosition;         // Pose relative to the grip, in the grip's rotation frame (world units).
    Quaternion offsetRotation = Quaternion.identity;

    // Remembers where the weapon sits relative to the grip right now.
    public void Attach(Transform gripPoint)
    {
        grip = gripPoint;
        Quaternion inverse = Quaternion.Inverse(grip.rotation);
        offsetPosition = inverse * (transform.position - grip.position);
        offsetRotation = inverse * transform.rotation;
    }

    public void FollowGrip()
    {
        if (grip != null)
        {
            transform.SetPositionAndRotation(grip.position + grip.rotation * offsetPosition, grip.rotation * offsetRotation);
        }
    }

    public Vector3 GripPosition => grip != null ? grip.position : transform.position;

    // Lets go of the weapon: it becomes a loose physics prop that can't hit or cut anything.
    public static void Drop(GameObject weaponObject, Vector3 velocity, GameObject fighter)
    {
        if (weaponObject == null)
        {
            return;
        }

        foreach (Weapon weapon in weaponObject.GetComponentsInChildren<Weapon>(true))
        {
            DestroyImmediate(weapon);
        }

        HeldWeapon held = weaponObject.GetComponent<HeldWeapon>();
        if (held != null)
        {
            DestroyImmediate(held);
        }

        foreach (Collider c in weaponObject.GetComponentsInChildren<Collider>(true))
        {
            c.isTrigger = false;
            if (c is MeshCollider mesh)
            {
                mesh.convex = true;     // Required on a moving body.
            }
        }

        Rigidbody body = weaponObject.GetComponent<Rigidbody>();
        if (body == null)
        {
            body = weaponObject.AddComponent<Rigidbody>();
        }

        body.isKinematic = false;
        body.useGravity = true;
        body.linearVelocity = velocity;
        body.angularVelocity = Random.insideUnitSphere * 6f;

        if (fighter != null)
        {
            Sliceable.IgnoreMovementCapsule(fighter, weaponObject);
        }

        Sliceable.RegisterPiece(weaponObject);
    }
}
