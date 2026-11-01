using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using BzKovSoft.ObjectSlicerSamples;
using DestroyIt;
using TMPro;

// Bushido Blade rules: no health bar. A clean hit to the head or body kills; a clean hit to an
// arm or leg wounds it (or cuts it off). Facing an attack while guarding blocks it, which costs
// stamina; a fighter is open while attacking, reeling, dashing, out of stamina, hit from the
// side or back, or hit at a height their stance doesn't cover.
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Sliceable))]
[RequireComponent(typeof(Destructible))]
[RequireComponent(typeof(AdderSliceableAsync))]
public class Character : MonoBehaviour
{
    // What a weapon reports when it reaches a fighter.
    public struct Strike
    {
        public Character attacker;
        public Weapon weapon;
        public Collider collider;       // What was struck: a bone, the movement capsule, or a weapon.
        public Vector3 point;
        public Vector3 direction;       // Which way the weapon was travelling.
        public Plane cutPlane;          // Plane of the cutting edge at the contact.
        public int swingId;
        public int sliceId;
        public CharacterControl.Stance height;
        public float guardDamage;
    }

    public enum StrikeResult { Ignored, Blocked, Clashed, Hit }

    [Header("Movement")]

    [SerializeField]
    Rigidbody rb;                           // Character's Rigidbody.

    [SerializeField]
    CharacterControl cc;                    // Paul and Nick's Character script

    public CameraControl control;           // Paul and Nick's Camera script

    GameObject managerob;
    public Player1Data Play1;
    public Player2Data Play2;

    [Range(100f, 1000f)]
    public int jumpForce;                   // Character's jumping force.

    public bool isRunning;                  // Bool to determine if they're running or not.

    public Slider p1healthsilder;
    public Slider p1shieldslider;
    public Slider p2healthsilder;
    public Slider p2shieldslider;

    public GameObject Char1Nameob;
    public GameObject Char2Nameob;

    public TextMeshProUGUI Char1Name;
    public TextMeshProUGUI Char2Name;

    /**************************/

    [Header("Character Statistics")]
    public int Build;                       // Determines how much blood the character has. Based upon their physical size.

    [Tooltip("Blood (the health bar). Clean hits kill outright; blood only drains from severed limbs.")]
    [Range(0, 200)]
    public float playerHealth;

    public int playerMaxHealth;

    [Tooltip("Stamina (the guard bar). Guarding needs it; blocking, attacking and dashing spend it.")]
    [Range(0, 100)]
    public float playerGuard;
    public float playerMaxGuard;

    [Header("Stamina")]
    [SerializeField] float staminaRegenPerSecond = 22f;
    [SerializeField] float staminaRegenDelay = 0.7f;
    [Tooltip("Fraction of stamina needed before a broken guard comes back.")]
    [SerializeField, Range(0f, 1f)] float guardRecoverFraction = 0.3f;

    [Header("Guard")]
    [Tooltip("Attacks from within this many degrees of where the fighter faces can be blocked.")]
    [SerializeField] float guardArc = 75f;
    [Tooltip("Stamina cost multiplier for blocking at a height the stance only partly covers.")]
    [SerializeField] float offHeightBlockCost = 1.5f;
    [SerializeField] float bludgeonGuardDamage = 1.6f;
    [SerializeField] float pierceGuardDamage = 0.8f;
    [SerializeField] float deflectStagger = 0.35f;
    [SerializeField] float guardBreakStagger = 0.65f;
    [SerializeField] float clashStagger = 0.3f;
    [SerializeField] float blockPushback = 1.5f;

    [Header("Impact")]
    [SerializeField] float blockHitstop = 0.06f;
    [SerializeField] float hitHitstop = 0.1f;
    [SerializeField] float woundStagger = 0.3f;
    [Tooltip("Slow the game down for a moment when someone dies.")]
    [SerializeField] bool killSlowMotion = true;

    [Header("Wounds")]
    [SerializeField, Range(0.3f, 1f)] float woundedArmAttackSpeed = 0.75f;
    [SerializeField] float woundedArmBlockCost = 1.5f;
    [Tooltip("Speed multiplier for each wounded or lost leg.")]
    [SerializeField, Range(0.1f, 1f)] float woundedLegSpeed = 0.45f;
    [Tooltip("How far a wounded leg drops the fighter into a kneel, as a fraction of their height (a second leg drops them further).")]
    [SerializeField, Range(0f, 0.3f)] float kneelDrop = 0.1f;
    [Tooltip("Never sink further than this (metres), however tall the fighter.")]
    [SerializeField] float maxKneelDrop = 0.35f;

