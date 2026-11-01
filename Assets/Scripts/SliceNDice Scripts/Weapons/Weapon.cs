using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using BzKovSoft.ObjectSlicerSamples;

// A held weapon. While its owner's attack is in its hit window, the weapon traces its striking
// edge every frame and reports what it reaches first: another weapon (block or clash) or a
// fighter's body (Character decides block / wound / kill). Slashing weapons also cut dead
// bodies and severed pieces along the exact plane the edge swept through.
public class Weapon : BzKnife
{
    [Header("Weapon Controls")]
    public int weaponDamage;
    public float weaponWeight;

    [Header("Cutting Edge")]
    [Tooltip("Off: the edge is worked out from the weapon's hitbox or mesh. On: use Edge Base / Edge Tip / Blade Normal below (local space). Select the weapon to see the edge gizmo.")]
    [SerializeField] bool customEdge;
    [SerializeField] Vector3 edgeBase;
    [SerializeField] Vector3 edgeTip = Vector3.up;
    [SerializeField] Vector3 bladeNormal = Vector3.right;
    [Tooltip("Auto edge only: how far along the weapon (0 = grip end, 1 = tip) the blade starts.")]
    [SerializeField, Range(0f, 0.9f)] float bladeStart = 0.35f;
    [Tooltip("Points along the edge traced each frame. Limb bones can be only ~10 cm thick, so keep the spacing below that.")]
    [SerializeField, Range(2, 16)] int edgeSamples = 10;
    [Tooltip("If the swing direction is nearly parallel to the edge (a stab), fall back to cutting along the flat of the blade.")]
    [SerializeField, Range(0.05f, 0.9f)] float minSweepAngleSin = 0.25f;

    public Character Owner { get; set; }

    Vector3 localBase;
    Vector3 localTip;
    Vector3 localNormal;
    bool edgeReady;

    Vector3 prevBase;
    Vector3 prevTip;
    bool hasPrev;

    static int lastSyncFrame = -1;

    readonly RaycastHit[] hitBuffer = new RaycastHit[32];
    readonly HashSet<Sliceable> cutThisFrame = new HashSet<Sliceable>();
    readonly HashSet<Character> struckThisSwing = new HashSet<Character>();
    readonly List<Contact> contacts = new List<Contact>();

    public bool CanCut => weaponType == WeaponType.Slash;

    public Vector3 EdgeBaseWorld => transform.TransformPoint(localBase);
    public Vector3 EdgeTipWorld => transform.TransformPoint(localTip);

    void Awake()
    {
        BuildEdge();
    }

    void OnEnable()
    {
        hasPrev = false;
    }

    // Called by CharacterControl at the start of every attack: one swing (with one or both
    // hands) lands at most once on each fighter and can't cut the same piece twice.
    public void BeginSwing(int sliceId)
    {
        BeginNewSlice(sliceId);
        struckThisSwing.Clear();
    }

    void LateUpdate()
    {
        if (!edgeReady)
        {
            return;
        }

        Vector3 curBase = EdgeBaseWorld;
        Vector3 curTip = EdgeTipWorld;

        if (hasPrev && Owner != null && Owner.IsStriking)
        {
            SweepEdge(prevBase, prevTip, curBase, curTip);
        }

        prevBase = curBase;
        prevTip = curTip;
        hasPrev = true;
    }

    // Raycasts along the paths each point on the edge travelled this frame, plus along the
    // edge itself, then handles every contact in the order the blade reached it.
    void SweepEdge(Vector3 fromBase, Vector3 fromTip, Vector3 toBase, Vector3 toTip)
    {
        cutThisFrame.Clear();
        contacts.Clear();

        // Bring physics up to the poses being drawn this frame (auto sync is off, and the
        // fighters move between physics steps), so the rays test where the bodies really are.
        if (lastSyncFrame != Time.frameCount)
        {
            lastSyncFrame = Time.frameCount;
            Physics.SyncTransforms();
        }

        Vector3 edgeDir = (toTip - toBase).normalized;

        for (int i = 0; i < edgeSamples; i++)
        {
            float s = edgeSamples == 1 ? 0.5f : i / (float)(edgeSamples - 1);
            Vector3 from = Vector3.Lerp(fromBase, fromTip, s);
            Vector3 to = Vector3.Lerp(toBase, toTip, s);
            Vector3 travel = to - from;
            float length = travel.magnitude;

            if (length < 0.0001f)
            {
                continue;
            }

            Vector3 sweepDir = travel / length;
            int count = Physics.RaycastNonAlloc(from, sweepDir, hitBuffer, length, ~0, QueryTriggerInteraction.Collide);
            for (int h = 0; h < count; h++)
            {
                // How far through this frame's swing the blade was when it got there.
                AddContact(hitBuffer[h], hitBuffer[h].distance / length, sweepDir);
            }
        }

        // Along the edge as it is now: whatever the blade ends the frame inside.
        Vector3 edge = toTip - toBase;
        float edgeLength = edge.magnitude;
        if (edgeLength > 0.0001f)
        {
            Vector3 midSweep = (((toBase + toTip) - (fromBase + fromTip)) * 0.5f).normalized;
            int count = Physics.RaycastNonAlloc(toBase, edge / edgeLength, hitBuffer, edgeLength, ~0, QueryTriggerInteraction.Collide);
            for (int h = 0; h < count; h++)
            {
                AddContact(hitBuffer[h], 1f, midSweep);
            }
        }

        contacts.Sort((a, b) => a.time.CompareTo(b.time));
        foreach (Contact contact in contacts)
        {
            // A block or clash ends the owner's attack; stop there.
            if (Owner == null || !Owner.IsStriking)
            {
                break;
            }

            Resolve(contact, edgeDir);
        }
    }

