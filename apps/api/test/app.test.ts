import assert from "node:assert/strict"
import type { Server } from "node:http"
import type { AddressInfo } from "node:net"
import { afterEach, test } from "node:test"

import { exportJWK, generateKeyPair, SignJWT } from "jose"
import type { JWK } from "jose"

import { createApp } from "../src/app.js"
import type {
    AccessTokenClaims,
    Account,
    AppDependencies,
    AuthStore,
    DesktopAuthorization,
    GameGrant,
    Identity,
    Installation,
    RefreshFamily,
    RefreshRotationResult,
    RefreshTokenRecord,
    TokenIssuer,
} from "../src/auth-types.js"
import { hashSecret } from "../src/security.js"

const now = 1_800_000_000_000
const issuer = "https://auth.example.test"
const tokenEndpoint = `${issuer}/v1/auth/token`
const servers: Server[] = []

afterEach(async () => {
    for (const server of servers.splice(0)) await server.close()
})

void test("exchanges a one-time desktop code and rotates its bound refresh token", async () => {
    const store = new MemoryAuthStore()
    const issuerAdapter = new RecordingTokenIssuer()
    const dependencies = createDependencies(store, issuerAdapter)
    const { privateKey, publicKey } = await generateKeyPair("ES256")
    const publicJwk = await exportJWK(publicKey)
    const verifier = "v".repeat(43)
    await store.upsertAccount(
        { uid: "user-1", email: "user@example.test" },
        now,
    )
    await store.putGameGrant("user-1", "guild-wars-2", now)

    const approval = await request(
        dependencies,
        "/v1/auth/desktop/authorizations",
        {
            method: "POST",
            headers: {
                authorization: "Bearer web-user",
                "content-type": "application/json",
            },
            body: JSON.stringify({
                code_challenge: hashSecret(verifier),
                redirect_uri: "http://127.0.0.1:49152/callback",
                state: "desktop-state",
                installation_jwk: publicJwk,
            }),
        },
    )
    assert.equal(approval.status, 201)
    const approved = (await approval.json()) as { code: string; state: string }
    assert.equal(approved.state, "desktop-state")
    assert.equal(store.authorizations.has(hashSecret(approved.code)), true)

    const dpop = await createDpop(privateKey, publicJwk)
    const exchange = await request(dependencies, "/v1/auth/token", {
        method: "POST",
        headers: { "content-type": "application/json", dpop },
        body: JSON.stringify({
            grant_type: "authorization_code",
            code: approved.code,
            code_verifier: verifier,
            redirect_uri: "http://127.0.0.1:49152/callback",
        }),
    })
    assert.equal(exchange.status, 200)
    const tokens = (await exchange.json()) as {
        access_token: string
        token_type: string
        expires_in: number
        refresh_token: string
    }
    assert.equal(tokens.token_type, "DPoP")
    assert.equal(tokens.expires_in, 300)
    assert.equal(tokens.access_token, "signed-access-token-1")
    assert.equal(store.refreshTokens.has(tokens.refresh_token), false)
    assert.equal(issuerAdapter.claims[0]?.aud, "gw2-audience")
    assert.equal(issuerAdapter.claims[0]?.scope, "guild-wars-2.assets.read")
    assert.equal(issuerAdapter.claims[0]?.exp, Math.floor(now / 1000) + 300)

    const replay = await request(dependencies, "/v1/auth/token", {
        method: "POST",
        headers: { "content-type": "application/json", dpop },
        body: JSON.stringify({
            grant_type: "authorization_code",
            code: approved.code,
            code_verifier: verifier,
            redirect_uri: "http://127.0.0.1:49152/callback",
        }),
    })
    assert.equal(replay.status, 400)

    const refreshDpop = await createDpop(privateKey, publicJwk)
    const refresh = await request(dependencies, "/v1/auth/token", {
        method: "POST",
        headers: {
            "content-type": "application/x-www-form-urlencoded",
            dpop: refreshDpop,
        },
        body: new URLSearchParams({
            grant_type: "refresh_token",
            refresh_token: tokens.refresh_token,
        }),
    })
    assert.equal(refresh.status, 200)
    const rotated = (await refresh.json()) as { refresh_token: string }
    assert.notEqual(rotated.refresh_token, tokens.refresh_token)

    const proofReplay = await request(dependencies, "/v1/auth/token", {
        method: "POST",
        headers: { "content-type": "application/json", dpop: refreshDpop },
        body: JSON.stringify({
            grant_type: "refresh_token",
            refresh_token: rotated.refresh_token,
        }),
    })
    assert.equal(proofReplay.status, 401)

    const reuse = await request(dependencies, "/v1/auth/token", {
        method: "POST",
        headers: {
            "content-type": "application/json",
            dpop: await createDpop(privateKey, publicJwk),
        },
        body: JSON.stringify({
            grant_type: "refresh_token",
            refresh_token: tokens.refresh_token,
        }),
    })
    assert.equal(reuse.status, 400)
    const family = [...store.refreshFamilies.values()][0]
    assert.equal(family?.revokedAt, now)

    if (family !== undefined) family.revokedAt = undefined
    const revocation = await request(dependencies, "/v1/auth/revoke", {
        method: "POST",
        headers: {
            "content-type": "application/json",
            dpop: await createDpop(privateKey, publicJwk, {
                htu: `${issuer}/v1/auth/revoke`,
            }),
        },
        body: JSON.stringify({ token: rotated.refresh_token }),
    })
    assert.equal(revocation.status, 204)
    assert.equal(family?.revokedAt, now)
})

