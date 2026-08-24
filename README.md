# Theorymancer

Theorymancer helps players understand the highest-value changes they can make
to improve game performance. Guild Wars 2 combat-log coaching is the initial
focus; the repository keeps game-specific work separately namespaced for
future games.

Product and deployment context live in [`docs/`](docs/).

## Repository layout

- `apps/web`: public React website.
- `apps/api`: public Node.js API.
- `packages/contracts`: validated API contracts shared by TypeScript apps.
- `apps/guild-wars-2`: future Guild Wars 2-specific tooling and analysis.
- `infrastructure`: Terraform for Google Cloud.

## Local development

```bash
cp .env.example .env
pnpm install
pnpm dev
```

The web app runs at `http://localhost:5173`; the API runs at
`http://localhost:3001`.

```bash
pnpm build
pnpm lint
pnpm format:check
```

## Infrastructure

Read [`infrastructure/README.md`](infrastructure/README.md) before applying
Terraform. Bootstrap the project once to establish GitHub OIDC and the shared
Artifact Registry, then deploy either the development or production
environment.