    [Header("Limb Loss")]
    [Tooltip("A clean slash to an arm or leg cuts it off when the cut can be made cleanly; otherwise the limb is just wounded.")]
    [SerializeField] bool liveLimbLoss = true;
    [SerializeField] float bleedPerSecondPerLimb = 1.5f;
    [SerializeField] float severedLimbFling = 3f;

    [Header("Deaths")]
    [Tooltip("Blunt kills: rounds of cuts that burst the body (each round splits every piece again).")]
    [SerializeField, Range(1, 3)] int burstRounds = 3;
    [SerializeField] float burstForce = 7f;
    [Tooltip("Extra holes punched through the body when a piercing weapon kills.")]
    [SerializeField] int killHoles = 4;
    [SerializeField] float holeRadius = 0.06f;

    [Header("Ring Out")]
    [Tooltip("Falling this far below where the fighter spawned counts as a knockout.")]
    [SerializeField] float fallOutDepth = 8f;

    float nextStaminaRegen;
    bool guardBroken;
    bool isDead;
    bool isP1;
    float spawnHeight;
    AdderSliceableAsync sliceableInstaller;
    Sliceable sliceable;
    BodyHoles holes;

    // Each swing can land at most once.
    int lastSwingThatHitMe = -1;
    Character lastAttacker;

    int limbsSevered;
    int woundedLegs;
    bool weaponArmWounded;
    readonly HashSet<Collider> woundedLimbs = new HashSet<Collider>();

    TextMeshProUGUI nameLabel;
    CharacterControl.Stance shownStance;
    bool stanceShown;

    static bool slowMotionActive;
    static float normalFixedDeltaTime = 0.02f;

    /**************************/

    [Header("External Objects")]
    [SerializeField]
    bool isTwoHanded;                       // Determines which animations will be used (one-handed or two-handed).
    public Animator anim;
    public GameObject platform;
    public Sprite characterWinPanel;
    public Sprite characterDeathPanel;

    /**************************/

    [Header("Hit Effects")]
    public GameObject guardHit;             // Effect when an attack is blocked.
    public GameObject healthHit;            // Effect when an attack lands.
    GameObject bleedParticles;

    InGameUI g;

    public bool IsDead => isDead;
    public bool IsAttacking => !isDead && cc != null && cc.IsAttacking;
    public bool IsStriking => !isDead && cc != null && cc.enabled && cc.IsStriking;
    public int SwingId => cc != null ? cc.SwingId : 0;
    public CharacterControl Control => cc;
    public bool IsGuarding => !isDead && cc != null && cc.CanGuard && !guardBroken && playerGuard > 0f;
    public bool IsGuardBroken => guardBroken;

    void Awake()
    {
        if (Sliceable.IsSpawningPiece)
        {
            // This is a severed body part copied from a fighter, not a fighter.
            enabled = false;
            Destroy(this);
            return;
        }

        managerob = GameObject.Find("PlayerManager");
        Play1 = GameObject.Find("PlayerManager").GetComponent<Player1Data>();
        Play2 = GameObject.Find("PlayerManager").GetComponent<Player2Data>();

        // Stored Statistic Variables
        Build = Mathf.CeilToInt(gameObject.transform.localScale.magnitude);     // How heavy/big the character is.
        playerMaxHealth = 50 + (Build * 25);                                    // Blood, based upon Build.
        playerHealth = playerMaxHealth;
        playerMaxGuard = 100;
        playerGuard = 100;                                                      // Stamina is always out of 100.

        // Stored Movement Variables
        rb = GetComponent<Rigidbody>();                                         // DO. NOT. DELETE.

        sliceable = GetComponent<Sliceable>();
        sliceableInstaller = GetComponent<AdderSliceableAsync>();
        sliceableInstaller.enabled = false;

        bleedParticles = sliceable._bloodPrefub;
        anim = GetComponent<Animator>();
        cc = GetComponent<CharacterControl>();
        isP1 = TPSpawn.SpawningSlot != 0 ? TPSpawn.SpawningSlot == 1 : cc != null && cc.P1;

        // Set up Destructible
        gameObject.GetComponent<Destructible>().totalHitPoints = playerMaxHealth;
        gameObject.GetComponent<Destructible>().currentHitPoints = playerMaxHealth;
    }

