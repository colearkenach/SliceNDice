using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Runs its LateUpdate (stance blade angle) before Weapon traces the blade's edge.
[DefaultExecutionOrder(-50)]
public class CharacterControl : MonoBehaviour
{
    public enum AttackType { Fast, Heavy, Stab }
    public enum Stance { High, Mid, Low }

    // An attack is wind-up (turns to face the opponent), a short window where the weapon can
    // hit, then a committed follow-through. The fighter steps in during wind-up and swing and
    // can't walk, guard or start anything else until the follow-through ends.
    [System.Serializable]
    public class AttackTiming
    {
        [Tooltip("Wind-up before the weapon can hit (seconds).")]
        public float startup = 0.12f;
        [Tooltip("How long the weapon can hit (seconds).")]
        public float active = 0.18f;
        [Tooltip("Follow-through after the hit window: no guard, no moving (seconds).")]
        public float recovery = 0.25f;
        [Tooltip("Distance stepped in toward the opponent during wind-up and swing (metres).")]
        public float lunge = 0.5f;
        [Tooltip("Stamina it costs to throw.")]
        public float staminaCost = 6f;
        [Tooltip("Stamina a guard loses blocking it.")]
        public float guardDamage = 18f;
        [Tooltip("Attack animation playback speed.")]
        public float animSpeed = 1.35f;
    }

    // Rotation is driven by code (always facing the opponent), so physics never spins the fighter.
    private const RigidbodyConstraints LiveMovementConstraints = RigidbodyConstraints.FreezeRotation;

    Rigidbody rb;
    [SerializeField] public float movementSpeed = 5f;   // Toward the opponent / generally forward.
    [SerializeField] public float backSpeed = 5f;       // Away from the opponent.
    [SerializeField] public float strafeSpeed = 4f;     // Circling around the opponent.
    [Tooltip("Scales the three speeds above. Bushido Blade fighters walk slowly and deliberately.")]
    [SerializeField, Range(0.2f, 1.5f)] float walkSpeedScale = 0.55f;
    [SerializeField] float acceleration = 35f;          // How quickly velocity reaches the stick's target.
    [SerializeField] float faceOpponentSharpness = 14f;

    [Header("Stances")]
    [SerializeField] Stance startingStance = Stance.Mid;
    [SerializeField] float stanceChangeTime = 0.15f;
    [Tooltip("Blade angle per stance (degrees, + tips the blade down), so swings land high, mid or low.")]
    [SerializeField] float highBladeTilt = -20f;
    [SerializeField] float lowBladeTilt = 55f;
    [SerializeField] float stanceTiltSharpness = 12f;
    [Tooltip("Walking speed in each stance (Low is a careful crouch).")]
    [SerializeField] float highStanceSpeed = 0.9f;
    [SerializeField] float lowStanceSpeed = 0.75f;

    [Header("Attacks")]
    [SerializeField] AttackTiming fastAttack = new AttackTiming { startup = 0.12f, active = 0.18f, recovery = 0.25f, lunge = 0.5f, staminaCost = 6f, guardDamage = 18f, animSpeed = 1.4f };
    [SerializeField] AttackTiming heavyAttack = new AttackTiming { startup = 0.28f, active = 0.2f, recovery = 0.42f, lunge = 0.9f, staminaCost = 14f, guardDamage = 40f, animSpeed = 1.25f };
    [SerializeField] AttackTiming stabAttack = new AttackTiming { startup = 0.16f, active = 0.16f, recovery = 0.32f, lunge = 1.2f, staminaCost = 9f, guardDamage = 25f, animSpeed = 1.35f };
    [Tooltip("High attacks are slower overheads that hit the guard harder; low attacks come out quicker but hit it softer.")]
    [SerializeField] float highStartupScale = 1.2f;
    [SerializeField] float lowStartupScale = 0.9f;
    [SerializeField] float highGuardDamageScale = 1.3f;
    [SerializeField] float lowGuardDamageScale = 0.8f;
    [SerializeField] float startupTurnSharpness = 25f;
    [SerializeField] float committedTurnSharpness = 2f;
    [Tooltip("Stop stepping in once this close to the opponent (metres between centres).")]
    [SerializeField] float lungeStopDistance = 0.8f;