    void AddContact(RaycastHit hit, float time, Vector3 sweepDir)
    {
        if (hit.collider == null)
        {
            return;
        }

        contacts.Add(new Contact
        {
            collider = hit.collider,
            point = hit.distance > 0f ? hit.point : hit.collider.ClosestPoint(EdgeBaseWorld),
            time = time,
            direction = sweepDir,
        });
    }

    void Resolve(Contact contact, Vector3 edgeDir)
    {
        Collider struck = contact.collider;
        if (struck == null || struck.GetComponentInParent<Character>() == Owner)
        {
            return;     // The wielder's own body or weapons.
        }

        Plane plane = new Plane(CutNormal(edgeDir, contact.direction), contact.point);

        // Blade meets blade: the other fighter's weapon may stop this one.
        Weapon otherWeapon = struck.GetComponentInParent<Weapon>();
        if (otherWeapon != null)
        {
            // Weapons aren't parented to the hand, so the wielder's own show up here too.
            Character defender = otherWeapon.Owner;
            if (defender == Owner)
            {
                return;
            }

            if (defender != null && !struckThisSwing.Contains(defender)
                && defender.ReceiveBladeContact(MakeStrike(struck, contact.point, contact.direction, plane)) != Character.StrikeResult.Ignored)
            {
                struckThisSwing.Add(defender);
            }
            return;
        }

        Sliceable target = struck.GetComponentInParent<Sliceable>();
        if (target == null)
        {
            return;     // Scenery.
        }

        if (target.IsDead)
        {
            if (CanCut && !cutThisFrame.Contains(target))
            {
                cutThisFrame.Add(target);
                target.Slice(plane, SliceID, null);
            }
            return;
        }

        Character victim = target.GetComponent<Character>();
        if (victim == null || victim == Owner || struckThisSwing.Contains(victim))
        {
            return;
        }

        // The blade reached the body's outline (movement capsule): score it by the bone it was
        // heading into, or failing that the nearest one, never by guesswork.
        Vector3 point = contact.point;
        if (!target.IsBoneCollider(struck))
        {
            Collider bone = target.FindBoneAlong(point, contact.direction, 0.8f, out Vector3 bonePoint);
            if (bone == null)
            {
                bone = target.NearestBoneCollider(point);
                bonePoint = bone != null ? bone.ClosestPoint(point) : point;
            }

            if (bone != null)
            {
                struck = bone;
                point = bonePoint;
                plane = new Plane(plane.normal, point);
            }
        }

        struckThisSwing.Add(victim);
        victim.ReceiveStrike(MakeStrike(struck, point, contact.direction, plane));
    }

    Character.Strike MakeStrike(Collider struck, Vector3 point, Vector3 direction, Plane plane)
    {
        CharacterControl control = Owner.Control;
        return new Character.Strike
        {
            attacker = Owner,
            weapon = this,
            collider = struck,
            point = point,
            direction = direction,
            cutPlane = plane,
            swingId = Owner.SwingId,
            sliceId = SliceID,
            height = control != null ? control.AttackHeight : CharacterControl.Stance.Mid,
            guardDamage = control != null ? control.CurrentGuardDamage : weaponDamage,
        };
    }

    struct Contact
    {
        public Collider collider;
        public Vector3 point;
        public float time;          // 0..1 through this frame's swing.
        public Vector3 direction;
    }

    // Cut a body that was just killed by this weapon, along the edge's current
    // plane. Waits a frame so the ragdoll conversion has finished.
    public void CutAlongEdge(Sliceable target)
    {
        if (!CanCut || target == null || !edgeReady || !isActiveAndEnabled)
        {
            return;
        }

        Vector3 curBase = EdgeBaseWorld;
        Vector3 curTip = EdgeTipWorld;
        Vector3 edgeDir = (curTip - curBase).normalized;
        Vector3 sweep = hasPrev ? ((curBase + curTip) - (prevBase + prevTip)) : Vector3.zero;
        Plane plane = new Plane(CutNormal(edgeDir, sweep.normalized), (curBase + curTip) * 0.5f);

        StartCoroutine(CutNextFrame(target, plane, Time.time));
    }

    IEnumerator CutNextFrame(Sliceable target, Plane plane, float killedAt)
    {
        yield return null;

        // Skip it if the edge sweep already cut the body since the kill. Otherwise use a fresh
        // slice id: this swing's id may already be spent on a limb it cut off earlier.
        if (target != null && target.LastCutTime < killedAt)
        {
            target.Slice(plane, Random.Range(1, int.MaxValue), null);
        }
    }

