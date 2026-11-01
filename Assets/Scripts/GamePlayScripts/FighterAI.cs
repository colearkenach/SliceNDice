using UnityEngine;

// Drives Player 2 when only one controller is connected. It plays by the Bushido Blade rules
// the human does (it only calls CharacterControl's move / stance / dash / attack): it fishes
// for a stance the opponent's guard doesn't cover, punishes whiffed and blocked attacks, steps
// out of incoming swings, and backs off to get its stamina back.
public class FighterAI : MonoBehaviour
{
    [Header("Spacing")]
    // Defaults only; recalculated from the equipped weapon's reach on Start.
    [SerializeField] float attackRange = 1.9f;          // Close enough to start a swing (attacks step in).
    [SerializeField] float preferredRange = 2.6f;       // Hover just outside the opponent's reach.
    [SerializeField] float tooClose = 0.9f;

    [Header("Timing")]
    [SerializeField] float reactionTime = 0.2f;         // Delay before reacting to what the opponent does.
    [SerializeField] Vector2 thinkInterval = new Vector2(0.3f, 0.7f);
    [SerializeField] Vector2 stanceInterval = new Vector2(1.2f, 3f);

    [Header("Personality")]
    [SerializeField, Range(0f, 1f)] float aggression = 0.45f;     // Chance to throw a probing attack when nothing better is on.
    [SerializeField, Range(0f, 1f)] float evasiveness = 0.55f;    // Chance to dash away from an incoming swing.
    [SerializeField, Range(0f, 1f)] float cunning = 0.7f;         // Chance to pick a stance that slips the opponent's guard.

    [Header("Ledges")]
    [SerializeField] float ledgeProbeDistance = 1.0f;
    [SerializeField] float ledgeProbeDepth = 3f;
    [SerializeField] LayerMask groundMask = ~0;

    enum Plan { Approach, Circle, Retreat, Hold }

    CharacterControl control;
    Character self;

    Plan plan = Plan.Circle;
    float nextThinkTime;
    float nextStanceTime;
    float circleSign = 1f;
    float opportunitySince = -1f;
    float threatSince = -1f;
    bool dodgeThisThreat;

    Vector3 lastPosition;
    float stuckTimer;
    float nextLedgeTurnTime;

    void Awake()
    {
        control = GetComponent<CharacterControl>();
        self = GetComponent<Character>();
    }

    void OnEnable()
    {
        lastPosition = transform.position;
        nextStanceTime = Time.time + Random.Range(stanceInterval.x, stanceInterval.y);
    }

    void Start()
    {
        CalibrateRange();
    }

    // Fighters and weapons vary a lot in size, so base the swing range on how far
    // this fighter's weapon actually reaches (weapons are attached by now).
    void CalibrateRange()
    {
        Weapon weapon = control.Wep != null ? control.Wep.GetComponentInChildren<Weapon>() : null;
        if (weapon == null)
        {
            return;
        }

        Vector3 toTip = weapon.EdgeTipWorld - transform.position;
        toTip.y = 0f;
        float reach = toTip.magnitude;

        // Attacks step in up to about a metre, so the range is reach plus most of that.
        attackRange = Mathf.Clamp(reach * 1.2f + 0.8f, 1.4f, 3f);
        preferredRange = attackRange + 0.6f;
        tooClose = Mathf.Max(0.7f, reach * 0.6f);
    }

    void Update()
    {
        if (control == null || (self != null && self.IsDead))
        {
            return;
        }

        Transform opponent = control.Opponent;
        Character them = opponent != null ? opponent.GetComponent<Character>() : null;
        if (them == null || them.IsDead)
        {
            control.SetMoveIntent(Vector3.zero);
            return;
        }

        CharacterControl theirControl = them.Control;
        Vector3 toOpponent = opponent.position - transform.position;
        toOpponent.y = 0f;
        float distance = toOpponent.magnitude;
        Vector3 forward = distance > 0.001f ? toOpponent / distance : transform.forward;
        Vector3 side = Vector3.Cross(Vector3.up, forward) * circleSign;

        if (React(them, theirControl, distance, forward, side))
        {
            return;
        }

        if (Time.time >= nextStanceTime)
        {
            ChooseStance(theirControl);
        }

        if (Time.time >= nextThinkTime)
        {
            Think(them, distance);
        }

        Vector3 move = ChooseMove(forward, side, distance);
        move = AvoidLedges(move, forward, side);
        UpdateStuck(move);
        control.SetMoveIntent(move);
    }