    void Start()
    {
        g = GameObject.Find("InGameUI").GetComponent<InGameUI>();
        spawnHeight = transform.position.y;

        if (isP1)
        {
            Char1Nameob = g.Char1Nameob;
            Char1Name = g.Char1Nameob.GetComponent<TextMeshProUGUI>();
            p1healthsilder = g.p1healthsilder;
            p1shieldslider = g.p1shieldslider;

            Char1Name.text = Play1.playerCharacter1;
            p1healthsilder.maxValue = playerMaxHealth;
            p1shieldslider.maxValue = playerMaxGuard;
            nameLabel = Char1Name;
        }
        else
        {
            Char2Nameob = g.Char2Nameob;
            Char2Name = g.Char2Nameob.GetComponent<TextMeshProUGUI>();
            p2healthsilder = g.p2healthsilder;
            p2shieldslider = g.p2shieldslider;

            Char2Name.text = Play2.playerCharacter2;
            p2healthsilder.maxValue = playerMaxHealth;
            p2shieldslider.maxValue = playerMaxGuard;
            nameLabel = Char2Name;
        }

        if (isP1)
        {
            gameObject.AddComponent<Player1Object>();
            if (platform != null) platform.AddComponent<Player1Object>();
            if (cc.Wep != null) cc.Wep.AddComponent<Player1Object>();
            if (cc.WepOff != null) cc.WepOff.AddComponent<Player1Object>();
        }
        else
        {
            gameObject.AddComponent<Player2Object>();
            if (platform != null) platform.AddComponent<Player2Object>();
            if (cc.Wep != null) cc.Wep.AddComponent<Player2Object>();
            if (cc.WepOff != null) cc.WepOff.AddComponent<Player2Object>();
        }
    }

    void Update()
    {
        if (isP1)
        {
            p1healthsilder.value = playerHealth;
            p1shieldslider.value = playerGuard;
        }
        else
        {
            p2healthsilder.value = playerHealth;
            p2shieldslider.value = playerGuard;
        }

        Cursor.lockState = CursorLockMode.Locked;

        if (isDead)
        {
            playerHealth = 0;
            playerGuard = 0;
            return;
        }

        ShowStance();

        if (transform.position.y < spawnHeight - fallOutDepth)
        {
            // Fell off the stage.
            playerHealth = 0;
        }

        if (limbsSevered > 0)
        {
            playerHealth -= bleedPerSecondPerLimb * limbsSevered * Time.deltaTime;
        }

        if (playerHealth <= 0)
        {
            Die(null, transform.position, Vector3.zero);
            return;
        }

        if (Time.time >= nextStaminaRegen && !(cc != null && cc.IsAttacking))
        {
            playerGuard += staminaRegenPerSecond * Time.deltaTime;
        }

        playerGuard = Mathf.Clamp(playerGuard, 0f, playerMaxGuard);
        if (guardBroken && playerGuard >= playerMaxGuard * guardRecoverFraction)
        {
            guardBroken = false;
        }
    }

    // Shows the current stance next to the fighter's name.
    void ShowStance()
    {
        if (cc == null || nameLabel == null || (stanceShown && shownStance == cc.CurrentStance))
        {
            return;
        }

        shownStance = cc.CurrentStance;
        stanceShown = true;
        string fighterName = isP1 ? Play1.playerCharacter1 : Play2.playerCharacter2;
        nameLabel.text = fighterName + "  <size=70%>" + shownStance.ToString().ToUpperInvariant() + "</size>";
    }

    private void LateUpdate()
    {
        ErrorChecker();     // Error checker makes sure that nothing is missing or going wrong. Displays errors.
    }

    void OnDestroy()
    {
        EndSlowMotion();
    }

    // Spends stamina on an action. With requireFull the action is refused if there isn't enough.
    public bool TrySpendStamina(float cost, bool requireFull)
    {
        if (isDead || (requireFull && playerGuard < cost))
        {
            return false;
        }

        playerGuard = Mathf.Max(0f, playerGuard - cost);
        nextStaminaRegen = Time.time + staminaRegenDelay;
        return true;
    }

    /**************************/
    // Being struck

    // An attacker's weapon reached this fighter's body.
    public StrikeResult ReceiveStrike(Strike strike)
    {
        if (!TakeSwing(strike))
        {
            return StrikeResult.Ignored;
        }

        if (IsGuarding && IsInFront(strike.attacker) && Covers(cc.CurrentStance, strike.height, out float costScale))
        {
            Block(strike, costScale);
            return StrikeResult.Blocked;
        }

        TakeCleanHit(strike);
        return StrikeResult.Hit;
    }

