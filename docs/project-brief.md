I am building a software project that will initially focus on **Guild Wars 2**, but I want the architecture to leave open the possibility of supporting other games later.

## Core product idea

The tool should help players **understand how to improve their actual performance**, rather than merely showing them statistics.

For Guild Wars 2, the initial focus is combat performance. Existing tools such as ArcDPS, dps.report, Wingman, and similar analytics tools expose large amounts of combat-log data, but players are still largely responsible for figuring out what that data means and what they should practice next.

A representative user problem is:

> “The benchmark for my build is 42k DPS. I am doing 24k. I have approximately the right build and gear. What am I actually doing wrong?”

The tool should ideally be able to answer something like:

* 4.2k DPS loss from repeatedly delaying Skill X after it becomes available
* 2.7k DPS loss from interrupting autoattack chains
* 1.8k DPS loss from delayed weapon swaps
* 1.2k DPS loss from poor buff-window alignment

Then, instead of overwhelming the player with every difference from the benchmark, it should recommend the **highest-value thing to practice next**.

For example:

> Your biggest realistically correctable problem is interrupting the third attack in your autoattack chain. Practice that before worrying about your opener.

After the player practices and uploads another log, the system should determine whether that problem improved and identify the next limiting behavior.

## Why I am interested in this

I have personally struggled with Guild Wars 2 damage rotations. One reason I stopped playing was that I could not figure out how to substantially improve my damage, and memorizing/practicing rotations does not come naturally to me.

That means I can actually dogfood this product and judge whether its recommendations are useful.

There is also evidence that other GW2 players have the same problem: players frequently ask why their damage is far below published benchmarks despite apparently having appropriate builds and equipment.

## Longer-term scope

I do **not** want to think of this as permanently being a Guild Wars 2 application.

Many games have analogous optimization problems:

* skill rotations
* cooldown timing
* build optimization
* gear optimization
* stat allocation
* talent/passive-tree optimization
* resource management
* encounter-specific configuration
* action sequencing

The long-term possibility is a platform where each supported game provides an adapter that converts game-specific information into a more general optimization/coaching system.

Conceptually:

```text
Game-specific data
    ↓
Game adapter
    ↓
Canonical representation
    ↓
Analysis / optimization engines
    ├── rotation analysis
    ├── build optimization
    ├── gear optimization
    ├── mistake attribution
    ├── benchmark comparison
    └── coaching
    ↓
Player recommendations
```

I do **not** want to over-generalize prematurely, however. Guild Wars 2 should be allowed to drive the initial architecture. Generalization should happen when actual second-game requirements justify it.

## Initial Guild Wars 2 capabilities

Potential areas include:

### Rotation/performance coaching

Analyze ArcDPS EVTC combat logs and compare a player's behavior against high-performing examples.

Possible analysis includes:

* skill sequencing
* cooldown utilization
* autoattack-chain interruption
* animation/cast cancellation
* weapon-swap timing
* resource utilization
* buff/debuff uptime and alignment
* missed opportunities
* encounter downtime
* target selection
* movement-related DPS loss

A critical goal is **causal or attributable feedback**, not merely correlation or raw statistics.

### Adaptive coaching

The system should prioritize improvements based on:

* estimated performance impact
* how frequently the player makes the mistake
* how difficult the behavior is to change
* the player's current skill level
* improvements observed after previous recommendations

Eventually this could create training data of the form:

```text
player state
    +
identified problem
    +
recommendation
    ↓
subsequent attempts
    ↓
measured improvement
```

This could support recommendation/ranking models that learn which coaching interventions are effective for different players.

### Build optimization

Given a character/build and an objective, search for better configurations.

Examples:

* maximize DPS
* maximize survivability while losing no more than 5% DPS
* improve a build within a gold budget
* optimize for a specific encounter
* identify poor trait/skill/specialization choices

### Gear optimization

Potentially recommend:

* stat combinations
* runes/sigils/relics
* weapons
* armor
* infusions
* upgrades under a specified budget

The system should use deterministic game calculations wherever possible rather than asking an LLM to guess what is optimal.

## Technical philosophy

The interesting part of this project should be **analysis, optimization, ML, data engineering, and distributed systems**, not simply building a web CRUD application.

Where deterministic algorithms can produce a correct answer, prefer them.

Potential techniques include:

* sequence alignment
* dynamic time warping
* graph algorithms
* constrained optimization
* search
* simulation
* ranking models
* clustering players by behavior
* temporal neural networks
* representation learning
* recommendation systems
* anomaly detection
* reinforcement learning where justified

LLMs may be useful for:

* explaining technical findings naturally
* interpreting user goals
* translating requests into structured constraints
* answering contextual questions

But an LLM should **not be the source of truth for game mechanics or numerical optimization**.

A useful principle is:

> Deterministic systems measure and verify. ML discovers patterns and prioritizes. LLMs interpret and explain.

## Architecture preferences

I am an experienced software engineer and particularly enjoy:

* cloud systems
* distributed systems
* event-driven architectures
* queues
* background/batch processing
* scalable data processing
* designing systems with multiple cooperating services

I would like this project to provide opportunities to improve my AI/ML knowledge as well.

A likely architecture could eventually include:

```text
Web application
      ↓
API / orchestration layer
      ↓
job queue
      ↓
analysis workers
      ├── combat-log parser
      ├── rotation analyzer
      ├── optimizer
      ├── ML inference
      └── benchmark processing
      ↓
results / recommendation service
```

This architecture is only a starting point. Do not preserve complexity merely because distributed architecture interests me; complexity should be justified by the workload.

## Product principles

Keep these in mind when discussing the project:

1. **Solve a real player problem.** Do not add AI merely because AI is interesting.
2. **Tell players what to do next**, not just what happened.
3. **Quantify recommendations whenever possible.**
4. **Prefer objective validation.** A recommendation is much more compelling if subsequent combat logs show that it helped.
5. **Avoid overwhelming users.** Prioritize a small number of actionable improvements.
6. **Dogfood aggressively.** I should be able to use the GW2 version myself.
7. **Do not prematurely build a universal game platform.**
8. **Keep eventual multi-game support in mind without compromising the first product.**
9. **Treat existing game-analysis tools as infrastructure or competitors to understand, not automatically as things to replace.**
10. **Look for opportunities where ML creates capabilities that deterministic analytics alone cannot provide.**

## Current objective

The first step is to determine what the **smallest genuinely useful GW2 product** should be.

A likely candidate is:

> Upload an ArcDPS combat log from a training golem or encounter and receive the few mistakes costing the player the most damage, along with specific practice recommendations and comparisons against appropriate benchmark players.

Before committing to an implementation, investigate:

* what EVTC logs actually contain
* how ArcDPS/dps.report/Wingman currently process them
* availability and licensing of benchmark/comparison data
* ArenaNet rules/API limitations
* current competing coaching/analysis tools
* how accurately individual DPS losses can be attributed
* which analyses can be deterministic
* where ML would genuinely improve the result
* whether the same analysis is useful in real encounters as well as the training golem

When proposing features or architecture, challenge assumptions and point out existing competitors or technical limitations rather than simply agreeing with ideas.