    // Reacting to the opponent comes first: get out of a swing, or punish an opening.
    bool React(Character them, CharacterControl theirControl, float distance, Vector3 forward, Vector3 side)
    {
        // An attack is coming and we're in reach. Decide once per attack whether to trust the
        // guard or step out of it (always step out if the guard won't cover it or is nearly spent).
        bool threatened = theirControl != null && theirControl.IsWindingUp && distance < attackRange + 0.8f;
        if (!threatened)
        {
            threatSince = -1f;
        }
        else if (threatSince < 0f)
        {
            threatSince = Time.time;
            bool guardCovers = self.IsGuarding && (control.CurrentStance == CharacterControl.Stance.Mid || control.CurrentStance == theirControl.CurrentStance);
            dodgeThisThreat = !guardCovers || self.playerGuard < 35f || Random.value < evasiveness;
        }

        if (threatened && dodgeThisThreat && Time.time - threatSince >= reactionTime * 0.5f)
        {
            // Step back or to the side, out of the swing (but not off a ledge).
            Vector3 escape = Random.value < 0.5f ? -forward : side;
            if (!HasGroundAhead(escape, DashDistance))
            {
                escape = HasGroundAhead(-side, DashDistance) ? -side : -forward;
            }

            if (HasGroundAhead(escape, DashDistance) && control.TryDash(escape))
            {
                dodgeThisThreat = false;
                return true;
            }
        }

        // While a swing is coming in, hold the guard rather than swinging into it.
        if (!control.IsArmed || threatened)
        {
            return false;
        }

        // Openings: stuck in a follow-through, reeling, guard broken, or idle without a guard
        // (dashing or out of stamina). Not their wind-up or swing: that's when they're dangerous.
        bool idleUnguarded = theirControl != null && !theirControl.IsAttacking && !them.IsGuarding;
        bool opening = theirControl != null && (theirControl.IsRecovering || theirControl.IsStaggered || them.IsGuardBroken || idleUnguarded);
        bool inRange = distance <= attackRange;
        opportunitySince = opening && inRange ? (opportunitySince < 0f ? Time.time : opportunitySince) : -1f;
        if (opportunitySince >= 0f && Time.time - opportunitySince >= reactionTime)
        {
            // Heavy into a broken guard, otherwise the quickest thing that lands.
            CharacterControl.AttackType attack = them.IsGuardBroken ? CharacterControl.AttackType.Heavy
                : distance > attackRange * 0.75f ? CharacterControl.AttackType.Stab
                : CharacterControl.AttackType.Fast;
            if (control.TryAttack(attack))
            {
                opportunitySince = -1f;
                plan = Plan.Retreat;
                nextThinkTime = Time.time + Random.Range(0.4f, 0.8f);
                return true;
            }
        }

        return false;
    }

    // Pick a stance whose attacks the opponent's current stance can't block (High vs Low),
    // or settle into Mid to guard everything.
    void ChooseStance(CharacterControl theirControl)
    {
        nextStanceTime = Time.time + Random.Range(stanceInterval.x, stanceInterval.y);
        if (theirControl == null)
        {
            return;
        }

        CharacterControl.Stance wanted;
        if (Random.value < cunning)
        {
            switch (theirControl.CurrentStance)
            {
                case CharacterControl.Stance.High: wanted = CharacterControl.Stance.Low; break;
                case CharacterControl.Stance.Low: wanted = CharacterControl.Stance.High; break;
                default: wanted = Random.value < 0.5f ? CharacterControl.Stance.High : CharacterControl.Stance.Low; break;
            }
        }
        else
        {
            wanted = CharacterControl.Stance.Mid;
        }

        // Stance changes go one step at a time.
        CharacterControl.Stance current = control.CurrentStance;
        if (wanted == current)
        {
            return;
        }

        CharacterControl.Stance step = wanted == CharacterControl.Stance.Mid || current == CharacterControl.Stance.Mid
            ? wanted
            : CharacterControl.Stance.Mid;
        control.TryChangeStance(step);
        if (step != wanted)
        {
            nextStanceTime = Time.time + 0.2f;   // Finish the change shortly.
        }
    }