    [Header("Dash")]
    [SerializeField] float dashSpeed = 7f;
    [SerializeField] float dashTime = 0.2f;
    [SerializeField] float dashCooldown = 0.35f;
    [SerializeField] float dashStaminaCost = 12f;

    Vector2 Moveet;
    Animator anim;
    private PlayerControls controls = null;
    public Gamepad pDevice;
    public bool P1;
    public bool P2;
    public GameObject Weaponpoint1;
    public GameObject Weaponpoint2;
    GameObject managerob;
    public CameraControl control;

    public Player1Data Play1;
    public Player2Data Play2;

    public GameObject Weapon;
    public GameObject WeaponOffhand;
    Character character;
    public GameObject Wep;
    public GameObject WepOff;
    TPSpawn spawnman;

    Vector3 moveIntent;             // Desired flat move direction (world space, length 0..1).
    Vector3 knockbackVelocity;
    bool usingAi;
    float speedMultiplier = 1f;     // Lowered by leg wounds.
    float attackSpeedScale = 1f;    // Lowered by a wounded weapon arm.

    Stance stance;
    float stanceReadyAt;

    AttackType currentAttack;
    Stance attackStance;
    float strikeStart = float.NegativeInfinity;
    float strikeEnd = float.NegativeInfinity;
    float attackEnd = float.NegativeInfinity;
    float lungeSpeed;
    float attackAnimSpeed = 1f;
    float currentGuardDamage;
    bool attackAnimating;
    int swingId;

    float dashUntil = float.NegativeInfinity;
    float dashReadyAt;
    Vector3 dashVelocity;
    float staggerUntil = float.NegativeInfinity;
    float hitstopUntil = float.NegativeInfinity;
    int idleStateHash;

    float currentTilt;
    HeldWeapon wepHeld;
    HeldWeapon wepOffHeld;
    Weapon wepBlade;
    Weapon wepOffBlade;

    Transform visualRoot;
    Vector3 visualRootBase;
    float kneelTarget;
    float kneelCurrent;

    public Stance CurrentStance => stance;
    public Stance AttackHeight => attackStance;
    public AttackType CurrentAttack => currentAttack;
    public float CurrentGuardDamage => currentGuardDamage;
    public int SwingId => swingId;

    public bool IsAttacking => Time.time < attackEnd;                                   // Any part of an attack.
    public bool IsStriking => Time.time >= strikeStart && Time.time < strikeEnd;        // The weapon can hit.
    public bool IsWindingUp => Time.time < strikeStart;
    public bool IsRecovering => Time.time >= strikeEnd && Time.time < attackEnd;
    public bool IsStaggered => Time.time < staggerUntil;
    public bool IsDashing => Time.time < dashUntil;
    public bool CanGuard => enabled && !IsAttacking && !IsStaggered && !IsDashing;
    public bool IsArmed => Wep != null || WepOff != null;
    public Transform Opponent
    {
        get
        {
            if (control == null)
            {
                return null;
            }

            GameObject opponent = control.Player1 == gameObject ? control.Player2 : control.Player1;
            return opponent != null && opponent != gameObject ? opponent.transform : null;
        }
    }

