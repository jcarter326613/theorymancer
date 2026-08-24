# Theorymancer Project Context

- Read the relevant files under `docs/` before proposing or making architecture,
  implementation-plan, product-behavior, deployment, or data-model changes.
- Treat `docs/project-brief.md` as the source of truth for product goals and
  `docs/architecture.md` as the source of truth for deployment decisions.
- Keep game-specific code under `apps/<game>/`. Do not generalize game
  abstractions until a second game provides a concrete shared requirement.
- Prefer deterministic analysis for game mechanics and numerical optimization.
  ML may discover patterns and prioritize; LLMs may explain findings but are
  not a source of truth for them.
