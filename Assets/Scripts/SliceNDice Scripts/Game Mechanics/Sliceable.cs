using BzKovSoft.ObjectSlicer;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using System.Diagnostics;
using BzKovSoft.CharacterSlicer;
using BzKovSoft.CharacterSlicerSamples;
using UnityEngine.Profiling;
using UnityEngine.InputSystem;


public class Sliceable : BzSliceableCharacterBase, IDeadable
{
#pragma warning disable 0649
	[SerializeField]
	int _maxSliceCount = 3;
	[SerializeField]
	public GameObject _bloodPrefub;
	[SerializeField]
	Vector3 _prefubDirection;
	[SerializeField]
	bool _convertToRagdoll = true;
	[SerializeField]
	bool _alignPrefSize = false;
	[Header("Live limb loss")]
	[SerializeField]
	int _maxLimbCuts = 4;
	[Tooltip("How far clear of the torso a limb cut must stay (in torso half-sizes). Lower lets cuts graze the torso.")]
	[SerializeField, Range(0.1f, 1.5f)]
	float _coreSafetyMargin = 0.85f;
#pragma warning restore 0649

	public bool IsDead
	{
		get; private set;
	}

	public float LastCutTime { get; private set; } = float.NegativeInfinity;

	// True while a slice is duplicating this object. Gameplay scripts check it in
	// Awake so the severed copy doesn't spawn weapons, take over the HUD, etc.
	public static bool IsSpawningPiece { get; private set; }

	// Every severed piece spawned this match, so the match reset can clear them all.
	static readonly List<GameObject> spawnedPieces = new List<GameObject>();

	public void MarkDead()
	{
		IsDead = true;
	}

	// True when the skeleton has its own ragdoll bodies. A few older rigs only have the
	// root Rigidbody; the character slicer can't cut those.
	public bool HasRagdollBones
	{
		get
		{
			Rigidbody root = GetComponent<Rigidbody>();
			foreach (Rigidbody body in GetComponentsInChildren<Rigidbody>())
			{
				if (body != root)
				{
					return true;
				}
			}

			return false;
		}
	}

	// Anything else (like a dropped weapon) that should be cleared with the match's pieces.
	public static void RegisterPiece(GameObject piece)
	{
		spawnedPieces.Add(piece);
	}

	// The weapons this fighter is holding (they follow the hands but aren't parented to them).
	List<HeldWeapon> HeldWeapons()
	{
		List<HeldWeapon> held = new List<HeldWeapon>();
		CharacterControl control = GetComponent<CharacterControl>();
		if (control == null)
		{
			return held;
		}

		foreach (GameObject weaponObject in new[] { control.Wep, control.WepOff })
		{
			HeldWeapon weapon = weaponObject != null ? weaponObject.GetComponent<HeldWeapon>() : null;
			if (weapon != null)
			{
				held.Add(weapon);
			}
		}

		return held;
	}

	public static void DestroyAllPieces()
	{
		for (int i = 0; i < spawnedPieces.Count; i++)
		{
			if (spawnedPieces[i] != null)
			{
				Destroy(spawnedPieces[i]);
			}
		}

		spawnedPieces.Clear();
	}

	protected override void GetNewObjects(out GameObject resultObjNeg, out GameObject resultObjPos)
	{
		resultObjNeg = gameObject;
		resultObjPos = null;

		// Build the copy under an inactive holder so none of its scripts wake up, strip
		// everything that makes it a fighter (input, AI, HUD, animation, movement capsule),
		// and only then let it into the world.
		GameObject holder = new GameObject("SlicePieceHolder");
		holder.SetActive(false);
		IsSpawningPiece = true;
		try
		{
			resultObjPos = Instantiate(gameObject, transform.position, transform.rotation, holder.transform);
			StripFighterComponents(resultObjPos);
			resultObjPos.transform.SetParent(transform.parent, true);
		}
		finally
		{
			IsSpawningPiece = false;
			Destroy(holder);
		}

		resultObjPos.name = resultObjNeg.name + "_pos";
		resultObjNeg.name = resultObjNeg.name + "_neg";

		// Only the original object is "the fighter"; the copy is just a body part.
		resultObjPos.tag = "Untagged";
		spawnedPieces.Add(resultObjPos);
	}

	static void StripFighterComponents(GameObject copy)
	{
		DestroyImmediateIfPresent(copy.GetComponent<FighterAI>());
		DestroyImmediateIfPresent(copy.GetComponent<CharacterControl>());
		DestroyImmediateIfPresent(copy.GetComponent<Character>());
		DestroyImmediateIfPresent(copy.GetComponent<PlayerInput>());
		DestroyImmediateIfPresent(copy.GetComponent<Animator>());

		// The living fighter's movement capsule rides on the root body (not on a bone). On a
		// body part it would be left behind as an invisible pillar.
		Rigidbody rootBody = copy.GetComponent<Rigidbody>();
		foreach (Collider collider in copy.GetComponentsInChildren<Collider>(true))
		{
			if (collider.GetComponent<Rigidbody>() == null && collider.GetComponentInParent<Rigidbody>(true) == rootBody)
			{
				DestroyImmediate(collider);
			}
		}
	}

	static void DestroyImmediateIfPresent(Component component)
	{
		if (component != null)
		{
			DestroyImmediate(component);
		}
	}

	// Held weapons and particle effects are never part of a body cut.
	protected override Renderer[] GetRenderers(GameObject go)
	{
		return go.GetComponentsInChildren<Renderer>()
			.Where(r => !(r is ParticleSystemRenderer) && r.GetComponentInParent<HeldWeapon>() == null)
			.ToArray();
	}

