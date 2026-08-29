import { createHash } from "node:crypto"

import {
    calculateJwkThumbprint,
    createRemoteJWKSet,
    importJWK,
    jwtVerify,
} from "jose"
import type { JWK, JWTVerifyGetKey } from "jose"

const requiredScope = "guild-wars-2.assets.read"
const defaultClockToleranceSeconds = 300
const defaultProofMaxAgeSeconds = 300
const defaultReplayCacheSize = 5_000

export interface AuthenticationRequest {
    accessToken: string | undefined
    dpopProof: string | undefined
    method: string
    url: string
}

export interface Authenticator {
    authenticate(request: AuthenticationRequest): Promise<void>
}

export class AuthenticationRejectedError extends Error {}

export interface AuthenticatorOptions {
    issuer: string
    audience: string
    jwksUrl: URL
    clockToleranceSeconds?: number
    proofMaxAgeSeconds?: number
    replayCacheSize?: number
    jwks?: JWTVerifyGetKey
}

export class TheorymancerAuthenticator implements Authenticator {
    private readonly issuer: string
    private readonly audience: string
    private readonly clockToleranceSeconds: number
    private readonly proofMaxAgeSeconds: number
    private readonly jwks: JWTVerifyGetKey
    private readonly replayCache: DpopReplayCache

    public constructor(options: AuthenticatorOptions) {
        this.issuer = options.issuer
        this.audience = options.audience
        this.clockToleranceSeconds =
            options.clockToleranceSeconds ?? defaultClockToleranceSeconds
        this.proofMaxAgeSeconds =
            options.proofMaxAgeSeconds ?? defaultProofMaxAgeSeconds
        this.jwks = options.jwks ?? createRemoteJWKSet(options.jwksUrl)
        this.replayCache = new DpopReplayCache(
            options.replayCacheSize ?? defaultReplayCacheSize,
        )
    }

    public async authenticate(request: AuthenticationRequest): Promise<void> {
        try {
            await this.verify(request)
        } catch (error) {
            if (isOperationalVerificationError(error)) throw error
            throw new AuthenticationRejectedError("DPoP authentication failed")
        }
    }

    private async verify(request: AuthenticationRequest): Promise<void> {
        if (
            request.accessToken === undefined ||
            request.dpopProof === undefined
        ) {
            throw new Error("Both an access token and DPoP proof are required.")
        }

        const { payload } = await jwtVerify(request.accessToken, this.jwks, {
            algorithms: ["RS256"],
            audience: this.audience,
            issuer: this.issuer,
            requiredClaims: ["exp", "iat", "sub"],
            clockTolerance: this.clockToleranceSeconds,
        })
        const now = Math.floor(Date.now() / 1_000)
        if (
            payload.aud !== this.audience ||
            typeof payload.iat !== "number" ||
            !Number.isInteger(payload.iat) ||
            payload.iat > now + this.clockToleranceSeconds ||
            typeof payload.exp !== "number" ||
            !Number.isInteger(payload.exp) ||
            payload.exp <= payload.iat ||
            payload.exp - payload.iat > 300 ||
            typeof payload.sub !== "string" ||
            payload.sub.length === 0 ||
            typeof payload.installation_id !== "string" ||
            payload.installation_id.length === 0 ||
            !hasRequiredScope(payload.scope)
        ) {
            throw new Error("The access token claims are invalid.")
        }

        const confirmation = payload.cnf
        if (
            !isRecord(confirmation) ||
            typeof confirmation.jkt !== "string" ||
            confirmation.jkt.length === 0
        ) {
            throw new Error(
                "The access token is not bound to an installation key.",
            )
        }

        const protectedHeader = parseProtectedHeader(request.dpopProof)
        const jwk = protectedHeader.jwk
        if (
            typeof protectedHeader.typ !== "string" ||
            protectedHeader.typ.toLowerCase() !== "dpop+jwt" ||
            protectedHeader.alg !== "ES256" ||
            !isPublicP256Jwk(jwk)
        ) {
            throw new Error("The DPoP proof header is invalid.")
        }

        const proofKey = await importJWK(jwk, "ES256")
        const { payload: proof } = await jwtVerify(
            request.dpopProof,
            proofKey,
            {
                algorithms: ["ES256"],
                typ: "dpop+jwt",
            },
        )
        if (
            typeof proof.htm !== "string" ||
            proof.htm !== request.method.toUpperCase() ||
            typeof proof.htu !== "string" ||
            proof.htu !== request.url ||
            typeof proof.iat !== "number" ||
            !Number.isInteger(proof.iat) ||
            proof.iat > now + this.clockToleranceSeconds ||
            proof.iat < now - this.proofMaxAgeSeconds ||
            typeof proof.jti !== "string" ||
            proof.jti.length === 0 ||
            typeof proof.ath !== "string" ||
            proof.ath !== accessTokenHash(request.accessToken)
        ) {
            throw new Error("The DPoP proof claims are invalid.")
        }

        const thumbprint = await calculateJwkThumbprint(jwk, "sha256")
        if (thumbprint !== confirmation.jkt) {
            throw new Error(
                "The DPoP proof key does not match the access token.",
            )
        }

        const replayKey = `${thumbprint}:${proof.jti}`
        const replayExpiry =
            proof.iat + this.proofMaxAgeSeconds + this.clockToleranceSeconds
        if (!this.replayCache.consume(replayKey, replayExpiry, now)) {
            throw new Error("The DPoP proof has already been used.")
        }
    }
}

