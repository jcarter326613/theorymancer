# Skill-Bar Fixtures

Each scenario is self-contained and has no dependency on user credentials,
network access, display resolution, or hotkey assignments.

`reaper-greatsword` captures an untransformed Reaper with a greatsword. Its
`expectations.json` lists the ten semantic slots in left-to-right skill-bar
order. Its `x`, `y`, `width`, and `height` values are integer pixels in the
original screenshot: `(0, 0)` is the upper-left corner, `x` increases right,
and `y` increases down. Tests scale these ground-truth bounds before checking
detector output and icon matching. The fixture also includes expected skill IDs
and names plus SHA-256 hashes for the canonical ArenaNet icon PNGs in `icons/`.
`build-input.json` is the minimal ArenaNet build, equipment, item, and
profession response needed to derive the same candidates.

New fixtures must include a screenshot, normalized slot expectations, a
minimal build input, and only the canonical reference PNGs needed by that
scenario. Do not add the general icon corpus to this directory.

Cooldown-state fixtures may instead set `referenceFixture` to a complete
skill-bar fixture and include only `screenshot`, plus each slot's pixel bounds
and expected `state` in `expectations.json`. Their tests use those explicit
bounds directly; they do not run skill-bar calibration or layout detection.

The diagnostics fixture recorder produces a timestamped JPEG sequence and
`timeline.json`. Copy selected JPEG frames into a state-fixture directory, then
copy each frame's pixel slot bounds from the timeline into that fixture's
`expectations.json`. Keep the original timeline with multi-frame cooldown
fixtures so tests can fit each overlapping skill's wipe progress independently.