    // An attacker's weapon met one of this fighter's weapons.
    public StrikeResult ReceiveBladeContact(Strike strike)
    {
        if (isDead || strike.attacker == null || strike.attacker == this || AlreadyTook(strike))
        {
            return StrikeResult.Ignored;
        }

        if (cc != null && cc.IsStriking)
        {
            TakeSwing(strike);
            Clash(strike);
            return StrikeResult.Clashed;
        }

        if (IsGuarding)
        {
            // The weapon is physically in the way, so it stops the blow at any height.
            TakeSwing(strike);
            Block(strike, 1f);
            return StrikeResult.Blocked;
        }

        return StrikeResult.Ignored;
    }

    bool AlreadyTook(Strike strike)
    {
        return strike.attacker == lastAttacker && strike.swingId == lastSwingThatHitMe;
    }

    // Records the swing; false if it already landed (one result per swing).
    bool TakeSwing(Strike strike)
    {
        if (isDead || strike.attacker == null || strike.attacker == this || AlreadyTook(strike))
        {
            return false;
        }

        lastAttacker = strike.attacker;
        lastSwingThatHitMe = strike.swingId;
        return true;
    }

    bool IsInFront(Character attacker)
    {
        Vector3 toAttacker = attacker.transform.position - transform.position;
        toAttacker.y = 0f;
        Vector3 forward = transform.forward;
        forward.y = 0f;
        return toAttacker.sqrMagnitude < 0.0001f || Vector3.Angle(forward, toAttacker) <= guardArc;
    }

    // Which attack heights a stance guards. Matching heights block cheaply; Mid guards
    // everything at a price; High and Low leave the opposite extreme open.
    bool Covers(CharacterControl.Stance stance, CharacterControl.Stance attackHeight, out float costScale)
    {
        costScale = 1f;
        if (stance == attackHeight)
        {
            return true;
        }

        if (stance == CharacterControl.Stance.Mid || attackHeight == CharacterControl.Stance.Mid)
        {
            costScale = offHeightBlockCost;
            return true;
        }

        return false;
    }

    float WeaponGuardScale(Weapon weapon)
    {
        if (weapon == null)
        {
            return 1f;
        }

        if (weapon.weaponType == BzKnife.WeaponType.Bludgeon)
        {
            return bludgeonGuardDamage;
        }

        return weapon.weaponType == BzKnife.WeaponType.Pierce ? pierceGuardDamage : 1f;
    }

    void Block(Strike strike, float costScale)
    {
        float cost = strike.guardDamage * WeaponGuardScale(strike.weapon) * costScale * (weaponArmWounded ? woundedArmBlockCost : 1f);
        playerGuard = Mathf.Max(0f, playerGuard - cost);
        nextStaminaRegen = Time.time + staminaRegenDelay;

        SpawnEffect(guardHit, strike.point);
        Vector3 away = AwayFrom(strike.attacker);

        cc.Hitstop(blockHitstop);
        if (strike.attacker.Control != null)
        {
            strike.attacker.Control.Hitstop(blockHitstop);
            strike.attacker.Control.Deflect(deflectStagger, -away * blockPushback);
        }

        if (playerGuard <= 0f)
        {
            // Guard broken: wide open until stamina comes back.
            guardBroken = true;
            cc.Deflect(guardBreakStagger, away * blockPushback * 1.5f);
        }
        else
        {
            cc.ApplyHitReaction(away * blockPushback * 0.6f, 0f);
        }
    }

    void Clash(Strike strike)
    {
        SpawnEffect(guardHit, strike.point);
        Vector3 away = AwayFrom(strike.attacker);

        cc.Hitstop(blockHitstop);
        cc.Deflect(clashStagger, away * blockPushback);
        if (strike.attacker.Control != null)
        {
            strike.attacker.Control.Hitstop(blockHitstop);
            strike.attacker.Control.Deflect(clashStagger, -away * blockPushback);
        }
    }