    void Think(Character them, float distance)
    {
        nextThinkTime = Time.time + Random.Range(thinkInterval.x, thinkInterval.y);

        bool lowStamina = self.playerMaxGuard > 0f && self.playerGuard / self.playerMaxGuard < 0.3f;

        if (!control.IsArmed || lowStamina)
        {
            // Nothing to fight with, or no guard left: keep away and recover.
            plan = distance < preferredRange + 1f ? Plan.Retreat : Plan.Circle;
            return;
        }

        if (distance < tooClose)
        {
            plan = Plan.Retreat;
            return;
        }

        // Their stance can't block ours: go in.
        bool slipsGuard = them.Control != null
            && ((control.CurrentStance == CharacterControl.Stance.High && them.Control.CurrentStance == CharacterControl.Stance.Low)
             || (control.CurrentStance == CharacterControl.Stance.Low && them.Control.CurrentStance == CharacterControl.Stance.High));

        if (distance <= attackRange && (slipsGuard || Random.value < aggression))
        {
            // Probing or committed attack. Most probes get blocked, but they wear the guard down.
            CharacterControl.AttackType attack = slipsGuard || Random.value < 0.3f
                ? CharacterControl.AttackType.Heavy
                : (Random.value < 0.5f ? CharacterControl.AttackType.Fast : CharacterControl.AttackType.Stab);
            if (control.TryAttack(attack))
            {
                plan = Plan.Retreat;
                nextThinkTime = Time.time + Random.Range(0.5f, 0.9f);
                return;
            }
        }

        if (distance > preferredRange + 0.4f)
        {
            plan = Plan.Approach;
        }
        else if (slipsGuard)
        {
            plan = Plan.Approach;
        }
        else
        {
            plan = Random.value < 0.7f ? Plan.Circle : Plan.Hold;
            if (Random.value < 0.3f) circleSign = -circleSign;
        }
    }

    Vector3 ChooseMove(Vector3 forward, Vector3 side, float distance)
    {
        switch (plan)
        {
            case Plan.Approach:
                // Close in on a slight curve so the approach isn't a perfectly straight line.
                return (forward + side * 0.25f).normalized;

            case Plan.Retreat:
                return (-forward + side * 0.4f).normalized;

            case Plan.Circle:
                // Strafe around the opponent while correcting toward the preferred range.
                float radial = Mathf.Clamp(distance - preferredRange, -1f, 1f);
                return (side + forward * radial).normalized * 0.8f;

            default:
                return Vector3.zero;
        }
    }

    Vector3 AvoidLedges(Vector3 move, Vector3 forward, Vector3 side)
    {
        if (move.sqrMagnitude < 0.0001f || HasGroundAhead(move))
        {
            return move;
        }

        // About to walk off an edge: stop backing up, and swap strafe direction (not every
        // frame, or the fighter just jitters on the spot). Try the other side, then forward.
        if (plan == Plan.Retreat)
        {
            plan = Plan.Circle;
        }

        if (Time.time >= nextLedgeTurnTime)
        {
            circleSign = -circleSign;
            nextLedgeTurnTime = Time.time + 0.75f;
        }

        Vector3 alternative = (-side + forward * 0.5f).normalized;
        if (HasGroundAhead(alternative))
        {
            return alternative;
        }

        return HasGroundAhead(forward) ? forward : Vector3.zero;
    }

    // A dash covers about 1.4 m, further than a walking step looks ahead.
    const float DashDistance = 1.6f;

    bool HasGroundAhead(Vector3 direction)
    {
        return HasGroundAhead(direction, ledgeProbeDistance);
    }

    bool HasGroundAhead(Vector3 direction, float distance)
    {
        Vector3 probe = transform.position + Vector3.up * 0.5f + direction.normalized * distance;
        return Physics.Raycast(probe, Vector3.down, ledgeProbeDepth + 0.5f, groundMask, QueryTriggerInteraction.Ignore);
    }

    void UpdateStuck(Vector3 move)
    {
        Vector3 delta = transform.position - lastPosition;
        delta.y = 0f;
        lastPosition = transform.position;

        // "Stuck" = trying to walk but going slower than 0.15 m/s (attacks and reeling don't count).
        bool tryingToMove = move.sqrMagnitude > 0.1f && !control.IsAttacking && !control.IsStaggered;
        if (tryingToMove && delta.magnitude < 0.15f * Time.deltaTime)
        {
            stuckTimer += Time.deltaTime;
            if (stuckTimer > 0.6f)
            {
                // Snagged on something: change direction.
                stuckTimer = 0f;
                circleSign = -circleSign;
                plan = plan == Plan.Approach ? Plan.Circle : Plan.Approach;
                nextThinkTime = Time.time + 0.5f;
            }
        }
        else
        {
            stuckTimer = 0f;
        }
    }
}