    private void Awake()
    {
        if (Sliceable.IsSpawningPiece)
        {
            // Severed body part copied from a dead fighter: it must not spawn weapons or read input.
            enabled = false;
            Destroy(this);
            return;
        }

        // TPSpawn says which slot it is filling, which matters for prefabs that
        // were saved with neither (or the wrong) P1/P2 box ticked.
        if (TPSpawn.SpawningSlot != 0)
        {
            P1 = TPSpawn.SpawningSlot == 1;
            P2 = TPSpawn.SpawningSlot == 2;
        }

        managerob = GameObject.Find("PlayerManager");
        Play1 = managerob.GetComponent<Player1Data>();
        Play2 = managerob.GetComponent<Player2Data>();
        controls = new PlayerControls();

        // Use this match's picks from the menus rather than whatever is saved on the prefab.
        GameObject pickedWeapon = P1 ? Play1.weaponPrefab : Play2.weaponPrefab;
        if (pickedWeapon != null)
        {
            Weapon = pickedWeapon;
            WeaponOffhand = P1 ? Play1.offhandPrefab : Play2.offhandPrefab;
        }

        RuntimeAnimatorController pickedAnimator = P1 ? Play1.animatorController : Play2.animatorController;
        Animator animator = GetComponent<Animator>();
        if (pickedAnimator != null && animator != null)
        {
            animator.runtimeAnimatorController = pickedAnimator;
        }

        // Weapons are spawned at the hand now and attached in Start, after TPSpawn has
        // placed the fighter, which is when the per-character grip offsets below were tuned.
        if (Weapon != null)
        {
            Wep = Instantiate(Weapon, Weaponpoint1.transform.position, Weaponpoint1.transform.rotation);
        }

        if (WeaponOffhand != null && UsesOffhand(CharacterName, WeaponName))
        {
            WepOff = Instantiate(WeaponOffhand, Weaponpoint2.transform.position, Weaponpoint2.transform.rotation);
        }
    }

    private void Start()
    {
        character = GetComponent<Character>();

        control = GameObject.Find("CameraManager").GetComponent<CameraControl>();

        anim = GetComponent<Animator>();

        rb = GetComponent<Rigidbody>();

        rb.useGravity = true;
        rb.isKinematic = false;
        rb.constraints = LiveMovementConstraints;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        spawnman = managerob.GetComponent<TPSpawn>();
        usingAi = P2 && spawnman != null && spawnman.usingAiForP2;
        if (usingAi && GetComponent<FighterAI>() == null)
        {
            gameObject.AddComponent<FighterAI>();
        }

        stance = startingStance;

        // The body model that sinks into a kneel on wounded legs.
        visualRoot = transform.Find("rootChrSlr");
        if (visualRoot != null)
        {
            visualRootBase = visualRoot.localPosition;
        }

        AttachWeapons();

        // The stance blade angle is applied on top of how each weapon sits in the hand.
        if (Wep != null)
        {
            wepHeld = Wep.GetComponent<HeldWeapon>();
            wepBlade = Wep.GetComponentInChildren<Weapon>();
        }

        if (WepOff != null)
        {
            wepOffHeld = WepOff.GetComponent<HeldWeapon>();
            wepOffBlade = WepOff.GetComponentInChildren<Weapon>();
        }
    }

    string CharacterName => P1 ? Play1.playerCharacter1 : Play2.playerCharacter2;
    string WeaponName => P1 ? Play1.PlayerCharWeapon1 : Play2.PlayerCharWeapon2;

    static bool UsesOffhand(string characterName, string weaponName)
    {
        return weaponName == "ToothPicks" || (characterName == "Tofu" && weaponName == "Cleaver");
    }

    // Grip offsets for each character / weapon pair. Positions were hand-tuned in world space
    // for Player 1 (who spawns facing +X); they're stored that way and rotated into the
    // fighter's own frame, so Player 2 gets the mirrored grip automatically.
    struct Grip
    {
        public Vector3 offset;
        public Vector3 euler;
        public Vector3 scaleAdd;

        public Grip(Vector3 offset, Vector3 euler, Vector3 scaleAdd = default)
        {
            this.offset = offset;
            this.euler = euler;
            this.scaleAdd = scaleAdd;
        }
    }

    static readonly Quaternion GripAuthoringFacing = Quaternion.Euler(0f, 90f, 0f);

