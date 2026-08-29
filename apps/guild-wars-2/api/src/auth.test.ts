import assert from "node:assert/strict"
import { createServer } from "node:http"
import type { Server } from "node:http"
import type { AddressInfo } from "node:net"
import { afterEach, test } from "node:test"

import {
    calculateJwkThumbprint,
    exportJWK,
    generateKeyPair,
    SignJWT,
} from "jose"

import { TheorymancerAuthenticator } from "./auth.js"

const issuer = "https://auth.theorymancer.test"
const audience = "guild-wars-2-api"
const requestUrl = "https://gw2.theorymancer.test/icons/manifest"
const servers: Server[] = []

afterEach(async () => {
    for (const server of servers.splice(0)) {
        await server.close()
    }
})

void test("verifies bound access tokens and DPoP proofs with a cached remote JWKS", async () => {
    const fixture = await createFixture()
    const accessToken = await fixture.accessToken()
    const firstProof = await fixture.proof(accessToken, { jti: "proof-1" })

    await fixture.authenticator.authenticate({
        accessToken,
        dpopProof: firstProof,
        method: "GET",
        url: requestUrl,
    })
    await fixture.authenticator.authenticate({
        accessToken,
        dpopProof: await fixture.proof(accessToken, { jti: "proof-2" }),
        method: "GET",
        url: requestUrl,
    })

    assert.equal(fixture.jwksRequests(), 1)
    await assert.rejects(
        fixture.authenticator.authenticate({
            accessToken,
            dpopProof: firstProof,
            method: "GET",
            url: requestUrl,
        }),
        /authentication failed/u,
    )
})

void test("rejects invalid access-token claims", async () => {
    const fixture = await createFixture()
    const now = Math.floor(Date.now() / 1_000)
    const invalidClaims = [
        { scope: "different.scope" },
        { installation_id: "" },
        { sub: "" },
        { iat: now + 30 },
        { exp: now + 301 },
        { cnf: {} },
    ]

    for (const [index, claims] of invalidClaims.entries()) {
        const accessToken = await fixture.accessToken(claims)
        await assert.rejects(
            fixture.authenticator.authenticate({
                accessToken,
                dpopProof: await fixture.proof(accessToken, {
                    jti: `invalid-access-${index}`,
                }),
                method: "GET",
                url: requestUrl,
            }),
        )
    }

    const wrongAudienceToken = await fixture.accessToken({
        aud: "not-guild-wars-2",
    })
    await assert.rejects(
        fixture.authenticator.authenticate({
            accessToken: wrongAudienceToken,
            dpopProof: await fixture.proof(wrongAudienceToken),
            method: "GET",
            url: requestUrl,
        }),
    )
})

void test("rejects invalid DPoP proof claims and key binding", async () => {
    const fixture = await createFixture()
    const accessToken = await fixture.accessToken()
    const now = Math.floor(Date.now() / 1_000)
    const invalidProofs = [
        await fixture.proof(accessToken, { htm: "POST", jti: "bad-method" }),
        await fixture.proof(accessToken, {
            htu: `${requestUrl}/wrong`,
            jti: "bad-url",
        }),
        await fixture.proof(accessToken, {
            iat: now - 301,
            jti: "stale-proof",
        }),
        await fixture.proof(`${accessToken}wrong`, { jti: "bad-ath" }),
    ]

    for (const proof of invalidProofs) {
        await assert.rejects(
            fixture.authenticator.authenticate({
                accessToken,
                dpopProof: proof,
                method: "GET",
                url: requestUrl,
            }),
        )
    }

    const otherProofKey = await generateKeyPair("ES256")
    await assert.rejects(
        fixture.authenticator.authenticate({
            accessToken,
            dpopProof: await signProof(accessToken, otherProofKey, {}),
            method: "GET",
            url: requestUrl,
        }),
        /authentication failed/u,
    )
})

async function createFixture(): Promise<{
    authenticator: TheorymancerAuthenticator
    accessToken: (claims?: Record<string, unknown>) => Promise<string>
    proof: (
        accessToken: string,
        claims?: Record<string, unknown>,
    ) => Promise<string>
    jwksRequests: () => number
}> {
    const accessKey = await generateKeyPair("RS256")
    const proofKey = await generateKeyPair("ES256")
    const accessPublicJwk = await exportJWK(accessKey.publicKey)
    accessPublicJwk.kid = "access-key"
    accessPublicJwk.alg = "RS256"
    accessPublicJwk.use = "sig"
    const proofPublicJwk = await exportJWK(proofKey.publicKey)
    const thumbprint = await calculateJwkThumbprint(proofPublicJwk)
    let requestCount = 0
    const server = createServer((_request, response) => {
        requestCount += 1
        response.setHeader("Content-Type", "application/json")
        response.end(JSON.stringify({ keys: [accessPublicJwk] }))
    }).listen(0)
    servers.push(server)
    await new Promise<void>((resolve) => server.once("listening", resolve))
    const { port } = server.address() as AddressInfo

    return {
        authenticator: new TheorymancerAuthenticator({
            issuer,
            audience,
            jwksUrl: new URL(`http://127.0.0.1:${port}/jwks`),
            clockToleranceSeconds: 0,
        }),
        async accessToken(claims = {}) {
            const now = Math.floor(Date.now() / 1_000)
            const payload = {
                scope: "profile guild-wars-2.assets.read",
                installation_id: "installation-123",
                cnf: { jkt: thumbprint },
                ...claims,
            }
            const token = new SignJWT(payload)
                .setProtectedHeader({ alg: "RS256", kid: "access-key" })
                .setIssuer(issuer)
                .setAudience(audience)
                .setSubject("user-123")
                .setIssuedAt(now)
                .setExpirationTime(now + 60)
            applyRegisteredClaims(token, claims)
            return token.sign(accessKey.privateKey)
        },
        proof(accessToken, claims = {}) {
            return signProof(accessToken, proofKey, claims)
        },
        jwksRequests() {
            return requestCount
        },
    }
}

async function signProof(
    accessToken: string,
    proofKey: Awaited<ReturnType<typeof generateKeyPair>>,
    claims: Record<string, unknown>,
): Promise<string> {
    const { createHash } = await import("node:crypto")
    const publicJwk = await exportJWK(proofKey.publicKey)
    return new SignJWT({
        htm: "GET",
        htu: requestUrl,
        ath: createHash("sha256")
            .update(accessToken, "ascii")
            .digest("base64url"),
        ...claims,
    })
        .setProtectedHeader({
            alg: "ES256",
            typ: "dpop+jwt",
            jwk: publicJwk,
        })
        .setIssuedAt(typeof claims.iat === "number" ? claims.iat : undefined)
        .setJti(typeof claims.jti === "string" ? claims.jti : "proof-default")
        .sign(proofKey.privateKey)
}

function applyRegisteredClaims(
    token: SignJWT,
    claims: Record<string, unknown>,
): void {
    if (typeof claims.aud === "string") {
        token.setAudience(claims.aud)
    }
    if (typeof claims.sub === "string") {
        token.setSubject(claims.sub)
    }
    if (typeof claims.iat === "number") {
        token.setIssuedAt(claims.iat)
    }
    if (typeof claims.exp === "number") {
        token.setExpirationTime(claims.exp)
    }
}