void test("requires an active game grant without counting its absence as an auth failure", async () => {
    const store = new MemoryAuthStore()
    const dependencies = createDependencies(store, new RecordingTokenIssuer())
    const { publicKey } = await generateKeyPair("ES256")
    const publicJwk = await exportJWK(publicKey)
    const verifier = "a".repeat(43)
    const invalidRedirect = await request(
        dependencies,
        "/v1/auth/desktop/authorizations",
        {
            method: "POST",
            headers: {
                authorization: "Bearer web-user",
                "content-type": "application/json",
            },
            body: JSON.stringify({
                code_challenge: hashSecret(verifier),
                redirect_uri: "http://localhost:40000/callback",
                state: "state",
                installation_jwk: publicJwk,
            }),
        },
    )
    assert.equal(invalidRedirect.status, 400)
    assert.equal(store.authorizations.size, 0)

    const approval = await request(
        dependencies,
        "/v1/auth/desktop/authorizations",
        {
            method: "POST",
            headers: {
                authorization: "Bearer web-user",
                "content-type": "application/json",
            },
            body: JSON.stringify({
                code_challenge: hashSecret(verifier),
                redirect_uri: "http://127.0.0.1:40000/callback",
                state: "state",
                installation_jwk: publicJwk,
            }),
        },
    )
    assert.equal(approval.status, 403)
    assert.deepEqual(await approval.json(), { error: "invalid_game_grant" })
    assert.equal(store.authorizations.size, 0)
    assert.equal(store.installations.size, 0)
    assert.equal(store.ipSecurity.size, 0)
})

void test("enforces admin grants and exposes account endpoints", async () => {
    const store = new MemoryAuthStore()
    store.accounts.set("admin-1", {
        uid: "admin-1",
        platformRole: "admin",
        createdAt: now,
        updatedAt: now,
    })
    store.accounts.set("user-1", {
        uid: "user-1",
        email: "user@example.test",
        platformRole: "user",
        createdAt: now,
        updatedAt: now,
    })
    const dependencies = createDependencies(store, new RecordingTokenIssuer())
    const forbidden = await request(
        dependencies,
        "/v1/admin/accounts/user-1/game-grants/guild-wars-2",
        { method: "PUT", headers: { authorization: "Bearer web-user" } },
    )
    assert.equal(forbidden.status, 403)
    const granted = await request(
        dependencies,
        "/v1/admin/accounts/user-1/game-grants/guild-wars-2",
        { method: "PUT", headers: { authorization: "Bearer web-admin" } },
    )
    assert.equal(granted.status, 200)

    const account = await request(dependencies, "/v1/account", {
        headers: { authorization: "Bearer web-user" },
    })
    assert.deepEqual(await account.json(), {
        uid: "user-1",
        email: "user@example.test",
        platform_role: "user",
    })
    const grants = await request(dependencies, "/v1/account/game-grants", {
        headers: { authorization: "Bearer web-user" },
    })
    assert.equal(
        ((await grants.json()) as { grants: GameGrant[] }).grants[0]?.active,
        true,
    )
})