    void TakeCleanHit(Strike strike)
    {
        Sliceable.BodyZone zone = sliceable.ClassifyHit(strike.collider, strike.point, strike.height, out Collider limb);
        BzKnife.WeaponType type = strike.weapon != null ? strike.weapon.weaponType : BzKnife.WeaponType.Slash;

        SpawnEffect(healthHit, strike.point);
        cc.Hitstop(hitHitstop);
        if (strike.attacker.Control != null)
        {
            strike.attacker.Control.Hitstop(hitHitstop);
        }

        if (type == BzKnife.WeaponType.Pierce)
        {
            Holes.Punch(strike.point, strike.direction, strike.collider, holeRadius);
        }

        if (zone == Sliceable.BodyZone.Vital)
        {
            Die(strike.weapon, strike.point, strike.direction);
            return;
        }

        // An arm or a leg.
        bool severed = false;
        if (type == BzKnife.WeaponType.Slash && liveLimbLoss && limb != null)
        {
            // Cut through the bone that was struck, or through the limb's middle if the blade only
            // reached the body's outline. The angle always comes from the blade.
            bool struckLimbBone = strike.collider != limb && sliceable.IsInLimb(strike.collider, limb);
            Collider cutBone = struckLimbBone || strike.collider == limb ? strike.collider : limb;
            Plane plane = cutBone == strike.collider ? strike.cutPlane : new Plane(strike.cutPlane.normal, cutBone.bounds.center);
            severed = TryLoseLimb(plane, strike.sliceId, cutBone, strike.direction);
        }

        WoundLimb(zone, limb, severed);

        // A wound interrupts whatever the fighter was doing, including their own swing.
        Vector3 knockback = AwayFrom(strike.attacker) * 1.2f;
        if (cc.IsAttacking)
        {
            cc.Deflect(woundStagger, knockback);
        }
        else
        {
            cc.ApplyHitReaction(knockback, woundStagger);
        }
    }

    void WoundLimb(Sliceable.BodyZone zone, Collider limb, bool severed)
    {
        if (limb != null && !woundedLimbs.Add(limb))
        {
            return;     // Already wounded; a limb only counts once.
        }

        if (zone == Sliceable.BodyZone.Leg)
        {
            woundedLegs = Mathf.Min(2, woundedLegs + 1);
            cc.SetSpeedMultiplier(Mathf.Pow(woundedLegSpeed, woundedLegs));
            cc.SetKneel(Mathf.Min(FighterHeight() * kneelDrop * (woundedLegs >= 2 ? 1.6f : 1f), maxKneelDrop));
        }
        else if (zone == Sliceable.BodyZone.Arm && !severed && limb != null && HoldsWeapon(limb))
        {
            weaponArmWounded = true;
            cc.SetAttackSpeedScale(woundedArmAttackSpeed);
        }
    }

    bool HoldsWeapon(Collider limb)
    {
        return (cc.Wep != null && sliceable.LimbHolds(limb, cc.Wep.transform))
            || (cc.WepOff != null && sliceable.LimbHolds(limb, cc.WepOff.transform));
    }

    float FighterHeight()
    {
        CapsuleCollider capsule = platform != null ? platform.GetComponent<CapsuleCollider>() : null;
        return capsule != null ? capsule.height * Mathf.Abs(capsule.transform.lossyScale.y) : 2f;
    }

    Vector3 AwayFrom(Character attacker)
    {
        Vector3 away = transform.position - attacker.transform.position;
        away.y = 0f;
        return away.sqrMagnitude > 0.0001f ? away.normalized : -transform.forward;
    }

    void SpawnEffect(GameObject effect, Vector3 point)
    {
        if (effect != null)
        {
            Instantiate(effect, point, Quaternion.identity, gameObject.transform.parent);
        }
    }

    BodyHoles Holes
    {
        get
        {
            if (holes == null)
            {
                holes = GetComponent<BodyHoles>();
                if (holes == null)
                {
                    holes = gameObject.AddComponent<BodyHoles>();
                }
            }

            return holes;
        }
    }

    /**************************/
    // Death