	protected override void OnSliceFinished(BzSliceTryResult result)
	{
		if (!result.sliced)
		{
			return;
		}

		var addData = (ResultData)result.addData;
		LastCutTime = Time.time;

		var resultNeg = result.outObjectNeg.GetComponent<Sliceable>();
		var resultPos = result.outObjectPos.GetComponent<Sliceable>();
		var lazyActionNeg = result.outObjectNeg.GetComponent<LazyActionRunner>();
		var lazyActionPos = result.outObjectPos.GetComponent<LazyActionRunner>();

		if (addData.liveCut != null)
		{
			// Live limb cut: this object is still the fighter and keeps animating;
			// only the severed limb becomes a ragdoll. Bookkeeping first, so a problem with
			// the blood effect can't leave the piece frozen in mid-air.
			ConvertToRagdoll(result.outObjectPos, addData.liveCut.flingVelocity, Vector3.zero, lazyActionPos);
			IgnoreMovementCapsule(gameObject, result.outObjectPos);
			resultPos.IsDead = true;
			resultPos._maxSliceCount = _maxSliceCount;
			liveCutSucceeded = true;
			AddBloodSafely(result);
			return;
		}

		AddBloodSafely(result);

        // convert to ragdoll
        if (_convertToRagdoll & !IsDead)
        {
            Profiler.BeginSample("ConvertToRagdoll");
            ConvertToRagdoll(result.outObjectNeg, result.outObjectPos, lazyActionNeg, lazyActionPos);
            Profiler.EndSample();
        }

		IsDead = true;
		resultNeg.IsDead = IsDead;
		resultPos.IsDead = IsDead;

		--_maxSliceCount;
		resultNeg._maxSliceCount = _maxSliceCount;
		resultPos._maxSliceCount = _maxSliceCount;
	}

	void AddBloodSafely(BzSliceTryResult result)
	{
		Profiler.BeginSample("AddBlood");
		try
		{
			AddBlood(result);
		}
		catch (Exception e)
		{
			UnityEngine.Debug.LogException(e);
		}
		Profiler.EndSample();
	}


	protected override void OnSliceFinishedWorkerThread(bool sliced, object addData)
    {
        ((ResultData)addData).stopwatch.Stop();
    }

	protected override BzSliceTryData PrepareData(Plane plane)
	{
		if (_maxSliceCount == 0 || !HasRagdollBones)
		{
			return null;
		}

		// remember some date. Later we could use it after the slice is done.
		// here I add Stopwatch object to see how much time it takes
		ResultData addData = new ResultData();
		addData.stopwatch = Stopwatch.StartNew();

		if (pendingLiveCut != null)
		{
			addData.liveCut = pendingLiveCut;
			return new BzSliceTryData()
			{
				componentManager = new LiveLimbComponentManager(gameObject, plane, pendingLiveCut),
				plane = plane,
				addData = addData,
			};
		}

		// collider we want to participate in slicing (skip switched-off ones, like the
		// dead fighter's movement capsule, so slicing doesn't bring them back as mesh colliders)
		var collidersArr = GetComponentsInChildren<Collider>().Where(c => c.enabled).ToArray();

		// create component manager.
		var componentManager = new CharacterComponentManagerFast(gameObject, plane, collidersArr);

		return new BzSliceTryData()
		{
			componentManager = componentManager,
			plane = plane,
			addData = addData,
		};
	}

	/**************************/
	// Live limb loss

	public struct LimbCut
	{
		public Plane plane;                 // Oriented so the severed limb is on the positive side.
		public bool isLeg;
		public List<GameObject> lostWeapons;    // Held weapons whose hand was cut off.
	}

	class LiveCut
	{
		public List<Collider> severed;
		public List<Collider> kept;
		public Collider limbRoot;           // The bone the cut goes through.
		public Vector3 flingVelocity;
	}

	LiveCut pendingLiveCut;
	bool liveCutSucceeded;
	int limbCutsDone;
	int lastLimbAttemptFrame = -1;

	// Skeleton description, rebuilt after every cut.
	List<Collider> boneCache;
	HashSet<Collider> coreCache;
	Dictionary<Rigidbody, Rigidbody> jointParent;
	Dictionary<Rigidbody, List<Rigidbody>> jointChildren;
	List<Transform> skinnedBones;

	static readonly string[] CoreBoneWords = { "pelvis", "hips", "spine", "chest", "torso", "neck", "head" };
	static readonly string[] LegBoneWords = { "leg", "thigh", "shin", "calf", "knee", "foot", "toe", "hip." };
	static readonly string[] ArmBoneWords = { "arm", "bicep", "elbow", "hand", "wrist", "shoulder", "clav" };