function hasRequiredScope(scope: unknown): boolean {
    return (
        typeof scope === "string" && scope.split(/\s+/u).includes(requiredScope)
    )
}

function parseProtectedHeader(token: string): Record<string, unknown> {
    const [encodedHeader] = token.split(".")
    if (encodedHeader === undefined) {
        throw new Error("The DPoP proof is malformed.")
    }

    try {
        const parsed: unknown = JSON.parse(
            Buffer.from(encodedHeader, "base64url").toString("utf8"),
        )
        if (!isRecord(parsed)) {
            throw new Error("The DPoP proof header is invalid.")
        }
        return parsed
    } catch {
        throw new Error("The DPoP proof header is invalid.")
    }
}

function isPublicP256Jwk(value: unknown): value is JWK {
    return (
        isRecord(value) &&
        value.kty === "EC" &&
        value.crv === "P-256" &&
        typeof value.x === "string" &&
        value.x.length > 0 &&
        typeof value.y === "string" &&
        value.y.length > 0 &&
        !("d" in value)
    )
}

function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === "object" && value !== null
}

function accessTokenHash(accessToken: string): string {
    return createHash("sha256").update(accessToken, "ascii").digest("base64url")
}

class DpopReplayCache {
    private readonly entries = new Map<string, number>()

    public constructor(private readonly maximumSize: number) {
        if (!Number.isInteger(maximumSize) || maximumSize < 1) {
            throw new Error("The DPoP replay cache size must be positive.")
        }
    }

    public consume(key: string, expiresAt: number, now: number): boolean {
        for (const [existingKey, existingExpiry] of this.entries) {
            if (existingExpiry < now) {
                this.entries.delete(existingKey)
            }
        }

        if (this.entries.has(key)) {
            return false
        }

        if (this.entries.size >= this.maximumSize) {
            const oldestKey = this.entries.keys().next().value as
                string | undefined
            if (oldestKey !== undefined) this.entries.delete(oldestKey)
        }
        this.entries.set(key, expiresAt)
        return true
    }
}

function isOperationalVerificationError(error: unknown): boolean {
    if (error instanceof TypeError) return true
    if (typeof error !== "object" || error === null || !("code" in error)) {
        return false
    }
    return new Set([
        "ERR_JOSE_GENERIC",
        "ERR_JWKS_INVALID",
        "ERR_JWKS_TIMEOUT",
    ]).has(String(error.code))
}
