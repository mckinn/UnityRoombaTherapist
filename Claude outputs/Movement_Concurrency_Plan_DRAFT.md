# Movement Concurrency — Draft Plan for Review

**Status:** Reviewed and aligned with Steve, 2026-09-10. Goals and approach for this phase are
agreed; the open items in section 4 are the worklist for the next sub-phase, not blockers to
closing this one out. Filename/DRAFT suffix left as-is for now — rename when committed if
desired. Originally written at Steve's request as the "next level of detail plan" flagged during
Step 5 scoping discussion, 2026-09-10. Revised twice same day: once after reading
`ActiveJourney.cs`, `JourneyCalculator.cs`, and `EmotionMovementConfig.cs` directly (the first
draft's "single active directive, replace semantics" model was wrong about how the existing
reflex tier actually works), and again after reading `CleanMapController.cs` and further
discussion settled three remaining points (sections 2 and 3 below, plus the BehaviorController
decision in section 4).

---

## 1. The problem, as raised

Four things can all plausibly want to direct the Roomba's movement at once:

1. Gather dirt (the existing Clean-Map coverage behavior).
2. Escape a stuck state (`JourneyStuckRecovery`).
3. Respond to an emotion with a Journey (e.g., flee a feared entity).
4. Respond to a direct movement request from the LLM (Step 5's new capability).

Hard constraint, stated explicitly by Steve: the existing collision-triggered Journey movement
is not one more item to arbitrate away — it has to be retained and combined with / prioritized
against whatever this plan proposes, not discarded.

## 2. What's actually already built (confirmed by reading source, 2026-09-10)

This turns out to matter a lot, because items 2 and 3 above are not two separate systems, and
concurrency between multiple emotional targets is not a new problem this plan needs to solve —
it's already solved.

**`JourneyCalculator.cs` owns a `Dictionary<entity_id, ActiveJourney>` (`activeJourneys`) and
blends every currently-unresolved entry every frame.** Each entity the Roomba has ever collided
with gets its own `ActiveJourney`: a target distance `D`, computed from that entity's
`EntitySensitivity` as `D = L + V·(H-L)` (per-emotion `L`/`H` bounds in
`EmotionMovementConfig`, e.g. fear: L=3, H=10 — closer strength means the Roomba tolerates less
distance before it's "resolved"). Every frame, `ComputeBlendedJourneyDirection()` walks every
unresolved entry, computes each one's own pull direction and a weight (`|d_i - D_i|`, how far
current distance is from that entity's target), and returns the weight-averaged sum as one
blended movement direction. An entity drops out of the blend automatically once its distance
error falls inside an epsilon deadband (`Resolved = true`) — but its entry is **never removed**
from the dictionary, just skipped.

That single mechanism already covers three things at once:

- **Collision response IS "respond to an emotion with a Journey."** `HandleEntityCollision`
  (fired from `CollisionController.OnEntityCollision`) is exactly what creates or refreshes an
  `ActiveJourney` from the entity's current `EntitySensitivity`. Items 2 and 3 from section 1
  are one system, not two.
- **Multiple emotional targets already blend concurrently.** If the Roomba is mid-pursuit of two
  different entities at once (e.g., fleeing a couch while still curious about a lamp), both
  contribute to the same weighted-average direction every frame, today, with no new work needed.
- **"Escape a stuck state" is a per-journey internal mechanism, not a competing objective.**
  `RedirectTarget`, `StuckTrackingBaseline`, and `StuckTimer` live directly on `ActiveJourney`
  itself, tracked independently per entity. It changes what direction one journey contributes to
  the blend; it doesn't contend for movement authority the way a distinct goal would. This is
  also why leaving "I'm stuck" out of the Brain-facing schema (already decided this session) costs
  nothing structurally — the reflex-layer recovery keeps working exactly as it does today,
  regardless of what the Brain tier ever learns about.

**Item 1 (gather dirt) has its own, separately-implemented stuck detector, confirmed by reading
`CleanMapController.cs` directly.** `CheckStuckAndMaybeBlock` is structurally near-identical to
`ActiveJourney`'s per-journey recovery — same threshold shapes (1.5s time / 0.05 world-unit net
progress) — but is a completely independent implementation, not a shared one. This resolves a
question raised during review: dirt-gathering does **not** lack stuck-coverage. It's a
code-duplication (DRY) observation, not a missing-behavior gap, and isn't a blocker for this
plan — flagged here only as a minor future cleanup candidate.

**The already-implemented dispatch priority**, read directly from `JourneyCalculator.FixedUpdate`:

1. Paused gate — blocks all autonomous movement (Journey blend, Clean/Map, Behavior pattern
   motion), never blocks the player's manual keyboard control.
2. The Journey blend above, whenever anything is unresolved.
3. Once *everything* tracked is resolved (`StableExpressionJourney` becomes non-null): manual
   keyboard override takes priority if the player is actively driving; otherwise a "Behavior"
   idle-expression pattern *would* run. This is officially deprecated, not merely disabled: it
   was found confusing rather than clarifying in play-testing (hence
   `behaviorMotionEnabled = false`), and per Steve's 2026-09-10 decision it is being removed from
   the codebase now, not carried forward as a disabled switch — see section 4.
4. Clean/Map coverage — explicitly the lowest-priority fallback, driven only "when nothing else
   (Journey, keyboard, Behavior pattern motion) wants to move the Roomba this frame."

**One more consequence worth calling out on its own, because it reshapes an earlier open
question in `Planning_Autonomous_Movement.md` about where entity-location memory should live:**
since resolved journeys are never removed from `activeJourneys`, Unity is already retaining
`entity_id → LastKnownPosition` (plus type, emotion, strength) for *every* entity the Roomba has
ever collided with, for the life of the session, at zero additional cost. The "remembered
location" capability speculated about earlier in this thread already exists Unity-side. What it
doesn't have is a name or first/last-encountered timestamps — see section 4.

## 3. Proposed model for the LLM directive

Rather than adding a new dispatch tier alongside the Journey blend, add the LLM's directive *as
one more entry in the same `activeJourneys` dictionary*, using an analogous target-distance
representation instead of one derived from `EntitySensitivity`.

This gets both of Steve's requirements from the same, already-proven mechanism, with no new
arbitration logic:

- **Replace:** if the directive targets an entity that already has an active emotional Journey,
  it overwrites that entry — the same re-trigger path `HandleEntityCollision` already uses when a
  fresh collision re-opens an existing journey (refresh `DestinationDistance`, clear
  `Resolved`/`RedirectTarget`/recovery state). The LLM's latest word on that specific entity wins
  immediately.
- **Intermingle:** every *other* currently-active journey is untouched and keeps contributing to
  the weighted blend exactly as it does today. A Roomba pursuing an LLM-directed approach to the
  lamp while still mid-flight from a couch it's afraid of gets a blended direction reflecting
  both, automatically — the same as if two ordinary emotional journeys were both active.
- **Natural expiry, already built in:** distance is measured from the Roomba's actual current
  position every frame, regardless of what produced that movement — exactly the mechanic Steve
  described ("a Journey typically expires... as a result of any goal being executed"). This is
  already true of any two existing journeys today; an LLM-directed entry inherits it for free by
  being represented the same way.

**Precision correction, added after further review:** because resolution is a pure distance
check against wherever the blend has actually put the Roomba, an LLM-directed entry's real
stopping point on its own D-circle can land away from a straight-line approach through two
distinct mechanisms, not one. Stuck-recovery redirect (`RedirectTarget`) is the one already
covered above. The second, independent of any stuck condition: whenever another journey is
concurrently blending in (the normal case per section 2, not an edge case), the Roomba's actual
path is the blend of all active pulls, not this entry's own radial direction — so it can cross
its own D-circle at a point the pure straight line would never have produced, with no stuck
condition involved at all. This isn't new behavior introduced by this proposal; it already
applies today to any two ordinary emotional journeys blending together, and an LLM-directed entry
inherits it for the same reason it inherits natural expiry — by being represented the same way.
It doesn't change the proposal above. Per section 5's build-simple-and-observe approach, it's
flagged as a specific thing to watch once this is in play (an "approach the lamp" directive that
visibly resolves somewhere odd because of an unrelated concurrent fear-response), not something
to solve preemptively.

This isn't a new idea being grafted on — `ActiveJourney`'s own docstring already names itself as
the intended extension point: "the natural home for the future proximity-memory feature... once
that's in scope... extending its lifetime beyond resolution, later, is additive, not a second
implementation."

## 4. What this leaves genuinely open

1. **How does an LLM intent compute its own target distance?** Every existing journey's `D` comes
   from `EmotionMovementConfig`'s per-emotion `L`/`H` bounds and an `EntitySensitivity` strength.
   An LLM-issued "approach" or "avoid" isn't derived from that pathway — it needs its own mapping
   (a plausible starting guess: approach → a small fixed D, avoid → a large fixed D, matching the
   existing L/H shape; "investigate" is less obvious — same as approach, or a marker that also
   engages something like an idle/exploratory expression once resolved, now that item 4 below is
   settled as "remove," not "revisit"). Genuinely undecided, not implied by anything already
   built.
2. **Naming and roster (carried over from the Step 5 scoping discussion, now sharpened):** the
   Orchestrator-side roster doesn't need to duplicate position data — Unity already retains it
   indefinitely via `activeJourneys`. What's still needed: a stable name assigned to each
   `entity_id` (Orchestrator-side, per earlier discussion), and first/last-encountered
   timestamps, which `activeJourneys` doesn't currently track (it has `LastKnownPosition`,
   refreshed per re-collision, but not a timestamp of when that happened).
3. **How does a named LLM directive reach Unity and get resolved to an `entity_id`?** Not
   addressed here — this is Step 5's schema question (what `ROOMBA_STATE_TOOL` carries) plus the
   translation-function question already flagged in conversation, not the concurrency question
   this document is scoped to.
4. ~~The currently-disabled Behavior pattern motion...~~ **Decided, 2026-09-10:** not carried
   forward as open. Steve confirmed this is being removed from the codebase, not left as a
   disabled switch or logged as a deferred backlog item — bundled into the Step 5 commit rather
   than done as a standalone change beforehand.

## 5. What this is meant to test

Consistent with "start simple, see what happens": representing the LLM's directive as one more
`activeJourneys` entry is the cheapest way to get real concurrent behavior, because the
concurrency machinery doesn't need to be built — only the one new way of producing an entry
(from an LLM directive rather than a collision) and the one new way of computing its target
distance (open question 1 above). If that turns out to produce unsatisfying behavior in play —
directives getting lost in the blend, dominating it unfairly, or resolving at a visibly odd point
on the D-circle because of concurrent-blend diversion (section 3) — that's concrete evidence for
where to add real complexity (e.g., giving LLM-directed entries their own weighting rule) rather
than a reason to have built it upfront.