	// Tries to cut off the arm or leg the plane crosses while the fighter stays alive.
	// The cut plane is infinite, so it's only allowed when it would take exactly one limb
	// (the struck one) and nothing else: no torso, no other limb, no stray hand or foot.
	// Otherwise nothing is cut and the caller just deals normal damage.
	public bool TryCutLimb(Plane plane, int sliceId, Collider hitCollider, Vector3 flingVelocity, out LimbCut cut)
	{
		cut = default;

		if (IsDead || limbCutsDone >= _maxLimbCuts || lastLimbAttemptFrame == Time.frameCount || !HasRagdollBones)
		{
			return false;
		}

		BuildSkeletonCache();
		List<Collider> bones = boneCache;
		HashSet<Collider> core = coreCache;
		if (core.Count == 0 || !bones.Contains(hitCollider) || core.Contains(hitCollider))
		{
			return false;
		}

		// One full check per frame is plenty (several rays usually hit the same limb).
		lastLimbAttemptFrame = Time.frameCount;

		// Weight the torso by size so tiny helper colliders don't decide which side it's on.
		Vector3 coreCenter = Vector3.zero;
		float coreWeight = 0f;
		float coreBottom = float.MaxValue;
		float biggestCore = core.Max(c => Volume(c.bounds));
		foreach (Collider c in core)
		{
			float w = Volume(c.bounds);
			coreCenter += c.bounds.center * w;
			coreWeight += w;
			coreBottom = Mathf.Min(coreBottom, c.bounds.min.y);
		}
		coreCenter /= Mathf.Max(coreWeight, 0.0001f);

		// The torso's half stays the fighter, and the slicer always keeps the original
		// object for the negative side, so put the torso there.
		if (plane.GetSide(coreCenter))
		{
			plane = plane.flipped;
		}

		foreach (Collider c in core)
		{
			Bounds b = c.bounds;
			if (Volume(b) < biggestCore * 0.1f)
			{
				continue;
			}

			Vector3 n = plane.normal;
			float reach = Mathf.Abs(n.x) * b.extents.x + Mathf.Abs(n.y) * b.extents.y + Mathf.Abs(n.z) * b.extents.z;
			if (plane.GetDistanceToPoint(b.center) > -reach * _coreSafetyMargin)
			{
				return false;
			}
		}

		List<Collider> severed = FindSeveredLimb(plane, hitCollider, bones, core, out Collider limbRoot);
		if (severed == null || !SkinMatchesLimb(plane, severed, limbRoot, bones))
		{
			return false;
		}

		// Decide once which held weapons go with the limb; the fighter and the severed copy
		// both use this list, so a weapon can never end up in both places or neither.
		List<HeldWeapon> lostWeapons = new List<HeldWeapon>();
		foreach (HeldWeapon weapon in HeldWeapons())
		{
			if (plane.GetSide(weapon.GripPosition))
			{
				lostWeapons.Add(weapon);
			}
		}

		cut.plane = plane;
		cut.isLeg = IsLeg(severed, coreBottom);
		cut.lostWeapons = lostWeapons.Select(w => w.gameObject).ToList();

		pendingLiveCut = new LiveCut
		{
			severed = severed,
			kept = bones.Where(c => !severed.Contains(c)).ToList(),
			limbRoot = limbRoot,
			flingVelocity = flingVelocity,
		};
		liveCutSucceeded = false;

		bool wasAsync = Asynchronously;
		Asynchronously = false;
		try
		{
			Slice(plane, sliceId, null);
		}
		finally
		{
			Asynchronously = wasAsync;
			pendingLiveCut = null;
		}

		if (liveCutSucceeded)
		{
			limbCutsDone++;
			boneCache = null;   // The skeleton changed.
		}

		return liveCutSucceeded;
	}

	// The struck bone plus every bone hanging off it (through joints) beyond the cut.
	// Returns null if the cut wouldn't take a single, clean limb.
	List<Collider> FindSeveredLimb(Plane plane, Collider hitCollider, List<Collider> bones, HashSet<Collider> core, out Collider limbRoot)
	{
		limbRoot = null;

		Rigidbody hitBody = hitCollider.GetComponent<Rigidbody>();
		if (hitBody == null)
		{
			return null;
		}

		// If the hit lands beyond a joint whose bone is itself mostly past the cut, the cut
		// really goes through that parent bone: climb to it (e.g. forearm hit, cut at the bicep).
		Rigidbody rootBody = hitBody;
		while (jointParent.TryGetValue(rootBody, out Rigidbody parent))
		{
			Collider parentCollider = BoneCollider(parent, bones);
			if (parentCollider == null || core.Contains(parentCollider) || !plane.GetSide(ShapeCenter(parentCollider)))
			{
				break;
			}

			rootBody = parent;
		}

		limbRoot = BoneCollider(rootBody, bones);
		if (limbRoot == null || !(plane.GetSide(ShapeCenter(limbRoot)) || Straddles(limbRoot, plane)))
		{
			return null;
		}

		// The limb is that bone and all of its joint descendants.
		HashSet<Rigidbody> limbBodies = new HashSet<Rigidbody>();
		Stack<Rigidbody> open = new Stack<Rigidbody>();
		open.Push(rootBody);
		while (open.Count > 0)
		{
			Rigidbody body = open.Pop();
			if (!limbBodies.Add(body))
			{
				continue;
			}

			if (jointChildren.TryGetValue(body, out List<Rigidbody> kids))
			{
				foreach (Rigidbody kid in kids)
				{
					open.Push(kid);
				}
			}
		}

		List<Collider> severed = bones.Where(c => limbBodies.Contains(c.GetComponent<Rigidbody>())).ToList();
		if (severed.Any(c => core.Contains(c)))
		{
			return null;
		}

		// Everything below the cut bone must be past the cut. (A limb bent back across the
		// plane would leave part of its mesh on the fighter with no bone physics.)
		foreach (Collider c in severed)
		{
			if (c != limbRoot && !plane.GetSide(ShapeCenter(c)))
			{
				return null;
			}
		}

		// Every other limb must be completely clear of the plane. Only the bone the limb
		// hangs from may be nicked.
		jointParent.TryGetValue(rootBody, out Rigidbody attachBody);
		foreach (Collider c in bones)
		{
			if (core.Contains(c) || severed.Contains(c) || c.GetComponent<Rigidbody>() == attachBody)
			{
				continue;
			}

			if (MaxSignedDistance(c, plane) > 0.005f)
			{
				return null;
			}
		}

		return severed;
	}

	// Hands, feet and fingers have no colliders. Every skinned bone the plane cuts away must
	// belong to the severed limb (its nearest bone collider is one of the limb's), and every
	// bone of the limb must be cut away. Bones right at the cut can go either way.
	bool SkinMatchesLimb(Plane plane, List<Collider> severed, Collider limbRoot, List<Collider> bones)
	{
		Rigidbody rootBody = limbRoot.GetComponent<Rigidbody>();
		jointParent.TryGetValue(rootBody, out Rigidbody attachBody);

		foreach (Transform bone in skinnedBones)
		{
			if (bone == null)
			{
				continue;
			}

			Vector3 position = bone.position;
			Collider owner = null;
			float best = float.MaxValue;
			foreach (Collider c in bones)
			{
				float d = (c.ClosestPoint(position) - position).sqrMagnitude;
				if (d < best)
				{
					best = d;
					owner = c;
				}
			}

			if (owner == null)
			{
				continue;
			}

			Rigidbody ownerBody = owner.GetComponent<Rigidbody>();
			if (ownerBody == rootBody || ownerBody == attachBody)
			{
				continue;
			}

			if (plane.GetSide(position) != severed.Contains(owner))
			{
				return false;
			}
		}

		return true;
	}