    void Die(Weapon killer, Vector3 point, Vector3 direction)
    {
        if (isDead)
        {
            return;
        }

        isDead = true;
        playerHealth = 0;
        playerGuard = 0;

        // Stop every "live fighter" script. They're disabled rather than removed so the
        // match reset can still find this fighter's weapons, and so a severed copy of
        // this body starts with them already off.
        if (cc != null)
        {
            cc.enabled = false;
        }

        FighterAI ai = GetComponent<FighterAI>();
        if (ai != null)
        {
            ai.enabled = false;
        }

        PlayerInput playerInput = GetComponent<PlayerInput>();
        if (playerInput != null)
        {
            playerInput.enabled = false;
        }

        sliceable.MarkDead();

        // The weapons fall from the dead fighter's hands.
        if (cc != null)
        {
            Vector3 dropVelocity = rb != null ? rb.linearVelocity : Vector3.zero;
            HeldWeapon.Drop(cc.Wep, dropVelocity, gameObject);
            HeldWeapon.Drop(cc.WepOff, dropVelocity, gameObject);
        }

        Vector3 velocity = anim != null ? anim.velocity : Vector3.zero;
        Vector3 angularVelocity = anim != null ? anim.angularVelocity : Vector3.zero;

        // Switch the Animator off now: ConvertToRagdoll destroys it, but only at the end of
        // the frame, and a body cut before then would hand a live Animator to the severed half.
        if (anim != null)
        {
            anim.enabled = false;
        }

        sliceable.ConvertToRagdoll(gameObject, velocity, angularVelocity, null);

        if (sliceable.HasRagdollBones)
        {
            // The capsule on "Platform" is the living fighter's body for movement; the ragdoll
            // bones take over now. Left on, it would be an invisible pillar where they fell.
            if (platform != null)
            {
                foreach (Collider bodyCollider in platform.GetComponents<Collider>())
                {
                    bodyCollider.enabled = false;
                    Destroy(bodyCollider);
                }
            }

            // With that capsule gone the root has no collider, so if it stayed dynamic it would
            // fall forever and drag the limbs through the floor. Pin it; the bones carry the body.
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.isKinematic = true;
            rb.useGravity = false;
        }
        else
        {
            // Rigs without ragdoll bones (some Chili Pepper prefabs) just topple over as one piece.
            rb.constraints = RigidbodyConstraints.None;
        }

        if (killer != null)
        {
            switch (killer.weaponType)
            {
                case BzKnife.WeaponType.Slash:
                    // Cut along the edge that landed the blow.
                    killer.CutAlongEdge(sliceable);
                    break;

                case BzKnife.WeaponType.Bludgeon:
                    // Burst into pieces.
                    sliceable.Burst(point, direction, burstRounds, burstForce);
                    break;

                case BzKnife.WeaponType.Pierce:
                    // Riddled: a few more holes straight through the body.
                    foreach (Collider vital in sliceable.RandomVitals(killHoles))
                    {
                        Vector3 center = vital.bounds.center;
                        Vector3 from = center + Random.onUnitSphere * vital.bounds.extents.magnitude;
                        Holes.Punch(vital.ClosestPoint(from), center - from, vital, holeRadius);
                    }
                    break;
            }
        }

        if (killSlowMotion)
        {
            StartCoroutine(SlowMotion());
        }
    }

    IEnumerator SlowMotion()
    {
        if (!slowMotionActive)
        {
            normalFixedDeltaTime = Time.fixedDeltaTime;
        }

        // Physics steps shrink with time, so ragdolls stay smooth instead of stepping at a crawl.
        slowMotionActive = true;
        Time.timeScale = 0.35f;
        Time.fixedDeltaTime = normalFixedDeltaTime * 0.35f;
        yield return new WaitForSecondsRealtime(0.5f);
        EndSlowMotion();
    }

    static void EndSlowMotion()
    {
        if (!slowMotionActive)
        {
            return;
        }

        slowMotionActive = false;
        Time.timeScale = 1f;
        Time.fixedDeltaTime = normalFixedDeltaTime;
    }

    // Cuts off the limb the plane crosses. Returns true if it came off.
    public bool TryLoseLimb(Plane cutPlane, int sliceId, Collider hitBone, Vector3 sweepDirection)
    {
        if (!liveLimbLoss || isDead)
        {
            return false;
        }

        Vector3 fling = sweepDirection * severedLimbFling + Vector3.up * severedLimbFling * 0.5f;
        if (!sliceable.TryCutLimb(cutPlane, sliceId, hitBone, fling, out Sliceable.LimbCut cut))
        {
            return false;
        }

        limbsSevered++;

        if (cc != null)
        {
            // A weapon gripped by the severed hand flies off with it as a loose prop.
            if (cc.Wep != null && cut.lostWeapons.Contains(cc.Wep))
            {
                HeldWeapon.Drop(cc.Wep, fling, gameObject);
                cc.Wep = null;
            }

            if (cc.WepOff != null && cut.lostWeapons.Contains(cc.WepOff))
            {
                HeldWeapon.Drop(cc.WepOff, fling, gameObject);
                cc.WepOff = null;
            }
        }

        return true;
    }

    void ErrorChecker()
    {
        if (!GetComponent<AdderSliceableAsync>())
        {
            Debug.LogWarning("AdderSliceableAsync is missing! (This is what allows things to be cut.)");
        }

        if (!rb)
        {
            Debug.LogWarning("This needs a Rigidbody to work.");
        }
    }
}