void test("blocks an IP after six IAM-authenticated failure reports", async () => {
    const store = new MemoryAuthStore()
    const dependencies = createDependencies(store, new RecordingTokenIssuer())
    for (let attempt = 1; attempt <= 6; attempt += 1) {
        const response = await request(
            dependencies,
            "/v1/internal/auth-failures",
            {
                method: "POST",
                headers: {
                    authorization: "Bearer service-token",
                    "content-type": "application/json",
                },
                body: JSON.stringify({ ip: "2001:0db8::1" }),
            },
        )
        const result = (await response.json()) as {
            blocked: boolean
            retry_after_seconds: number
        }
        assert.equal(result.blocked, attempt === 6)
        if (attempt === 6) assert.equal(result.retry_after_seconds, 432000)
    }
    const blocked = await request(dependencies, "/v1/account", {
        headers: {
            authorization: "Bearer web-user",
            "x-forwarded-for": "198.51.100.2, 2001:db8::1",
        },
    })
    assert.equal(blocked.status, 200)

    const rejected = await request(dependencies, "/v1/account", {
        headers: {
            authorization: "Bearer invalid",
            "x-forwarded-for": "198.51.100.2, 2001:db8::1",
        },
    })
    assert.equal(rejected.status, 429)
    assert.equal(rejected.headers.get("retry-after"), "432000")
})

async function createDpop(
    privateKey: CryptoKey,
    publicJwk: JWK,
    options: { athValue?: string; htu?: string } = {},
): Promise<string> {
    return new SignJWT({
        htm: "POST",
        htu: options.htu ?? tokenEndpoint,
        iat: Math.floor(now / 1000),
        jti: crypto.randomUUID(),
        ...(options.athValue === undefined
            ? {}
            : { ath: hashSecret(options.athValue) }),
    })
        .setProtectedHeader({ typ: "dpop+jwt", alg: "ES256", jwk: publicJwk })
        .sign(privateKey)
}

function createDependencies(
    store: AuthStore,
    tokenIssuer: TokenIssuer,
): AppDependencies {
    return {
        store,
        tokenIssuer,
        now: () => now,
        identityVerifier: {
            async verify(token): Promise<Identity> {
                if (token === "web-user") {
                    return { uid: "user-1", email: "user@example.test" }
                }
                if (token === "web-admin") return { uid: "admin-1" }
                throw new Error("invalid token")
            },
        },
        serviceIdentityVerifier: {
            async verify(token) {
                if (token !== "service-token") throw new Error("invalid token")
                return { email: "gw2@example.iam.gserviceaccount.com" }
            },
        },
        config: {
            issuer,
            audience: "gw2-audience",
            webOrigin: "https://web.example.test",
            ipHashSecret: "test-secret-not-used-outside-this-test",
            internalFailureReporterServiceAccounts: new Set([
                "gw2@example.iam.gserviceaccount.com",
            ]),
        },
    }
}

async function request(
    dependencies: AppDependencies,
    path: string,
    init?: RequestInit,
): Promise<Response> {
    const server = createApp(dependencies).listen(0)
    servers.push(server)
    await new Promise<void>((resolve) => server.once("listening", resolve))
    const { port } = server.address() as AddressInfo
    const headers = new Headers(init?.headers)
    if (path === "/v1/auth/token" || path === "/v1/auth/revoke") {
        headers.set("x-forwarded-proto", "https")
        headers.set("x-forwarded-host", "auth.example.test")
    }
    return fetch(`http://127.0.0.1:${port}${path}`, { ...init, headers })
}