	void BuildSkeletonCache()
	{
		if (boneCache != null)
		{
			return;
		}

		// Colliders that belong to skeleton bones (each bone has its own Rigidbody),
		// leaving out the movement capsule and held weapons.
		boneCache = new List<Collider>();
		foreach (Collider c in GetComponentsInChildren<Collider>())
		{
			if (c.enabled && c.gameObject != gameObject && c.GetComponent<Rigidbody>() != null && c.GetComponentInParent<HeldWeapon>() == null)
			{
				boneCache.Add(c);
			}
		}

		Rigidbody rootBody = GetComponent<Rigidbody>();
		jointParent = new Dictionary<Rigidbody, Rigidbody>();
		jointChildren = new Dictionary<Rigidbody, List<Rigidbody>>();
		foreach (CharacterJoint joint in GetComponentsInChildren<CharacterJoint>())
		{
			Rigidbody child = joint.GetComponent<Rigidbody>();
			Rigidbody parent = joint.connectedBody;
			if (child == null || parent == null || parent == rootBody)
			{
				continue;
			}

			jointParent[child] = parent;
			if (!jointChildren.TryGetValue(parent, out List<Rigidbody> list))
			{
				list = new List<Rigidbody>();
				jointChildren[parent] = list;
			}
			list.Add(child);
		}

		coreCache = GetCoreColliders(boneCache);

		// Points that must end up on the right side of the cut: bones that actually move some
		// of the body mesh, plus any non-skinned body parts (accessories parented to bones).
		skinnedBones = new List<Transform>();
		foreach (MeshRenderer part in GetComponentsInChildren<MeshRenderer>())
		{
			if (part.GetComponentInParent<HeldWeapon>() == null)
			{
				skinnedBones.Add(part.transform);
			}
		}

		foreach (SkinnedMeshRenderer skin in GetComponentsInChildren<SkinnedMeshRenderer>())
		{
			if (skin.sharedMesh == null || skin.GetComponentInParent<HeldWeapon>() != null)
			{
				continue;
			}

			Transform[] skinBones = skin.bones;
			HashSet<int> used = new HashSet<int>();
			foreach (BoneWeight w in skin.sharedMesh.boneWeights)
			{
				if (w.weight0 > 0f) used.Add(w.boneIndex0);
				if (w.weight1 > 0f) used.Add(w.boneIndex1);
				if (w.weight2 > 0f) used.Add(w.boneIndex2);
				if (w.weight3 > 0f) used.Add(w.boneIndex3);
			}

			foreach (int index in used)
			{
				if (index >= 0 && index < skinBones.Length && skinBones[index] != null)
				{
					skinnedBones.Add(skinBones[index]);
				}
			}
		}
	}

	// The torso chain: bones named like a torso, plus the chain of biggest bones walking
	// out from the skeleton's root joint (so rigs with other naming still work).
	HashSet<Collider> GetCoreColliders(List<Collider> bones)
	{
		HashSet<Collider> core = new HashSet<Collider>();
		foreach (Collider c in bones)
		{
			string boneName = c.gameObject.name.ToLowerInvariant();
			if (CoreBoneWords.Any(word => boneName.Contains(word)))
			{
				core.Add(c);
			}
		}

		Rigidbody current = bones
			.Select(c => c.GetComponent<Rigidbody>())
			.FirstOrDefault(body => !jointParent.ContainsKey(body) && jointChildren.ContainsKey(body));

		HashSet<Rigidbody> visited = new HashSet<Rigidbody>();
		while (current != null && visited.Add(current))
		{
			foreach (Collider c in current.GetComponents<Collider>())
			{
				if (bones.Contains(c))
				{
					core.Add(c);
				}
			}

			current = jointChildren.TryGetValue(current, out List<Rigidbody> next)
				? next.OrderByDescending(body => body.GetComponents<Collider>().Sum(c => Volume(c.bounds))).FirstOrDefault()
				: null;
		}

		return core;
	}

	static Collider BoneCollider(Rigidbody body, List<Collider> bones)
	{
		foreach (Collider c in body.GetComponents<Collider>())
		{
			if (bones.Contains(c))
			{
				return c;
			}
		}

		return null;
	}

	static bool IsLeg(List<Collider> severed, float coreBottom)
	{
		foreach (Collider c in severed)
		{
			string boneName = c.gameObject.name.ToLowerInvariant();
			if (LegBoneWords.Any(word => boneName.Contains(word)))
			{
				return true;
			}
		}

		// No telling names: anything hanging below the torso counts as a leg.
		float averageHeight = severed.Average(c => c.bounds.center.y);
		return averageHeight < coreBottom;
	}

	/**************************/
	// Hit zones (Bushido Blade style: head/body kill, arms/legs wound)

	public enum BodyZone { Vital, Arm, Leg }

	// Where a blow landed. limbRoot is the struck limb's top bone (for arms and legs).
	public BodyZone ClassifyHit(Collider hit, Vector3 point, CharacterControl.Stance attackHeight, out Collider limbRoot)
	{
		limbRoot = null;
		bool haveBones = HasRagdollBones;
		if (haveBones)
		{
			BuildSkeletonCache();
			if (hit != null && boneCache.Contains(hit))
			{
				if (coreCache.Contains(hit))
				{
					return BodyZone.Vital;
				}

				limbRoot = LimbRootOf(hit);
				return IsLegLimb(limbRoot) ? BodyZone.Leg : BodyZone.Arm;
			}
		}

		// The blade only reached the body's outline (the movement capsule), or this rig has no
		// bone colliders: go by the height the attack was aimed at.
		if (attackHeight == CharacterControl.Stance.Low)
		{
			if (haveBones)
			{
				limbRoot = NearestLimbRoot(point, true);
			}
			return BodyZone.Leg;
		}

		return BodyZone.Vital;
	}

	// True for the skeleton's own hit colliders (not the movement capsule or a weapon).
	public bool IsBoneCollider(Collider c)
	{
		if (c == null || !HasRagdollBones)
		{
			return false;
		}

		BuildSkeletonCache();
		return boneCache.Contains(c);
	}

	static readonly RaycastHit[] probeHits = new RaycastHit[32];