    // The cut plane contains the edge and the direction it travelled. If the blade
    // moved along its own length (a stab) or barely moved, cut along the flat of the blade.
    Vector3 CutNormal(Vector3 edgeDir, Vector3 sweepDir)
    {
        Vector3 normal = Vector3.Cross(edgeDir, sweepDir);
        if (normal.magnitude >= minSweepAngleSin)
        {
            return normal.normalized;
        }

        return transform.TransformDirection(localNormal).normalized;
    }

    bool IsOwnBody(Sliceable target)
    {
        return Owner != null && target.transform == Owner.transform;
    }

    void BuildEdge()
    {
        if (customEdge)
        {
            localBase = edgeBase;
            localTip = edgeTip;
            localNormal = bladeNormal.sqrMagnitude > 0.0001f ? bladeNormal.normalized : Vector3.right;
            edgeReady = (localTip - localBase).sqrMagnitude > 0.000001f;
            return;
        }

        // Prefer the weapon's own hitbox (the team sized those to the blade); fall back to the mesh,
        // which also includes the handle.
        bool fromHitbox = TryGetHitboxBounds(out Bounds bounds);
        edgeReady = fromHitbox || TryGetMeshBounds(out bounds);
        if (!edgeReady)
        {
            return;
        }

        // Longest axis runs along the weapon, shortest is the blade's thickness
        // (compared at real size, since some weapons are scaled unevenly).
        Vector3 scale = transform.lossyScale;
        Vector3 size = Vector3.Scale(bounds.size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        int lengthAxis = 0;
        int thinAxis = 0;
        for (int i = 1; i < 3; i++)
        {
            if (size[i] > size[lengthAxis]) lengthAxis = i;
            if (size[i] < size[thinAxis]) thinAxis = i;
        }
        if (thinAxis == lengthAxis)
        {
            thinAxis = (lengthAxis + 1) % 3;
        }

        // The weapon prefab's pivot is the grip (it's placed in the hand). The tip is the
        // far end from it; the edge runs from the grip (or the near end) out to the tip.
        Vector3 grip = transform.InverseTransformPoint(transform.root.position);
        float near = bounds.min[lengthAxis];
        float far = bounds.max[lengthAxis];
        float gripCoord = grip[lengthAxis];
        if (Mathf.Abs(near - gripCoord) > Mathf.Abs(far - gripCoord) + 0.0001f)
        {
            float swap = near;
            near = far;
            far = swap;
        }

        float baseCoord = Mathf.Clamp(gripCoord, Mathf.Min(near, far), Mathf.Max(near, far));
        if (!fromHitbox)
        {
            baseCoord = Mathf.Lerp(baseCoord, far, bladeStart);
        }

        localBase = bounds.center;
        localBase[lengthAxis] = baseCoord;
        localTip = bounds.center;
        localTip[lengthAxis] = far;
        localNormal = Vector3.zero;
        localNormal[thinAxis] = 1f;
    }

    bool TryGetHitboxBounds(out Bounds bounds)
    {
        BoxCollider box = GetComponent<BoxCollider>();
        if (box != null)
        {
            bounds = new Bounds(box.center, box.size);
            return true;
        }

        CapsuleCollider capsule = GetComponent<CapsuleCollider>();
        if (capsule != null)
        {
            Vector3 size = Vector3.one * capsule.radius * 2f;
            size[capsule.direction] = Mathf.Max(capsule.height, capsule.radius * 2f);
            bounds = new Bounds(capsule.center, size);
            return true;
        }

        bounds = new Bounds();
        return false;
    }

    // Bounds of every mesh on the weapon, in this object's local space.
    bool TryGetMeshBounds(out Bounds bounds)
    {
        bounds = new Bounds();
        bool any = false;

        foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null)
            {
                continue;
            }

            Bounds meshBounds = filter.sharedMesh.bounds;
            Vector3 min = meshBounds.min;
            Vector3 max = meshBounds.max;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3(
                    (i & 1) == 0 ? min.x : max.x,
                    (i & 2) == 0 ? min.y : max.y,
                    (i & 4) == 0 ? min.z : max.z);
                Vector3 local = transform.InverseTransformPoint(filter.transform.TransformPoint(corner));

                if (!any)
                {
                    bounds = new Bounds(local, Vector3.zero);
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(local);
                }
            }
        }

        return any;
    }

    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying)
        {
            BuildEdge();
        }

        if (!edgeReady)
        {
            return;
        }

        Vector3 a = EdgeBaseWorld;
        Vector3 b = EdgeTipWorld;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(a, b);
        Gizmos.DrawSphere(a, 0.01f);
        Gizmos.DrawSphere(b, 0.015f);
        Gizmos.color = Color.cyan;
        Vector3 mid = (a + b) * 0.5f;
        Gizmos.DrawLine(mid, mid + transform.TransformDirection(localNormal).normalized * 0.1f);
    }
}