    static bool TryGetGrips(string characterName, string weaponName, out Grip main, out Grip off)
    {
        off = new Grip(Vector3.zero, Vector3.zero);

        bool tofu = characterName == "Tofu";
        bool broccoli = characterName == "Broccoli";
        bool seaweed = characterName == "Seaweed";
        // Blueberry has no arm mesh and a round body her short arm bones end up inside, so she
        // holds each weapon at the same angle as the others, moved out just clear of her body.
        bool blueberry = characterName == "BlueBerry";
        Vector3 toothpickEuler = new Vector3(-148f, -80f, -95f);

        switch (weaponName)
        {
            case "Knife":
                if (blueberry) main = new Grip(new Vector3(-0.141f, 0.163f, -0.106f), new Vector3(-55f, 180f, 30f));
                else if (tofu) main = new Grip(new Vector3(-0.05f, 0f, 0.05f), new Vector3(-87f, 150f, 30f), new Vector3(.015f, .25f, .025f));
                else if (broccoli) main = new Grip(Vector3.zero, new Vector3(-80f, 180f, -7f));
                else if (seaweed) main = new Grip(Vector3.zero, new Vector3(-80f, 180f, 30f));
                else main = new Grip(Vector3.zero, new Vector3(-55f, 180f, 30f));
                return true;

            case "Cleaver":
                // The cleaver is built on the knife model (same handle), so it's held like the knife.
                if (blueberry) main = new Grip(new Vector3(-0.141f, 0.163f, -0.106f), new Vector3(-55f, 180f, 30f));
                else if (tofu)
                {
                    main = new Grip(new Vector3(-0.05f, 0f, 0.05f), new Vector3(-87f, 150f, 30f), new Vector3(.04f, .04f, .04f));
                    off = new Grip(new Vector3(-0.05f, 0f, -0.05f), new Vector3(-87f, 160f, 25f), new Vector3(.04f, .04f, .04f));
                }
                else if (broccoli) main = new Grip(Vector3.zero, new Vector3(-80f, 180f, -7f));
                else if (seaweed) main = new Grip(Vector3.zero, new Vector3(-80f, 180f, 30f));
                else main = new Grip(Vector3.zero, new Vector3(-55f, 180f, 30f));
                return true;

            case "Rolling Pin":
                if (blueberry) main = new Grip(new Vector3(-0.056f, 0.619f, -0.086f), new Vector3(0f, 190f, 40f));
                else if (tofu) main = new Grip(new Vector3(.03f, .59f, .05f), new Vector3(0f, 190f, 40f), new Vector3(0f, 0f, 0.1f));
                else main = new Grip(new Vector3(.09f, .45f, 0f), new Vector3(0f, 190f, 40f));
                return true;

            case "Spoon":
                if (blueberry) main = new Grip(new Vector3(-0.218f, 0.064f, -0.172f), new Vector3(-80f, 180f, 0f));
                else if (tofu) main = new Grip(new Vector3(-.03f, 0f, 0f), new Vector3(-85f, 180f, 10f), new Vector3(0f, 0.08f, 0f));
                else main = new Grip(Vector3.zero, new Vector3(-80f, 180f, 0f));
                return true;

            case "Tenderizer":
                if (blueberry) main = new Grip(new Vector3(-0.343f, -0.68f, -0.149f), new Vector3(-148f, -80f, -95f));
                else if (tofu) main = new Grip(new Vector3(-.09f, -.75f, .09f), new Vector3(-148f, -80f, -95f), new Vector3(0f, .0009f, 0f));
                else if (broccoli) main = new Grip(new Vector3(-.09f, -.55f, .09f), new Vector3(-148f, -80f, -95f));
                else main = new Grip(new Vector3(-.09f, -.75f, .09f), new Vector3(-148f, -80f, -95f));
                return true;

            case "ToothPicks":
                if (blueberry)
                {
                    main = new Grip(new Vector3(-0.09f, 0.479f, -0.082f), toothpickEuler);
                    off = new Grip(new Vector3(-0.12f, 0.34f, 0.04f), toothpickEuler);
                }
                else if (tofu)
                {
                    main = new Grip(new Vector3(-0.05f, .34f, 0.05f), new Vector3(-87f, 140f, 40f));
                    off = new Grip(new Vector3(-0.05f, .34f, -0.05f), new Vector3(-87f, 160f, 25f));
                }
                else
                {
                    main = new Grip(new Vector3(0.03f, .34f, -0.01f), toothpickEuler);
                    off = new Grip(new Vector3(-0.05f, .34f, 0.01f), toothpickEuler);
                }
                return true;
        }

        main = new Grip(Vector3.zero, Vector3.zero);
        return false;
    }

