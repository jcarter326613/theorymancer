import { createHash } from "node:crypto"

import { KeyManagementServiceClient } from "@google-cloud/kms"
import { applicationDefault, getApps, initializeApp } from "firebase-admin/app"
import { getAuth } from "firebase-admin/auth"
import { OAuth2Client } from "google-auth-library"
import { exportJWK, importSPKI } from "jose"
import type { JWK } from "jose"

import type {
    AccessTokenClaims,
    IdentityVerifier,
    ServiceIdentityVerifier,
    TokenIssuer,
} from "./auth-types.js"

export class FirebaseIdentityVerifier implements IdentityVerifier {
    public constructor(
        projectId: string,
        private readonly tenantId: string,
    ) {
        if (getApps().length === 0) {
            initializeApp({ credential: applicationDefault(), projectId })
        }
    }

    public async verify(
        token: string,
    ): Promise<{ uid: string; email?: string }> {
        const decoded = await getAuth()
            .tenantManager()
            .authForTenant(this.tenantId)
            .verifyIdToken(token)
        if (decoded.firebase.tenant !== this.tenantId) {
            throw new Error("Unexpected Firebase tenant")
        }
        return {
            uid: decoded.uid,
            ...(decoded.email === undefined ? {} : { email: decoded.email }),
        }
    }
}

export class GoogleServiceIdentityVerifier implements ServiceIdentityVerifier {
    private readonly client = new OAuth2Client()

    public constructor(private readonly audience: string) {}

    public async verify(token: string): Promise<{ email: string }> {
        const ticket = await this.client.verifyIdToken({
            idToken: token,
            audience: this.audience,
        })
        const payload = ticket.getPayload()
        if (payload?.email === undefined || payload.email_verified !== true) {
            throw new Error("Invalid service identity")
        }
        return { email: payload.email }
    }
}

export class KmsTokenIssuer implements TokenIssuer {
    private readonly client = new KeyManagementServiceClient()
    private publicJwkPromise?: Promise<JWK>

    public constructor(
        private readonly keyVersionName: string,
        private readonly keyId: string,
    ) {}

    public async issueAccessToken(claims: AccessTokenClaims): Promise<string> {
        const header = encodeJson({ alg: "RS256", typ: "JWT", kid: this.keyId })
        const payload = encodeJson(claims)
        const signingInput = `${header}.${payload}`
        const digest = createHash("sha256").update(signingInput).digest()
        const [result] = await this.client.asymmetricSign({
            name: this.keyVersionName,
            digest: { sha256: digest },
        })
        if (result.signature === undefined || result.signature === null) {
            throw new Error("KMS did not return a signature")
        }
        const signature =
            typeof result.signature === "string"
                ? Buffer.from(result.signature, "base64")
                : Buffer.from(result.signature)
        return `${signingInput}.${signature.toString("base64url")}`
    }

    public async getJwks(): Promise<{ keys: JWK[] }> {
        return { keys: [await this.getPublicJwk()] }
    }

    private getPublicJwk(): Promise<JWK> {
        this.publicJwkPromise ??= this.loadPublicJwk()
        return this.publicJwkPromise
    }

    private async loadPublicJwk(): Promise<JWK> {
        const [result] = await this.client.getPublicKey({
            name: this.keyVersionName,
        })
        if (result.pem === undefined || result.pem === null) {
            throw new Error("KMS did not return a public key")
        }
        const key = await importSPKI(result.pem, "RS256")
        return {
            ...(await exportJWK(key)),
            kid: this.keyId,
            alg: "RS256",
            use: "sig",
        }
    }
}

function encodeJson(value: unknown): string {
    return Buffer.from(JSON.stringify(value)).toString("base64url")
}