	// The first of this body's bones a line from the point (on the body's outline) runs into.
	public Collider FindBoneAlong(Vector3 point, Vector3 direction, float distance, out Vector3 bonePoint)
	{
		bonePoint = point;
		if (!HasRagdollBones || direction.sqrMagnitude < 0.000001f)
		{
			return null;
		}

		BuildSkeletonCache();
		int count = Physics.RaycastNonAlloc(point, direction.normalized, probeHits, distance, ~0, QueryTriggerInteraction.Collide);
		Collider best = null;
		float bestDistance = float.MaxValue;
		for (int i = 0; i < count; i++)
		{
			if (probeHits[i].distance < bestDistance && boneCache.Contains(probeHits[i].collider))
			{
				bestDistance = probeHits[i].distance;
				best = probeHits[i].collider;
				bonePoint = probeHits[i].point;
			}
		}

		return best;
	}

	// The bone nearest a point (null on rigs without bone colliders).
	public Collider NearestBoneCollider(Vector3 point)
	{
		if (!HasRagdollBones)
		{
			return null;
		}

		BuildSkeletonCache();
		return NearestBone(point);
	}

	// True if the collider belongs to the limb hanging from limbRoot.
	public bool IsInLimb(Collider c, Collider limbRoot)
	{
		if (c == null || limbRoot == null)
		{
			return false;
		}

		BuildSkeletonCache();
		Rigidbody body = c.GetComponent<Rigidbody>();
		return body != null && LimbBodies(limbRoot).Contains(body);
	}

	// True if the weapon's grip is on the limb hanging from limbRoot (the hand holding it).
	public bool LimbHolds(Collider limbRoot, Transform weapon)
	{
		BuildSkeletonCache();
		Vector3 grip = GripPoint(weapon);
		Collider owner = NearestBone(grip);
		return owner != null && IsInLimb(owner, limbRoot);
	}

	// A few of the torso's larger colliders, for extra wounds.
	public List<Collider> RandomVitals(int count)
	{
		List<Collider> picks = new List<Collider>();
		if (!HasRagdollBones)
		{
			return picks;
		}

		BuildSkeletonCache();
		List<Collider> vitals = coreCache.Where(c => c != null && c.enabled).ToList();
		if (vitals.Count == 0)
		{
			return picks;
		}

		float biggest = vitals.Max(c => Volume(c.bounds));
		vitals = vitals.Where(c => Volume(c.bounds) >= biggest * 0.1f).ToList();
		for (int i = 0; i < count; i++)
		{
			picks.Add(vitals[UnityEngine.Random.Range(0, vitals.Count)]);
		}

		return picks;
	}

	Collider LimbRootOf(Collider c)
	{
		Rigidbody body = c.GetComponent<Rigidbody>();
		while (body != null && jointParent.TryGetValue(body, out Rigidbody parent))
		{
			Collider parentCollider = BoneCollider(parent, boneCache);
			if (parentCollider == null || coreCache.Contains(parentCollider))
			{
				break;
			}

			body = parent;
		}

		return body != null ? BoneCollider(body, boneCache) ?? c : c;
	}

	HashSet<Rigidbody> LimbBodies(Collider limbRoot)
	{
		HashSet<Rigidbody> bodies = new HashSet<Rigidbody>();
		Rigidbody start = limbRoot.GetComponent<Rigidbody>();
		if (start == null)
		{
			return bodies;
		}

		Stack<Rigidbody> open = new Stack<Rigidbody>();
		open.Push(start);
		while (open.Count > 0)
		{
			Rigidbody body = open.Pop();
			if (!bodies.Add(body))
			{
				continue;
			}

			if (jointChildren.TryGetValue(body, out List<Rigidbody> kids))
			{
				foreach (Rigidbody kid in kids)
				{
					open.Push(kid);
				}
			}
		}

		return bodies;
	}

	bool IsLegLimb(Collider limbRoot)
	{
		HashSet<Rigidbody> bodies = LimbBodies(limbRoot);
		List<Collider> limb = boneCache.Where(c => bodies.Contains(c.GetComponent<Rigidbody>())).ToList();
		if (limb.Count == 0)
		{
			limb.Add(limbRoot);
		}

		// Names first (arm names win, since some rigs hang their arms low); otherwise anything
		// hanging below the middle of the torso is a leg.
		foreach (Collider c in limb)
		{
			string boneName = c.gameObject.name.ToLowerInvariant();
			if (ArmBoneWords.Any(word => boneName.Contains(word)))
			{
				return false;
			}
		}

		Collider torso = coreCache.OrderByDescending(c => Volume(c.bounds)).FirstOrDefault();
		float torsoMiddle = torso != null ? torso.bounds.center.y : float.MinValue;
		return IsLeg(limb, torsoMiddle);
	}

	// The nearest arm or leg (top bone) to a point.
	Collider NearestLimbRoot(Vector3 point, bool legs)
	{
		Collider best = null;
		float bestDistance = float.MaxValue;
		foreach (Collider c in boneCache)
		{
			if (coreCache.Contains(c))
			{
				continue;
			}

			Collider root = LimbRootOf(c);
			if (root != c || IsLegLimb(root) != legs)
			{
				continue;
			}

			float d = (c.ClosestPoint(point) - point).sqrMagnitude;
			if (d < bestDistance)
			{
				bestDistance = d;
				best = root;
			}
		}

		return best;
	}

	Collider NearestBone(Vector3 point)
	{
		Collider best = null;
		float bestDistance = float.MaxValue;
		foreach (Collider c in boneCache)
		{
			float d = (c.ClosestPoint(point) - point).sqrMagnitude;
			if (d < bestDistance)
			{
				bestDistance = d;
				best = c;
			}
		}

		return best;
	}

	/**************************/
	// Blunt kills: burst into pieces

	// Splits the (dead) body with a few rounds of random cuts through each piece, then throws
	// every piece away from the blow.
	public void Burst(Vector3 origin, Vector3 direction, int rounds, float force)
	{
		StartCoroutine(BurstRoutine(origin, direction, rounds, force));
	}