    void AttachWeapons()
    {
        TryGetGrips(CharacterName, WeaponName, out Grip main, out Grip off);

        if (Wep != null)
        {
            AttachWeapon(Wep, Weaponpoint1.transform, main);
        }

        if (WepOff != null)
        {
            AttachWeapon(WepOff, Weaponpoint2.transform, off);
        }
    }

    void AttachWeapon(GameObject weaponObject, Transform hand, Grip grip)
    {
        Vector3 worldOffset = transform.rotation * (Quaternion.Inverse(GripAuthoringFacing) * grip.offset);

        weaponObject.transform.SetParent(hand, true);
        weaponObject.transform.position = hand.position + worldOffset;
        weaponObject.transform.Rotate(grip.euler);
        weaponObject.transform.localScale += grip.scaleAdd;

        // Record the grip, then take the weapon out of the hand's hierarchy: from here it copies
        // the hand's position and rotation each frame (see LateUpdate) without its scale.
        HeldWeapon held = weaponObject.AddComponent<HeldWeapon>();
        held.Attach(hand);
        weaponObject.transform.SetParent(null, true);

        // Moved every frame, so give its colliders a kinematic body.
        Rigidbody body = weaponObject.GetComponent<Rigidbody>();
        if (body == null)
        {
            body = weaponObject.AddComponent<Rigidbody>();
        }
        body.isKinematic = true;
        body.useGravity = false;

        // The Weapon script is on the prefab root for some weapons and on a child for others.
        foreach (Weapon weapon in weaponObject.GetComponentsInChildren<Weapon>())
        {
            weapon.Owner = character;
        }
    }

    private void Update()
    {
        if (character != null && character.IsDead)
        {
            moveIntent = Vector3.zero;
            return;
        }

        // The default state the Animator starts in is idle; remember it so a deflected or
        // finished attack can be cut straight back to it.
        if (idleStateHash == 0 && anim != null)
        {
            idleStateHash = anim.GetCurrentAnimatorStateInfo(0).fullPathHash;
        }

        if (P1)
        {
            UpdateHumanControls(1);
        }
        else if (P2 && !usingAi)
        {
            UpdateHumanControls(2);
        }
        // An AI-driven fighter gets its calls from FighterAI instead.

        UpdateAnimation();
    }

    private void UpdateHumanControls(int player)
    {
        if (GameInput.LeftBumper(player))
        {
            TryChangeStance(stance == Stance.Low ? Stance.Mid : Stance.High);
        }
        if (GameInput.RightBumper(player))
        {
            TryChangeStance(stance == Stance.High ? Stance.Mid : Stance.Low);
        }

        if (GameInput.FastAttack(player))
        {
            TryAttack(AttackType.Fast);
        }
        if (GameInput.HeavyAttack(player))
        {
            TryAttack(AttackType.Heavy);
        }
        if (GameInput.StabAttack(player))
        {
            TryAttack(AttackType.Stab);
        }

        // Camera-relative: right on the stick is right on screen, up is into the screen.
        Vector2 input = GameInput.Move(player);
        Vector3 right = control != null ? control.FlatRight : Vector3.right;
        Vector3 forward = control != null ? control.FlatForward : Vector3.forward;
        SetMoveIntent(right * input.x + forward * input.y);

        if (GameInput.Dash(player))
        {
            TryDash(moveIntent);
        }
    }

    // World-space flat direction, magnitude 0..1. Used by human input and FighterAI alike.
    public void SetMoveIntent(Vector3 worldDirection)
    {
        worldDirection.y = 0f;
        moveIntent = Vector3.ClampMagnitude(worldDirection, 1f);
    }

