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
