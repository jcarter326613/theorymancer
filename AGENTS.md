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
- GitHub Actions identities must never receive a role that can modify project
  IAM policy, including `roles/resourcemanager.projectIamAdmin`. Do not put
  `google_project_iam_*` resources in a workflow-applied Terraform root.
  Project IAM bindings and runtime service-account creation belong only in the
  manually applied `infrastructure/bootstrap` root.