	IEnumerator BurstRoutine(Vector3 origin, Vector3 direction, int rounds, float force)
	{
		List<Sliceable> parts = new List<Sliceable> { this };

		// Let the ragdoll conversion finish first.
		yield return null;

		for (int round = 0; round < rounds; round++)
		{
			List<Sliceable> next = new List<Sliceable>();
			foreach (Sliceable part in parts)
			{
				if (part == null)
				{
					continue;
				}

				next.Add(part);

				// A fresh cut adds its colliders over the next few frames, and the slicer won't
				// cut a piece again until that's done.
				float giveUpAt = Time.time + 0.5f;
				while (part != null && part.GetComponent<LazyActionRunner>() != null && Time.time < giveUpAt)
				{
					yield return null;
				}

				if (part == null || !part.TryGetPieceBounds(out Bounds bounds))
				{
					continue;
				}

				Vector3 center = bounds.center + UnityEngine.Random.insideUnitSphere * bounds.extents.magnitude * 0.15f;
				Plane plane = new Plane(UnityEngine.Random.onUnitSphere, center);

				// Cut right now (not on a worker thread) so the new piece is known immediately.
				part.Asynchronously = false;
				Sliceable newPiece = null;
				part.Slice(plane, UnityEngine.Random.Range(1, int.MaxValue), result =>
				{
					if (result != null && result.sliced && result.outObjectPos != null)
					{
						newPiece = result.outObjectPos.GetComponent<Sliceable>();
					}
				});

				if (newPiece != null)
				{
					next.Add(newPiece);
				}
			}

			parts = next;
			yield return null;
		}

		// Give the last cuts' colliders a moment to appear, then blow everything apart.
		yield return null;
		yield return null;

		Vector3 blastCenter = origin - (direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero) * 0.3f;
		foreach (Sliceable part in parts)
		{
			if (part == null)
			{
				continue;
			}

			foreach (Rigidbody body in part.GetComponentsInChildren<Rigidbody>())
			{
				if (!body.isKinematic)
				{
					body.AddExplosionForce(force, blastCenter, 3f, 0.4f, ForceMode.VelocityChange);
				}
			}
		}
	}

	bool TryGetPieceBounds(out Bounds bounds)
	{
		bounds = new Bounds();
		bool any = false;
		foreach (Collider c in GetComponentsInChildren<Collider>())
		{
			if (!c.enabled || c.isTrigger)
			{
				continue;
			}

			if (!any)
			{
				bounds = c.bounds;
				any = true;
			}
			else
			{
				bounds.Encapsulate(c.bounds);
			}
		}

		return any;
	}

	// Where a held weapon is gripped (the hand point it's attached to).
	static Vector3 GripPoint(Transform weapon)
	{
		HeldWeapon held = weapon.GetComponent<HeldWeapon>();
		if (held != null)
		{
			return held.GripPosition;
		}

		return weapon.parent != null ? weapon.parent.position : weapon.position;
	}

	static Vector3 ShapeCenter(Collider c)
	{
		if (c is SphereCollider sphere) return c.transform.TransformPoint(sphere.center);
		if (c is CapsuleCollider capsule) return c.transform.TransformPoint(capsule.center);
		if (c is BoxCollider box) return c.transform.TransformPoint(box.center);
		return c.bounds.center;
	}

