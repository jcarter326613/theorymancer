# Skill-Bar Fixtures

Each scenario is self-contained and has no dependency on user credentials,
network access, display resolution, or hotkey assignments.

`reaper-greatsword` captures an untransformed Reaper with a greatsword. Its
`expectations.json` uses normalized screenshot coordinates and lists the ten
semantic slots, expected skill IDs and names, plus SHA-256 hashes for the
canonical ArenaNet icon PNGs in `icons/`. `build-input.json` is the minimal
ArenaNet build, equipment, item, and profession response needed to derive the
same candidates.

New fixtures must include a screenshot, normalized slot expectations, a
minimal build input, and only the canonical reference PNGs needed by that
scenario. Do not add the general icon corpus to this directory.