class RecordingTokenIssuer implements TokenIssuer {
    public readonly claims: AccessTokenClaims[] = []

    public async issueAccessToken(claims: AccessTokenClaims): Promise<string> {
        this.claims.push(claims)
        return `signed-access-token-${this.claims.length}`
    }

    public async getJwks(): Promise<{ keys: JWK[] }> {
        return { keys: [] }
    }
}

class MemoryAuthStore implements AuthStore {
    public readonly accounts = new Map<string, Account>()
    public readonly grants = new Map<string, GameGrant>()
    public readonly installations = new Map<string, Installation>()
    public readonly authorizations = new Map<string, DesktopAuthorization>()
    public readonly refreshFamilies = new Map<string, RefreshFamily>()
    public readonly refreshTokens = new Map<string, RefreshTokenRecord>()
    public readonly webSessions = new Map<string, import("./auth-types.js").WebSession>()
    public readonly dpopProofs = new Map<string, number>()
    public readonly ipSecurity = new Map<
        string,
        { failures: number[]; blockedUntil?: number }
    >()

    public async upsertAccount(identity: Identity, timestamp: number) {
        const existing = this.accounts.get(identity.uid)
        const account: Account = {
            uid: identity.uid,
            ...(identity.email === undefined ? {} : { email: identity.email }),
            platformRole: existing?.platformRole ?? "user",
            createdAt: existing?.createdAt ?? timestamp,
            updatedAt: timestamp,
        }
        this.accounts.set(identity.uid, account)
        return account
    }

    public async getAccountByEmailHash(emailHash: string) {
        return [...this.accounts.values()].find((account) => account.email && hashSecret(account.email) === emailHash)
    }

    public async createAccount(account: Account, emailHash: string) {
        if (await this.getAccountByEmailHash(emailHash)) throw new Error("email_exists")
        this.accounts.set(account.uid, account)
    }

    public async updatePassword(uid: string, passwordHash: string, timestamp: number) {
        const account = this.accounts.get(uid)
        if (account !== undefined) {
            account.passwordHash = passwordHash
            account.updatedAt = timestamp
        }
    }

    public async createWebSession(session: import("./auth-types.js").WebSession) {
        this.webSessions.set(session.tokenHash, session)
    }

    public async getWebSession(tokenHash: string) {
        return this.webSessions.get(tokenHash)
    }

    public async rotateWebSessionCsrf(tokenHash: string, csrfTokenHash: string, timestamp: number) {
        const session = this.webSessions.get(tokenHash)
        if (session === undefined || session.revokedAt !== undefined || session.expiresAt <= timestamp) return false
        session.csrfTokenHash = csrfTokenHash
        session.lastUsedAt = timestamp
        return true
    }

    public async revokeWebSession(tokenHash: string, timestamp: number) {
        const session = this.webSessions.get(tokenHash)
        if (session !== undefined) session.revokedAt = timestamp
    }

    public async getAccount(uid: string) {
        return this.accounts.get(uid)
    }

    public async listGameGrants(uid: string) {
        return [...this.grants.entries()]
            .filter(([key]) => key.startsWith(`${uid}:`))
            .map(([, grant]) => grant)
    }

    public async getGameGrant(uid: string, game: string) {
        return this.grants.get(`${uid}:${game}`)
    }

    public async putGameGrant(uid: string, game: string, timestamp: number) {
        const existing = this.grants.get(`${uid}:${game}`)
        const grant: GameGrant = {
            gameId: game,
            active: true,
            version: existing?.active
                ? existing.version
                : (existing?.version ?? 0) + 1,
            createdAt: existing?.createdAt ?? timestamp,
            updatedAt: timestamp,
        }
        this.grants.set(`${uid}:${game}`, grant)
        return grant
    }

    public async deleteGameGrant(uid: string, game: string, timestamp: number) {
        const grant = this.grants.get(`${uid}:${game}`)
        if (grant !== undefined) {
            grant.active = false
            grant.updatedAt = timestamp
        }
    }