	// How far the collider's shape reaches onto the positive side of the plane
	// (negative = entirely on the negative side). Exact for spheres, capsules and boxes.
	static float MaxSignedDistance(Collider c, Plane plane)
	{
		Transform t = c.transform;
		Vector3 scale = t.lossyScale;
		scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));

		if (c is SphereCollider sphere)
		{
			float radius = sphere.radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
			return plane.GetDistanceToPoint(t.TransformPoint(sphere.center)) + radius;
		}

		if (c is CapsuleCollider capsule)
		{
			GetCapsuleSegment(capsule, out Vector3 a, out Vector3 b, out float radius);
			return Mathf.Max(plane.GetDistanceToPoint(a), plane.GetDistanceToPoint(b)) + radius;
		}

		if (c is BoxCollider box)
		{
			float max = float.MinValue;
			for (int i = 0; i < 8; i++)
			{
				Vector3 corner = box.center + Vector3.Scale(box.size * 0.5f, new Vector3(
					(i & 1) == 0 ? -1f : 1f,
					(i & 2) == 0 ? -1f : 1f,
					(i & 4) == 0 ? -1f : 1f));
				max = Mathf.Max(max, plane.GetDistanceToPoint(t.TransformPoint(corner)));
			}
			return max;
		}

		Bounds bounds = c.bounds;
		float boundsMax = float.MinValue;
		for (int i = 0; i < 8; i++)
		{
			Vector3 corner = new Vector3(
				(i & 1) == 0 ? bounds.min.x : bounds.max.x,
				(i & 2) == 0 ? bounds.min.y : bounds.max.y,
				(i & 4) == 0 ? bounds.min.z : bounds.max.z);
			boundsMax = Mathf.Max(boundsMax, plane.GetDistanceToPoint(corner));
		}
		return boundsMax;
	}

	static bool Straddles(Collider c, Plane plane)
	{
		return MaxSignedDistance(c, plane) > 0f && MaxSignedDistance(c, plane.flipped) > 0f;
	}

	// World-space centre line (sphere centres at each end) and radius of a capsule.
	static void GetCapsuleSegment(CapsuleCollider capsule, out Vector3 a, out Vector3 b, out float radius)
	{
		Transform t = capsule.transform;
		Vector3 scale = t.lossyScale;
		scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));

		Vector3 axis = Vector3.zero;
		axis[capsule.direction] = 1f;
		float axisScale = scale[capsule.direction];
		float sideScale = Mathf.Max(scale[(capsule.direction + 1) % 3], scale[(capsule.direction + 2) % 3]);

		radius = capsule.radius * sideScale;
		float halfLine = Mathf.Max(0f, capsule.height * axisScale * 0.5f - radius);
		Vector3 center = t.TransformPoint(capsule.center);
		Vector3 worldAxis = t.TransformDirection(axis).normalized;
		a = center - worldAxis * halfLine;
		b = center + worldAxis * halfLine;
	}

	// Shrinks a capsule to the part on the positive side of the plane, so a severed hand
	// doesn't fly off wrapped in the whole forearm's collider.
	static void ClipCapsuleToPositiveSide(CapsuleCollider capsule, Plane plane)
	{
		GetCapsuleSegment(capsule, out Vector3 a, out Vector3 b, out float radius);
		Transform t = capsule.transform;
		Vector3 worldAxis = (b - a).sqrMagnitude > 0.000001f ? (b - a).normalized : t.TransformDirection(Vector3.up);

		// Full extent of the capsule along its axis.
		Vector3 start = a - worldAxis * radius;
		Vector3 end = b + worldAxis * radius;
		float dStart = plane.GetDistanceToPoint(start);
		float dEnd = plane.GetDistanceToPoint(end);
		if ((dStart > 0f) == (dEnd > 0f))
		{
			return;     // Doesn't cross the plane along its length.
		}

		Vector3 crossing = Vector3.Lerp(start, end, dStart / (dStart - dEnd));
		Vector3 farEnd = dEnd > 0f ? end : start;

		float axisScale = Mathf.Abs(t.lossyScale[capsule.direction]);
		if (axisScale < 0.000001f)
		{
			return;
		}

		capsule.center = t.InverseTransformPoint((crossing + farEnd) * 0.5f);
		capsule.height = Mathf.Max(Vector3.Distance(crossing, farEnd) / axisScale, capsule.radius * 2f);
	}

	static float Volume(Bounds b)
	{
		return b.size.x * b.size.y * b.size.z;
	}

	// The fighter's movement capsule: colliders riding on the root body rather than on a bone.
	static List<Collider> MovementColliders(GameObject fighter)
	{
		Rigidbody rootBody = fighter.GetComponent<Rigidbody>();
		return fighter.GetComponentsInChildren<Collider>(true)
			.Where(c => c.GetComponent<Rigidbody>() == null && c.GetComponentInParent<Rigidbody>(true) == rootBody)
			.ToList();
	}

	// A severed limb starts inside the fighter's movement capsule; without this the physics
	// engine would shove the fighter away from its own arm.
	public static void IgnoreMovementCapsule(GameObject fighter, GameObject piece)
	{
		// (Both colliders must be switched on, or Unity refuses.)
		List<Collider> capsule = MovementColliders(fighter).Where(c => c.enabled && c.gameObject.activeInHierarchy).ToList();
		foreach (Collider pieceCollider in piece.GetComponentsInChildren<Collider>())
		{
			if (!pieceCollider.enabled)
			{
				continue;
			}

			foreach (Collider bodyCollider in capsule)
			{
				Physics.IgnoreCollision(pieceCollider, bodyCollider, true);
			}
		}
	}

	// Component handling for a live cut. The fighter (negative side) keeps its skeleton,
	// animation and joints untouched apart from losing the severed bones' physics; the copy
	// (positive side) keeps only the severed bones and is rebuilt as a ragdoll by the slicer.
	class LiveLimbComponentManager : IComponentManager
	{
		readonly GameObject original;
		readonly Plane plane;
		readonly LiveCut cut;

		public LiveLimbComponentManager(GameObject original, Plane plane, LiveCut cut)
		{
			this.original = original;
			this.plane = plane;
			this.cut = cut;
		}

		public bool Success => true;

		public void OnSlicedWorkerThread(SliceTryItem[] items) { }

		public void OnSlicedMainThread(GameObject resultObjNeg, GameObject resultObjPos, Renderer[] renderersNeg, Renderer[] renderersPos)
		{
			// Map onto the copy first, while the two hierarchies are still identical.
			List<Collider> pieceColliders = cut.severed.Select(c => BzSlicerHelper.GetSameComponentForDuplicate(c, original, resultObjPos)).ToList();
			List<Collider> pieceLeftovers = cut.kept.Select(c => BzSlicerHelper.GetSameComponentForDuplicate(c, original, resultObjPos)).ToList();
			Collider pieceRoot = BzSlicerHelper.GetSameComponentForDuplicate(cut.limbRoot, original, resultObjPos);

			// Fighter: the missing limb's bones stop colliding and lose their ragdoll physics,
			// so they can't be hit again or dangle invisibly once the fighter is a ragdoll.
			HashSet<Rigidbody> severedBodies = new HashSet<Rigidbody>();
			foreach (Collider c in cut.severed)
			{
				c.enabled = false;
				Rigidbody body = c.GetComponent<Rigidbody>();
				if (body != null)
				{
					severedBodies.Add(body);
				}
			}

			foreach (Joint joint in resultObjNeg.GetComponentsInChildren<Joint>())
			{
				if (severedBodies.Contains(joint.GetComponent<Rigidbody>()) || severedBodies.Contains(joint.connectedBody))
				{
					DestroyImmediate(joint);
				}
			}

			foreach (Rigidbody body in severedBodies)
			{
				Destroy(body);
			}

			// Severed piece: drop everything that stayed with the fighter, and trim the
			// collider of the bone the cut went through down to the part that came off.
			foreach (Collider c in pieceLeftovers)
			{
				c.enabled = false;
				Destroy(c);
			}

			if (pieceRoot is CapsuleCollider rootCapsule)
			{
				ClipCapsuleToPositiveSide(rootCapsule, plane);
			}

			CharacterComponentManagerFast.OnCompletePerSide(pieceColliders, resultObjPos);
		}
	}

	public void ConvertToRagdoll(GameObject resultNeg, GameObject resultPos, LazyActionRunner lazyRunnerNeg, LazyActionRunner lazyRunnerPos)
	{
		Animator animator = this.GetComponent<Animator>();
		Vector3 velocityContinue = animator.velocity;
		Vector3 angularVelocityContinue = animator.angularVelocity;

		ConvertToRagdoll(resultNeg, velocityContinue, angularVelocityContinue, lazyRunnerNeg);
		ConvertToRagdoll(resultPos, velocityContinue, angularVelocityContinue, lazyRunnerPos);
	}

	private void AddBlood(BzSliceTryResult result)
	{
		for (int i = 0; i < result.meshItems.Length; i++)
		{
			var meshItem = result.meshItems[i];

			if (meshItem == null)
				continue;

			for (int j = 0; j < meshItem.sliceEdgesNeg.Length; j++)
			{
				var meshData = meshItem.sliceEdgesNeg[j].capsData;
				SetBleedingObjects(meshData, meshItem.rendererNeg);
			}

			for (int j = 0; j < meshItem.sliceEdgesPos.Length; j++)
			{
				var meshData = meshItem.sliceEdgesPos[j].capsData;
				SetBleedingObjects(meshData, meshItem.rendererPos);
			}
		}
	}

	private void SetBleedingObjects(PolyMeshData meshData, Renderer renderer)
	{
		var meshRenderer = renderer as MeshRenderer;
		var skinnedRenderer = renderer as SkinnedMeshRenderer;

		GameObject blood = null;

		if (meshRenderer != null)
		{
			// add blood object
			Vector3 position = AVG(meshData.vertices);
			Vector3 direction = AVG(meshData.normals).normalized;
			var rotation = Quaternion.FromToRotation(_prefubDirection, direction);
			blood = Instantiate(_bloodPrefub, renderer.gameObject.transform);

			blood.transform.localPosition = position;
			blood.transform.localRotation = rotation;
		}
		else if (skinnedRenderer != null)
		{
			var bones = skinnedRenderer.bones;
			float[] weightSums = new float[bones.Length];
			for (int i = 0; i < meshData.boneWeights.Length; i++)
			{
				var w = meshData.boneWeights[i];
				weightSums[w.boneIndex0] += w.weight0;
				weightSums[w.boneIndex1] += w.weight1;
				weightSums[w.boneIndex2] += w.weight2;
				weightSums[w.boneIndex3] += w.weight3;
			}

			// detect most weightful bone for this PolyMeshData
			int maxIndex = 0;
			for (int i = 0; i < weightSums.Length; i++)
			{
				float maxValue = weightSums[maxIndex];
				float current = weightSums[i];

				if (current > maxValue)
					maxIndex = i;
			}
			Transform bone = bones[maxIndex];

			// add blood object to the bone
			Vector3 position = AVG(meshData.vertices);

			Vector3 normal = AVG(meshData.normals).normalized;

			var rotation = Quaternion.FromToRotation(_prefubDirection, normal);

			var m = skinnedRenderer.sharedMesh.bindposes[maxIndex];
			position = m.MultiplyPoint3x4(position);

			blood = Instantiate(_bloodPrefub, bone);
			blood.transform.localPosition = position;
			blood.transform.localRotation = rotation;
		}

		if (_alignPrefSize)
		{
			var parentScale = blood.transform.parent.lossyScale;
			var newScale = new Vector3(
				1f / parentScale.x,
				1f / parentScale.y,
				1f / parentScale.z);

			blood.transform.localScale = Vector3.Scale(newScale, blood.transform.localScale);
		}
	}

	private static Vector3 AVG(Vector3[] vertices)
	{
		Vector3 result = Vector3.zero;

		for (int i = 0; i < vertices.Length; i++)
		{
			result += vertices[i];
		}

		return result / vertices.Length;
	}

	public void ConvertToRagdoll(GameObject go, Vector3 velocityContinue, Vector3 angularVelocityContinue, LazyActionRunner lazyRunner)
	{
		Profiler.BeginSample("ConvertToRagdoll");
		// if your player is dead, you do not need animator or collision collider
		Animator animator = go.GetComponent<Animator>();
		Collider triggerCollider = go.GetComponent<Collider>();

		UnityEngine.Object.Destroy(animator);
		UnityEngine.Object.Destroy(triggerCollider);

		var collidersArr = go.GetComponentsInChildren<Collider>();
		for (int i = 0; i < collidersArr.Length; i++)
		{
			var collider = collidersArr[i];
			if (collider == triggerCollider)
				continue;

			collider.isTrigger = false;
		}

		// set rigid bodies as non kinematic
		var rigidsArr = go.GetComponentsInChildren<Rigidbody>();
		for (int i = 0; i < rigidsArr.Length; i++)
		{
			var rigid = rigidsArr[i];
			rigid.isKinematic = false;
		}

		// Started after the bodies are dynamic, since it sets their starting velocity.
		StartCoroutine(SmoothDepenetration(go, velocityContinue, angularVelocityContinue));
		Profiler.EndSample();
	}

	static IEnumerator SmoothDepenetration(GameObject go, Vector3 velocityContinue, Vector3 angularVelocityContinue)
	{
		var rigids = go.GetComponentsInChildren<Rigidbody>();
		var maxVelocitys = new float[rigids.Length];
		for (int i = 0; i < rigids.Length; i++)
		{
			var rigid = rigids[i];
			maxVelocitys[i] = rigid.maxDepenetrationVelocity;

			rigid.linearVelocity = velocityContinue;
			rigid.angularVelocity = angularVelocityContinue;
		}

		const float duration = 2f;

		float t = 0f;

		do
		{
			t += Time.deltaTime;

			// Ramp the push-apart speed up over the duration so overlapping halves
			// separate gently instead of popping.
			float r = t / duration;

			for (int i = 0; i < rigids.Length; i++)
			{
				var rigid = rigids[i];

				if (rigid == null)
				{
					continue;
				}

				rigid.maxDepenetrationVelocity = Mathf.Lerp(0.0f, 2f, r);
			}
			yield return null;
		}
		while (t < duration);


		for (int i = 0; i < rigids.Length; i++)
		{
			var rigid = rigids[i];
			if (rigid == null)
				continue;

			float maxVel = maxVelocitys[i];
			rigid.maxDepenetrationVelocity = maxVel;
		}
	}

	// Sample of data that can be attached to slice request.
	// In this the Stopwatch is used to time duration of slice operation.
	class ResultData
	{
		public Stopwatch stopwatch;
		public LiveCut liveCut;
	}
}
