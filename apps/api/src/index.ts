import { z } from "zod"

import { createApp } from "./app.js"
import { FirestoreAuthStore } from "./firestore-auth-store.js"
import {
    FirebaseIdentityVerifier,
    GoogleServiceIdentityVerifier,
    KmsTokenIssuer,
} from "./runtime-adapters.js"

const environmentSchema = z.object({
    GCP_PROJECT_ID: z.string().min(1),
    FIRESTORE_DATABASE_ID: z.string().min(1),
    FIREBASE_TENANT_ID: z.string().min(1),
    AUTH_ISSUER: z.string().url(),
    GW2_AUTH_AUDIENCE: z.string().min(1),
    AUTH_SIGNING_KEY_VERSION: z.string().min(1),
    AUTH_SIGNING_KEY_ID: z.string().min(1),
    WEB_ORIGIN: z.string().url(),
    IP_HASH_SECRET: z.string().min(32),
    INTERNAL_FAILURE_REPORTER_SERVICE_ACCOUNTS: z.string().min(1),
    API_PORT: z.coerce.number().int().min(1).max(65535).default(3001),
})

const environment = environmentSchema.parse(process.env)
const issuer = environment.AUTH_ISSUER.replace(/\/$/, "")
const allowedServiceAccounts = new Set(
    environment.INTERNAL_FAILURE_REPORTER_SERVICE_ACCOUNTS.split(",").map(
        (email) => email.trim().toLowerCase(),
    ),
)
if (allowedServiceAccounts.has("")) {
    throw new Error("Service account allowlist contains an empty value")
}

const app = createApp({
    store: new FirestoreAuthStore(
        environment.GCP_PROJECT_ID,
        environment.FIRESTORE_DATABASE_ID,
    ),
    identityVerifier: new FirebaseIdentityVerifier(
        environment.GCP_PROJECT_ID,
        environment.FIREBASE_TENANT_ID,
    ),
    serviceIdentityVerifier: new GoogleServiceIdentityVerifier(issuer),
    tokenIssuer: new KmsTokenIssuer(
        environment.AUTH_SIGNING_KEY_VERSION,
        environment.AUTH_SIGNING_KEY_ID,
    ),
    config: {
        issuer,
        audience: environment.GW2_AUTH_AUDIENCE,
        webOrigin: environment.WEB_ORIGIN,
        ipHashSecret: environment.IP_HASH_SECRET,
        internalFailureReporterServiceAccounts: allowedServiceAccounts,
    },
})

app.listen(environment.API_PORT, () => {
    console.log(`Theorymancer API listening on port ${environment.API_PORT}`)
})