    private void FixedUpdate()
    {
        if (rb == null || rb.isKinematic || (character != null && character.IsDead))
        {
            return;
        }

        Transform opponent = Opponent;
        Vector3 toOpponent = opponent != null ? opponent.position - rb.position : transform.forward;
        toOpponent.y = 0f;
        float distance = toOpponent.magnitude;
        Vector3 facing = distance > 0.01f ? toOpponent / distance : transform.forward;

        Vector3 targetVelocity;
        float turnSharpness = faceOpponentSharpness;

        if (Time.time < hitstopUntil)
        {
            targetVelocity = Vector3.zero;      // Frozen on impact.
        }
        else if (IsDashing)
        {
            targetVelocity = dashVelocity;
        }
        else if (IsStaggered)
        {
            targetVelocity = Vector3.zero;
            turnSharpness = committedTurnSharpness;
        }
        else if (IsAttacking)
        {
            // Committed: the stick does nothing. Step in through wind-up and swing, then stop.
            bool stepping = Time.time < strikeEnd && distance > lungeStopDistance;
            targetVelocity = stepping ? facing * lungeSpeed : Vector3.zero;
            turnSharpness = IsWindingUp ? startupTurnSharpness : committedTurnSharpness;
        }
        else
        {
            // Split the intent into toward/away and around the opponent so each has its own speed.
            float along = Vector3.Dot(moveIntent, facing);
            Vector3 sideways = moveIntent - facing * along;
            float alongSpeed = along >= 0f ? movementSpeed : backSpeed;
            float stanceSpeed = stance == Stance.High ? highStanceSpeed : stance == Stance.Low ? lowStanceSpeed : 1f;
            targetVelocity = (facing * along * alongSpeed + sideways * strafeSpeed) * walkSpeedScale * stanceSpeed * speedMultiplier;
        }

        targetVelocity += knockbackVelocity;
        knockbackVelocity = Vector3.MoveTowards(knockbackVelocity, Vector3.zero, 12f * Time.fixedDeltaTime);

        Vector3 velocity = rb.linearVelocity;
        Vector3 flatVelocity = new Vector3(velocity.x, 0f, velocity.z);
        float maxChange = IsDashing ? float.MaxValue : acceleration * Time.fixedDeltaTime + knockbackVelocity.magnitude;
        flatVelocity = Vector3.MoveTowards(flatVelocity, targetVelocity, maxChange);
        rb.linearVelocity = new Vector3(flatVelocity.x, velocity.y, flatVelocity.z);

        Quaternion targetRotation = Quaternion.LookRotation(facing, Vector3.up);
        float turnStep = 1f - Mathf.Exp(-turnSharpness * Time.fixedDeltaTime);
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRotation, turnStep));
    }

    private void LateUpdate()
    {
        if (character != null && character.IsDead)
        {
            return;
        }

        // Wounded legs drop the fighter into a kneel (first, so the weapons follow the lowered hands).
        if (visualRoot != null)
        {
            kneelCurrent = Mathf.MoveTowards(kneelCurrent, kneelTarget, 1.5f * Time.deltaTime);
            float parentScale = Mathf.Max(Mathf.Abs(transform.lossyScale.y), 0.0001f);
            visualRoot.localPosition = visualRootBase + Vector3.down * (kneelCurrent / parentScale);
        }

        // Weapons follow the hands (after the Animator has posed them), then tip for the stance.
        float targetTilt = stance == Stance.High ? highBladeTilt : stance == Stance.Low ? lowBladeTilt : 0f;
        currentTilt = Mathf.Lerp(currentTilt, targetTilt, 1f - Mathf.Exp(-stanceTiltSharpness * Time.deltaTime));
        TiltWeapon(Wep, wepHeld, wepBlade);
        TiltWeapon(WepOff, wepOffHeld, wepOffBlade);
    }

    void TiltWeapon(GameObject weaponObject, HeldWeapon held, Weapon blade)
    {
        if (weaponObject == null || held == null)
        {
            return;
        }

        held.FollowGrip();
        if (Mathf.Abs(currentTilt) < 0.1f || blade == null)
        {
            return;
        }

        Vector3 along = blade.EdgeTipWorld - blade.EdgeBaseWorld;
        if (along.sqrMagnitude < 0.000001f)
        {
            return;
        }

        // Rotate the blade toward straight down (positive tilt) or up (negative), about the grip,
        // without swinging it past vertical.
        along.Normalize();
        Vector3 toward = currentTilt > 0f ? Vector3.down : Vector3.up;
        Vector3 axis = Vector3.Cross(along, toward);
        if (axis.sqrMagnitude < 0.0001f)
        {
            return;
        }

        float room = Mathf.Max(0f, Vector3.Angle(along, toward) - 10f);
        float angle = Mathf.Min(Mathf.Abs(currentTilt), room);
        weaponObject.transform.rotation = Quaternion.AngleAxis(angle, axis.normalized) * weaponObject.transform.rotation;
    }

    // Root motion is ignored (the Rigidbody owns movement; attacks step in through FixedUpdate).
    // Having this method stops the Animator from applying root motion itself.
    private void OnAnimatorMove()
    {
    }

    private void UpdateAnimation()
    {
        if (anim == null)
        {
            return;
        }

        bool moving = !IsAttacking && !IsStaggered && moveIntent.sqrMagnitude > 0.01f;
        anim.SetInteger(P1 ? "Condition" : "Condition2", moving ? 1 : 0);

        // Once the follow-through ends, cut the swing animation back to idle so what you see
        // matches when you can act again.
        if (attackAnimating && !IsAttacking)
        {
            attackAnimating = false;
            ReturnToIdle(0.12f);
        }

        if (Time.time < hitstopUntil)
        {
            anim.speed = 0f;
        }
        else
        {
            anim.speed = IsAttacking ? attackAnimSpeed * attackSpeedScale : 1f;
        }
    }

    void ReturnToIdle(float blend)
    {
        if (anim == null)
        {
            return;
        }

        anim.ResetTrigger(P1 ? "FastAtt" : "FastAtt2");
        anim.ResetTrigger(P1 ? "StrongAtt" : "StrongAtt2");
        anim.ResetTrigger(P1 ? "StabbyStab" : "StabbyStab2");
        if (idleStateHash != 0)
        {
            anim.CrossFadeInFixedTime(idleStateHash, blend);
        }
    }

    public void SetSpeedMultiplier(float multiplier)
    {
        speedMultiplier = Mathf.Clamp(multiplier, 0.1f, 1f);
    }

    public void SetAttackSpeedScale(float scale)
    {
        attackSpeedScale = Mathf.Clamp(scale, 0.3f, 1f);
    }

    // World metres to sink the body (a kneel on wounded legs).
    public void SetKneel(float drop)
    {
        kneelTarget = Mathf.Max(0f, drop);
    }

    public void ApplyHitReaction(Vector3 knockback, float stunTime)
    {
        knockbackVelocity += knockback;
        if (stunTime > 0f)
        {
            staggerUntil = Mathf.Max(staggerUntil, Time.time + stunTime);
        }
    }

    // The attack bounced off a guard or another blade: it ends now and the fighter reels.
    public void Deflect(float stagger, Vector3 knockback)
    {
        if (IsAttacking)
        {
            strikeStart = Mathf.Min(strikeStart, Time.time);
            strikeEnd = Time.time;
            attackEnd = Time.time;
            attackAnimating = false;
        }

        dashUntil = float.NegativeInfinity;
        ApplyHitReaction(knockback, stagger);
        ReturnToIdle(0.08f);
    }

    // Freeze the animation for a few frames so impacts land with weight.
    public void Hitstop(float duration)
    {
        float now = Time.time;
        float until = now + duration;
        float added = until - Mathf.Max(hitstopUntil, now);
        if (added <= 0f)
        {
            return;
        }

        // The swing's clock pauses too, so the frozen frames don't eat into it.
        if (IsAttacking)
        {
            if (strikeStart > now) strikeStart += added;
            strikeEnd += added;
            attackEnd += added;
        }

        hitstopUntil = until;
    }

    private void OnEnable()
    {
        if (controls != null)
        {
            controls.Player.Enable();
        }
    }

    private void OnDisable()
    {
        if (controls != null)
        {
            controls.Player.Disable();
        }

        moveIntent = Vector3.zero;
    }

    public bool TryChangeStance(Stance newStance)
    {
        if (!enabled || newStance == stance || IsAttacking || IsStaggered || Time.time < stanceReadyAt)
        {
            return false;
        }

        stance = newStance;
        stanceReadyAt = Time.time + stanceChangeTime;
        return true;
    }

    // A quick step in the given direction (straight back from the opponent if none). Leaves
    // the fighter unguarded for its duration.
    public bool TryDash(Vector3 direction)
    {
        if (!enabled || IsAttacking || IsStaggered || IsDashing || Time.time < dashReadyAt)
        {
            return false;
        }

        if (character != null && !character.TrySpendStamina(dashStaminaCost, true))
        {
            return false;
        }

        direction.y = 0f;
        if (direction.sqrMagnitude < 0.04f)
        {
            Transform opponent = Opponent;
            direction = opponent != null ? transform.position - opponent.position : -transform.forward;
            direction.y = 0f;
        }

        dashVelocity = direction.normalized * dashSpeed * Mathf.Max(speedMultiplier, 0.35f);
        dashUntil = Time.time + dashTime;
        dashReadyAt = dashUntil + dashCooldown;
        return true;
    }

    // Starts an attack if the fighter is free to (not mid-attack, reeling or dashing).
    public bool TryAttack(AttackType type)
    {
        if (!enabled || anim == null || (character != null && character.IsDead))
        {
            return false;
        }

        if (IsAttacking || IsStaggered || IsDashing || Time.time < stanceReadyAt)
        {
            return false;
        }

        AttackTiming timing = type == AttackType.Fast ? fastAttack : type == AttackType.Heavy ? heavyAttack : stabAttack;
        float startupScale = stance == Stance.High ? highStartupScale : stance == Stance.Low ? lowStartupScale : 1f;
        float guardScale = stance == Stance.High ? highGuardDamageScale : stance == Stance.Low ? lowGuardDamageScale : 1f;

        // A wounded weapon arm makes everything slower.
        float startup = timing.startup * startupScale / attackSpeedScale;
        float active = timing.active / attackSpeedScale;
        float recovery = timing.recovery / attackSpeedScale;

        float now = Time.time;
        strikeStart = now + startup;
        strikeEnd = strikeStart + active;
        attackEnd = strikeEnd + recovery;
        lungeSpeed = timing.lunge / Mathf.Max(startup + active, 0.01f);
        attackAnimSpeed = timing.animSpeed;
        currentGuardDamage = timing.guardDamage * guardScale;
        currentAttack = type;
        attackStance = stance;
        attackAnimating = true;
        swingId++;

        if (character != null)
        {
            character.TrySpendStamina(timing.staminaCost, false);
        }

        // One slice id for the whole swing (0 means "no id" to the slicer, so skip it).
        int sliceId = Random.Range(1, int.MaxValue);
        BeginSwing(Wep, sliceId);
        BeginSwing(WepOff, sliceId);

        switch (type)
        {
            case AttackType.Fast:
                anim.SetTrigger(P1 ? "FastAtt" : "FastAtt2");
                break;
            case AttackType.Heavy:
                anim.SetTrigger(P1 ? "StrongAtt" : "StrongAtt2");
                break;
            default:
                anim.SetTrigger(P1 ? "StabbyStab" : "StabbyStab2");
                break;
        }

        return true;
    }

    static void BeginSwing(GameObject weaponObject, int sliceId)
    {
        if (weaponObject == null)
        {
            return;
        }

        foreach (Weapon weapon in weaponObject.GetComponentsInChildren<Weapon>())
        {
            weapon.BeginSwing(sliceId);
        }
    }

    // Kept for anything still wired to the old per-player methods (UI events, animation events).
    public void StabAttack() { TryAttack(AttackType.Stab); }
    public void StabAttack2() { TryAttack(AttackType.Stab); }
    public void FastAttack() { TryAttack(AttackType.Fast); }
    public void FastAttack2() { TryAttack(AttackType.Fast); }
    public void HeavyAttack() { TryAttack(AttackType.Heavy); }
    public void HeavyAttack2() { TryAttack(AttackType.Heavy); }

    private void OnMove(InputValue value)
    {
        Moveet = value.Get<Vector2>();
    }
}
