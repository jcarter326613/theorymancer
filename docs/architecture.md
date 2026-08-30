# Architecture

## Product boundaries

Theorymancer is a multi-game platform, initially focused on Guild Wars 2. The
public website is separate from each game's tools or integrations; calling a
game component a "mod" does not imply a particular implementation.

The website will be available at `https://theorymancer.com` in production.
Game-specific code belongs under `apps/<game>/`. We will only extract shared
cross-game abstractions when a second game establishes a concrete need.

## Application structure

The public website and API use Node.js and TypeScript:

- `apps/web` is the React/Vite website.
- `apps/api` is the central authorization server and the system responsible for
  accounts and game grants. It never calls a game API.
- `apps/<game>` is a game-specific application namespace.
- `packages/contracts` holds runtime-validated contracts shared by TypeScript
  applications.

Guild Wars 2 ingestion and deterministic analysis intentionally have no chosen
runtime yet. That code is isolated in `apps/guild-wars-2`, allowing a future
Python worker to use scientific and ML tooling without coupling it to the
website or API runtime.

## Authorization

The central API authenticates users, manages accounts and game grants, and is
the only service that issues or refreshes Theorymancer tokens. Access tokens are
RS256 JWTs with a five-minute lifetime. Refresh credentials are accepted only
by the central API. A revoked grant can therefore remain usable at a child
resource server for no more than the remaining access-token lifetime, bounded
at five minutes.

Game APIs are independent resource servers. They validate issuer, audience,
signature, expiry, and grants locally rather than asking the central API on
every request. A child may fetch the central API's JWKS and may report token
validation failures to a central internal endpoint. Failure reporting uses the
child's Google service identity in addition to application-level authorization;
it is not a general user endpoint. Cloud Run IAM is not the user authorization
boundary.

The desktop client calls the central API for authorization and refresh. It calls
the Guild Wars 2 service directly for game assets; the central API does not
proxy or invoke Guild Wars 2. The central API likewise has no Cloud Run invoker
grant on the Guild Wars 2 service.

The logical production URL namespace reserves auth and account routes for the
central API and `/v1/games/guild-wars-2` for the Guild Wars 2 resource server.
Future production edge routing will map those paths directly to their Cloud Run
services, not through an application proxy. No load balancer is provisioned
yet. Development continues to use separate generated `run.app` URLs, and child
services receive the generated central API URL for JWKS and failure reporting.

## Google Cloud deployment

Google Cloud hosts the initial deployment. Cost control is the primary
infrastructure constraint, so development and production initially share one
GCP project while remaining logically isolated:

- Terraform has independent `development` and `production` root modules and
  remote-state prefixes.
- A `shared` Terraform root owns project-global Identity Platform and Firebase
  configuration plus environment-specific signing keys.
- Each environment has distinct Cloud Run services, runtime service accounts,
  named Firestore databases, and Cloud Storage buckets.
- The manually applied `bootstrap` root owns runtime service accounts and all
  project IAM bindings. Workflow-applied Terraform roots must not mutate the
  project IAM policy, and GitHub Actions identities must not be able to do so.
- Every environment deployment first plans the project-global `shared` root. A
  shared diff requires production approval before its reviewed plan is applied;
  the environment deployment runs only after that apply succeeds or the shared
  plan is a no-op. The first development deployment performs a second, likewise
  protected shared reconciliation after Cloud Run generates the development web
  URL needed by Identity Platform.
- GitHub Environments separate development from production deployment
  permissions. Production should require reviewers before it is used by
  customers.
- GitHub Actions uses Workload Identity Federation; no service-account keys
  are stored in the repository.

Cloud Run hosts the web and API services and scales to zero when unused. Cloud
Storage holds uploaded combat logs, generated artifacts, and immutable
game-specific asset caches. Artifact Registry holds deployable container images.
Development uses the generated Cloud Run URLs and therefore requires no second
domain. Production is reserved for `theorymancer.com`; DNS and custom-domain
configuration occur after ownership and verification are available.

The shared development environment is the target for deployed integration
checks. We will not maintain permanent preview environments for each pull
request. Unit and build checks run in CI; development deployments are used for
integration testing when required.

## Persistence

Firestore is the initial system of record for accounts, external identities,
refresh sessions, game grants, and authorization state. Because development and
production currently share a GCP project, each uses a separate named Firestore
Native database: `theorymancer-development` and `theorymancer-production`.
Production has deletion protection and an abandon-on-destroy policy.

Only central API service identities receive `roles/datastore.user`. Browser and
desktop clients must not access Firestore directly; Firebase client
configuration does not grant database access, and no permissive Firestore rules
are deployed. Each named database has an explicit deny-all client ruleset. Data
access is server IAM only. The Guild Wars 2 service has no Firestore role.

Named Firestore databases receive no free quota. Both databases are therefore
billed for usage from their first read, write, delete, TTL deletion, byte of
storage, or applicable network transfer. Both use Standard edition with
point-in-time recovery disabled initially. This is a deliberate
isolation-versus-cost tradeoff in the shared project.
PostgreSQL remains a possible future system of record if query, transaction, or
analytics requirements justify its fixed operational cost.

## Signing And Identity

Identity Platform is configured once at project scope with separate development
and production tenants and Firebase web apps. Google sign-in requires an OAuth
client secret, so its tenant provider configuration is intentionally not stored
in Terraform state and must be configured through a separate secret-bearing
administrative process. Firebase web app API keys are public client
configuration, not OAuth client secrets.

Each environment has a software-backed Cloud KMS asymmetric
`RSA_SIGN_PKCS1_2048_SHA256` key and a pinned version. Only that environment's
central API identity can sign and read the public key. Game APIs receive neither
permission; they validate from JWKS. The central API's IP-hash value is injected
from a pinned Secret Manager version whose payload is created out of band and
never supplied to Terraform.