    public async revokeRefreshFamilies(uid: string, timestamp: number) {
        for (const family of this.refreshFamilies.values()) {
            if (family.uid === uid) family.revokedAt = timestamp
        }
    }

    public async revokeRefreshFamily(familyId: string, timestamp: number) {
        const family = this.refreshFamilies.get(familyId)
        if (family !== undefined) family.revokedAt = timestamp
    }

    public async putInstallation(installation: Installation) {
        this.installations.set(
            `${installation.uid}:${installation.id}`,
            installation,
        )
    }

    public async getInstallation(uid: string, id: string) {
        return this.installations.get(`${uid}:${id}`)
    }

    public async createDesktopAuthorization(value: DesktopAuthorization) {
        this.authorizations.set(value.codeHash, value)
    }

    public async getDesktopAuthorization(codeHash: string) {
        return this.authorizations.get(codeHash)
    }

    public async consumeDesktopAuthorization(
        codeHash: string,
        timestamp: number,
    ) {
        const value = this.authorizations.get(codeHash)
        if (
            value === undefined ||
            value.consumedAt !== undefined ||
            value.expiresAt <= timestamp
        ) {
            return false
        }
        value.consumedAt = timestamp
        return true
    }

    public async createRefreshFamily(
        family: RefreshFamily,
        token: RefreshTokenRecord,
    ) {
        this.refreshFamilies.set(family.id, family)
        this.refreshTokens.set(token.tokenHash, token)
    }

    public async getRefreshContext(tokenHash: string) {
        const token = this.refreshTokens.get(tokenHash)
        if (token === undefined) return undefined
        const family = this.refreshFamilies.get(token.familyId)
        return family === undefined ? undefined : { family, token }
    }

    public async rotateRefreshToken(
        oldHash: string,
        newToken: RefreshTokenRecord,
        timestamp: number,
    ): Promise<RefreshRotationResult> {
        const oldToken = this.refreshTokens.get(oldHash)
        if (oldToken === undefined) return { status: "invalid" }
        const family = this.refreshFamilies.get(oldToken.familyId)
        if (family === undefined) return { status: "invalid" }
        const grant = this.grants.get(`${family.uid}:guild-wars-2`)
        if (
            oldToken.consumedAt !== undefined ||
            family.revokedAt !== undefined
        ) {
            family.revokedAt ??= timestamp
            return { status: "reused" }
        }
        if (
            grant === undefined ||
            !grant.active ||
            grant.version !== family.grantVersion
        ) {
            family.revokedAt = timestamp
            return { status: "invalid" }
        }
        oldToken.consumedAt = timestamp
        this.refreshTokens.set(newToken.tokenHash, newToken)
        return { status: "rotated", family }
    }

    public async consumeDpopProof(
        proofHash: string,
        expiresAt: number,
        timestamp: number,
    ) {
        if ((this.dpopProofs.get(proofHash) ?? 0) > timestamp) return false
        this.dpopProofs.set(proofHash, expiresAt)
        return true
    }

    public async getIpBlock(ipHash: string, timestamp: number) {
        const blockedUntil = this.ipSecurity.get(ipHash)?.blockedUntil ?? 0
        return {
            blocked: blockedUntil > timestamp,
            retryAfterSeconds: Math.ceil(
                Math.max(0, blockedUntil - timestamp) / 1000,
            ),
        }
    }

    public async recordIpFailure(ipHash: string, timestamp: number) {
        const value = this.ipSecurity.get(ipHash) ?? { failures: [] }
        if ((value.blockedUntil ?? 0) <= timestamp) {
            value.failures = value.failures.filter(
                (failure) => failure > timestamp - 60_000,
            )
            value.failures.push(timestamp)
            if (value.failures.length >= 6) {
                value.blockedUntil = timestamp + 5 * 24 * 60 * 60 * 1000
            }
        }
        this.ipSecurity.set(ipHash, value)
        return this.getIpBlock(ipHash, timestamp)
    }
}
