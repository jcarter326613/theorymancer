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

Then, instead of overwhelming the player with every difference from the benchmark, it should recommend the **highest-value thing to practice next**.  Rather than actually comparing against established benchmarks, I want the system to be able to determine what an improvement would be to the user's build and skill rotation based on theoretical max dps.

For example:

> Your biggest realistically correctable problem is interrupting the third attack in your autoattack chain. Practice that before worrying about your opener.

After the player practices and uploads another log, the system should determine whether that problem improved and identify the next limiting behavior.

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

The long-term possibility is website that hosts tooling for several games.  While we may discover opportunities for overlapping architecture, lets not over engineer this thing to be super generic when we have no idea what a good strucure would look like for different games.  Instead, lets rely on "namespacing" and good project folder structure to seperate components that are game specific.

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

Improvements should take into account the cost of the improvement and optimize around effort/gold per dps for its next suggestion.

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

