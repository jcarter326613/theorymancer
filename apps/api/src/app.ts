import { randomBytes as nodeRandomBytes } from "node:crypto"

import cors from "cors"
import express from "express"
import type { Express, NextFunction, Request, Response } from "express"
import type { JWK } from "jose"
import { z } from "zod"

import { gameId, gameScope } from "./auth-types.js"
import type {
    AppDependencies,
    Identity,
    RefreshFamily,
    RefreshTokenRecord,
} from "./auth-types.js"
import {
    clientIp,
    constantTimeEqual,
    extractBearer,
    hashIp,
    hashSecret,
    normalizeIp,
    validatePublicP256Jwk,
    verifyDpopProof,
} from "./security.js"

const authorizationLifetimeMs = 5 * 60 * 1000
const refreshLifetimeMs = 30 * 24 * 60 * 60 * 1000
const codeChallengePattern = /^[A-Za-z0-9_-]{43}$/
const codeVerifierPattern = /^[A-Za-z0-9._~-]{43,128}$/
const gameIdSchema = z.literal(gameId)

const desktopAuthorizationSchema = z.object({
    code_challenge: z.string().regex(codeChallengePattern),
    redirect_uri: z.string().refine(isLoopbackRedirect),
    state: z.string().min(1).max(1024),
    installation_jwk: z
        .object({
            kty: z.string(),
            crv: z.string(),
            x: z.string(),
            y: z.string(),
        })
        .passthrough(),
})

const tokenRequestSchema = z.discriminatedUnion("grant_type", [
    z.object({
        grant_type: z.literal("authorization_code"),
        code: z.string().min(1).max(1024),
        code_verifier: z.string().regex(codeVerifierPattern),
        redirect_uri: z.string(),
    }),
    z.object({
        grant_type: z.literal("refresh_token"),
        refresh_token: z.string().min(1).max(2048),
    }),
])
const revocationRequestSchema = z.object({ token: z.string().min(1).max(2048) })

const internalFailureSchema = z.object({ ip: z.string().min(1).max(128) })

export function createApp(dependencies: AppDependencies): Express {
    const app = express()
    const now = dependencies.now ?? Date.now
    const randomBytes = dependencies.randomBytes ?? nodeRandomBytes
    app.disable("x-powered-by")
    app.use(
        cors({
            origin: dependencies.config.webOrigin,
            methods: ["GET", "POST", "PUT", "DELETE"],
            allowedHeaders: ["Authorization", "Content-Type", "DPoP"],
        }),
    )
    app.use(express.json({ limit: "32kb" }))
    app.use(express.urlencoded({ extended: false, limit: "32kb" }))

    app.get("/health", (_request, response) => {
        response.json({ status: "ok", service: "api" })
    })

    app.get("/.well-known/openid-configuration", (request, response) => {
        const issuer = dependencies.config.issuer.replace(/\/$/, "")
        const publicOrigin = externallyObservedOrigin(request)
        response.json({
            issuer,
            jwks_uri: `${publicOrigin}/.well-known/jwks.json`,
            token_endpoint: `${publicOrigin}/v1/auth/token`,
            grant_types_supported: ["authorization_code", "refresh_token"],
            token_endpoint_auth_methods_supported: ["none"],
            id_token_signing_alg_values_supported: ["RS256"],
            scopes_supported: [gameScope],
        })
    })

    app.get("/.well-known/jwks.json", async (_request, response) => {
        response.set("Cache-Control", "public, max-age=300")
        response.json(await dependencies.tokenIssuer.getJwks())
    })

    app.post("/v1/auth/desktop/authorizations", async (request, response) => {
        response.set("Cache-Control", "no-store")
        const identity = await authenticateWeb(request, response)
        if (identity === undefined) return
        const parsed = desktopAuthorizationSchema.safeParse(request.body)
        if (!parsed.success) {
            response.status(400).json({ error: "invalid_request" })
            return
        }

        let installationJkt: string
        try {
            installationJkt = await validatePublicP256Jwk(
                parsed.data.installation_jwk as JWK,
            )
        } catch {
            response.status(400).json({ error: "invalid_request" })
            return
        }

        const timestamp = now()
        const account = await dependencies.store.upsertAccount(
            identity,
            timestamp,
        )
        const grant = await dependencies.store.getGameGrant(account.uid, gameId)
        if (grant?.active !== true) {
            response.status(403).json({ error: "invalid_game_grant" })
            return
        }
        const installationId = randomBytes(18).toString("base64url")
        const code = randomBytes(32).toString("base64url")
        const installationJwk = parsed.data.installation_jwk as JWK
        await dependencies.store.putInstallation({
            id: installationId,
            uid: account.uid,
            publicJwk: installationJwk,
            jwkThumbprint: installationJkt,
            active: true,
            createdAt: timestamp,
            updatedAt: timestamp,
        })
        await dependencies.store.createDesktopAuthorization({
            codeHash: hashSecret(code),
            uid: account.uid,
            codeChallenge: parsed.data.code_challenge,
            redirectUri: parsed.data.redirect_uri,
            installationId,
            installationJwk,
            installationJkt,
            grantVersion: grant.version,
            createdAt: timestamp,
            expiresAt: timestamp + authorizationLifetimeMs,
        })
        response.status(201).json({ code, state: parsed.data.state })
    })

    app.post("/v1/auth/token", async (request, response) => {
        response.set("Cache-Control", "no-store")
        const parsed = tokenRequestSchema.safeParse(request.body)
        if (!parsed.success) {
            await sendAuthenticationFailure(
                request,
                response,
                400,
                "invalid_request",
            )
            return
        }

        if (parsed.data.grant_type === "authorization_code") {
            const codeHash = hashSecret(parsed.data.code)
            const authorization =
                await dependencies.store.getDesktopAuthorization(codeHash)
            const timestamp = now()
            const expectedChallenge = hashSecret(parsed.data.code_verifier)
            if (
                authorization === undefined ||
                authorization.consumedAt !== undefined ||
                authorization.expiresAt <= timestamp ||
                parsed.data.redirect_uri !== authorization.redirectUri ||
                !constantTimeEqual(
                    expectedChallenge,
                    authorization.codeChallenge,
                )
            ) {
                await sendAuthenticationFailure(
                    request,
                    response,
                    400,
                    "invalid_grant",
                )
                return
            }
            try {
                const proof = await verifyDpopProof({
                    proof: request.get("DPoP"),
                    expectedJwk: authorization.installationJwk,
                    expectedJkt: authorization.installationJkt,
                    expectedHtu: externallyObservedUrl(request),
                    now: timestamp,
                })
                if (
                    !(await dependencies.store.consumeDpopProof(
                        hashSecret(
                            `${authorization.installationJkt}:${proof.jti}`,
                        ),
                        proof.expiresAt,
                        timestamp,
                    ))
                ) {
                    throw new Error("DPoP proof replayed")
                }
            } catch {
                await sendAuthenticationFailure(
                    request,
                    response,
                    401,
                    "invalid_dpop_proof",
                )
                return
            }
            const accountStatus = await getAccountStatus(authorization.uid)
            if (accountStatus === "missing_account") {
                await sendAuthenticationFailure(
                    request,
                    response,
                    400,
                    "invalid_grant",
                )
                return
            }
            if (accountStatus === "invalid_grant") {
                response.status(403).json({ error: "invalid_game_grant" })
                return
            }
            const grant = await dependencies.store.getGameGrant(
                authorization.uid,
                gameId,
            )
            if (
                grant === undefined ||
                !grant.active ||
                grant.version !== authorization.grantVersion
            ) {
                response.status(403).json({ error: "invalid_game_grant" })
                return
            }
            if (
                !(await dependencies.store.consumeDesktopAuthorization(
                    codeHash,
                    timestamp,
                ))
            ) {
                await sendAuthenticationFailure(
                    request,
                    response,
                    400,
                    "invalid_grant",
                )
                return
            }
            await issueTokens(
                response,
                authorization.uid,
                authorization.installationId,
                authorization.installationJkt,
                grant.version,
                timestamp,
            )
            return
        }

        const timestamp = now()
        const oldTokenHash = hashSecret(parsed.data.refresh_token)
        const context = await dependencies.store.getRefreshContext(oldTokenHash)
        if (
            context === undefined ||
            context.token.expiresAt <= timestamp ||
            context.family.expiresAt <= timestamp ||
            context.family.revokedAt !== undefined
        ) {
            await sendAuthenticationFailure(
                request,
                response,
                400,
                "invalid_grant",
            )
            return
        }
        const installation = await dependencies.store.getInstallation(
            context.family.uid,
            context.family.installationId,
        )
        if (installation === undefined || !installation.active) {
            await sendAuthenticationFailure(
                request,
                response,
                400,
                "invalid_grant",
            )
            return
        }
        try {
            const proof = await verifyDpopProof({
                proof: request.get("DPoP"),
                expectedJwk: installation.publicJwk,
                expectedJkt: context.family.installationJkt,
                expectedHtu: externallyObservedUrl(request),
                now: timestamp,
            })
            if (
                !(await dependencies.store.consumeDpopProof(
                    hashSecret(
                        `${context.family.installationJkt}:${proof.jti}`,
                    ),
                    proof.expiresAt,
                    timestamp,
                ))
            ) {
                throw new Error("DPoP proof replayed")
            }
        } catch {
            await sendAuthenticationFailure(
                request,
                response,
                401,
                "invalid_dpop_proof",
            )
            return
        }
        const accountStatus = await getAccountStatus(context.family.uid)
        if (accountStatus === "missing_account") {
            await sendAuthenticationFailure(
                request,
                response,
                400,
                "invalid_grant",
            )
            return
        }
        if (accountStatus === "invalid_grant") {
            response.status(403).json({ error: "invalid_game_grant" })
            return
        }

        const refreshToken = randomBytes(32).toString("base64url")
        const accessToken = await createAccessToken(
            context.family.uid,
            context.family.installationId,
            context.family.installationJkt,
            timestamp,
        )
        const rotation = await dependencies.store.rotateRefreshToken(
            oldTokenHash,
            {
                tokenHash: hashSecret(refreshToken),
                familyId: context.family.id,
                createdAt: timestamp,
                expiresAt: context.family.expiresAt,
            },
            timestamp,
        )
        if (rotation.status !== "rotated") {
            await sendAuthenticationFailure(
                request,
                response,
                400,
                "invalid_grant",
            )
            return
        }
        await sendTokenResponse(response, refreshToken, accessToken)
    })

    app.post("/v1/auth/revoke", async (request, response) => {
        const parsed = revocationRequestSchema.safeParse(request.body)
        if (!parsed.success) {
            response.status(400).json({ error: "invalid_request" })
            return
        }
        const timestamp = now()
        const context = await dependencies.store.getRefreshContext(
            hashSecret(parsed.data.token),
        )
        if (context === undefined) {
            response.sendStatus(204)
            return
        }
        const installation = await dependencies.store.getInstallation(
            context.family.uid,
            context.family.installationId,
        )
        if (installation === undefined || !installation.active) {
            response.sendStatus(204)
            return
        }
        try {
            const proof = await verifyDpopProof({
                proof: request.get("DPoP"),
                expectedJwk: installation.publicJwk,
                expectedJkt: context.family.installationJkt,
                expectedHtu: externallyObservedUrl(request),
                now: timestamp,
            })
            if (
                !(await dependencies.store.consumeDpopProof(
                    hashSecret(
                        `${context.family.installationJkt}:${proof.jti}`,
                    ),
                    proof.expiresAt,
                    timestamp,
                ))
            ) {
                throw new Error("DPoP proof replayed")
            }
        } catch {
            await sendAuthenticationFailure(
                request,
                response,
                401,
                "invalid_dpop_proof",
            )
            return
        }
        await dependencies.store.revokeRefreshFamily(
            context.family.id,
            timestamp,
        )
        response.sendStatus(204)
    })

    app.get("/v1/account", async (request, response) => {
        const identity = await authenticateWeb(request, response)
        if (identity === undefined) return
        const account = await dependencies.store.upsertAccount(identity, now())
        response.json(publicAccount(account))
    })

    app.get("/v1/account/game-grants", async (request, response) => {
        const identity = await authenticateWeb(request, response)
        if (identity === undefined) return
        response.json({
            grants: await dependencies.store.listGameGrants(identity.uid),
        })
    })

    app.put(
        "/v1/admin/accounts/:uid/game-grants/:gameId",
        async (request, response) => {
            const admin = await authenticateAdmin(request, response)
            if (admin === undefined) return
            const parsedGame = gameIdSchema.safeParse(request.params.gameId)
            if (!parsedGame.success) {
                response.status(400).json({ error: "unsupported_game" })
                return
            }
            if (
                (await dependencies.store.getAccount(request.params.uid)) ===
                undefined
            ) {
                response.sendStatus(404)
                return
            }
            response.json(
                await dependencies.store.putGameGrant(
                    request.params.uid,
                    parsedGame.data,
                    now(),
                ),
            )
        },
    )

    app.delete(
        "/v1/admin/accounts/:uid/game-grants/:gameId",
        async (request, response) => {
            const admin = await authenticateAdmin(request, response)
            if (admin === undefined) return
            const parsedGame = gameIdSchema.safeParse(request.params.gameId)
            if (!parsedGame.success) {
                response.status(400).json({ error: "unsupported_game" })
                return
            }
            const timestamp = now()
            await dependencies.store.deleteGameGrant(
                request.params.uid,
                parsedGame.data,
                timestamp,
            )
            await dependencies.store.revokeRefreshFamilies(
                request.params.uid,
                timestamp,
            )
            response.sendStatus(204)
        },
    )

    app.post("/v1/internal/auth-failures", async (request, response) => {
        const bearer = extractBearer(request.get("Authorization"))
        if (bearer === undefined) {
            response.sendStatus(401)
            return
        }
        let caller: { email: string }
        try {
            caller = await dependencies.serviceIdentityVerifier.verify(bearer)
        } catch {
            response.sendStatus(401)
            return
        }
        if (
            !dependencies.config.internalFailureReporterServiceAccounts.has(
                caller.email.toLowerCase(),
            )
        ) {
            response.sendStatus(403)
            return
        }
        const parsed = internalFailureSchema.safeParse(request.body)
        const ip = parsed.success ? normalizeIp(parsed.data.ip) : undefined
        if (ip === undefined) {
            response.status(400).json({ error: "invalid_request" })
            return
        }
        const result = await dependencies.store.recordIpFailure(
            hashIp(ip, dependencies.config.ipHashSecret),
            now(),
        )
        response.json({
            blocked: result.blocked,
            retry_after_seconds: result.retryAfterSeconds,
        })
    })

    app.use(
        async (
            error: unknown,
            request: Request,
            response: Response,
            next: NextFunction,
        ) => {
            if (response.headersSent) {
                next(error)
                return
            }
            if (isRequestBodyError(error)) {
                if (request.path === "/v1/auth/token") {
                    await sendAuthenticationFailure(
                        request,
                        response,
                        400,
                        "invalid_request",
                    )
                    return
                }
                response.status(400).json({ error: "invalid_request" })
                return
            }
            response.status(500).json({ error: "internal_error" })
        },
    )

    return app

    async function authenticateWeb(
        request: Request,
        response: Response,
    ): Promise<Identity | undefined> {
        const bearer = extractBearer(request.get("Authorization"))
        if (bearer === undefined) {
            await sendAuthenticationFailure(
                request,
                response,
                401,
                "invalid_token",
            )
            return undefined
        }
        try {
            return await dependencies.identityVerifier.verify(bearer)
        } catch {
            await sendAuthenticationFailure(
                request,
                response,
                401,
                "invalid_token",
            )
            return undefined
        }
    }

    async function authenticateAdmin(
        request: Request,
        response: Response,
    ): Promise<Identity | undefined> {
        const identity = await authenticateWeb(request, response)
        if (identity === undefined) return undefined
        const account = await dependencies.store.getAccount(identity.uid)
        if (account?.platformRole !== "admin") {
            response.sendStatus(403)
            return undefined
        }
        return identity
    }

    async function getAccountStatus(
        uid: string,
    ): Promise<"active" | "missing_account" | "invalid_grant"> {
        const [account, grant] = await Promise.all([
            dependencies.store.getAccount(uid),
            dependencies.store.getGameGrant(uid, gameId),
        ])
        if (account === undefined) return "missing_account"
        return grant?.active === true ? "active" : "invalid_grant"
    }

    async function issueTokens(
        response: Response,
        uid: string,
        installationId: string,
        installationJkt: string,
        grantVersion: number,
        timestamp: number,
    ): Promise<void> {
        const refreshToken = randomBytes(32).toString("base64url")
        const accessToken = await createAccessToken(
            uid,
            installationId,
            installationJkt,
            timestamp,
        )
        const family: RefreshFamily = {
            id: randomBytes(18).toString("base64url"),
            uid,
            installationId,
            installationJkt,
            grantVersion,
            createdAt: timestamp,
            expiresAt: timestamp + refreshLifetimeMs,
        }
        const record: RefreshTokenRecord = {
            tokenHash: hashSecret(refreshToken),
            familyId: family.id,
            createdAt: timestamp,
            expiresAt: family.expiresAt,
        }
        await dependencies.store.createRefreshFamily(family, record)
        await sendTokenResponse(response, refreshToken, accessToken)
    }

    async function sendTokenResponse(
        response: Response,
        refreshToken: string,
        accessToken: string,
    ): Promise<void> {
        response.set("Cache-Control", "no-store")
        response.json({
            access_token: accessToken,
            token_type: "DPoP",
            expires_in: 300,
            refresh_token: refreshToken,
            scope: gameScope,
        })
    }

    async function createAccessToken(
        uid: string,
        installationId: string,
        installationJkt: string,
        timestamp: number,
    ): Promise<string> {
        const issuedAt = Math.floor(timestamp / 1000)
        return dependencies.tokenIssuer.issueAccessToken({
            iss: dependencies.config.issuer.replace(/\/$/, ""),
            aud: dependencies.config.audience,
            sub: uid,
            iat: issuedAt,
            exp: issuedAt + 300,
            jti: randomBytes(18).toString("base64url"),
            installation_id: installationId,
            scope: gameScope,
            cnf: { jkt: installationJkt },
        })
    }

    async function sendAuthenticationFailure(
        request: Request,
        response: Response,
        status: number,
        error: string,
    ): Promise<void> {
        const ip = clientIp(request)
        const block =
            ip === undefined
                ? undefined
                : await dependencies.store.recordIpFailure(
                      hashIp(ip, dependencies.config.ipHashSecret),
                      now(),
                  )
        if (block?.blocked === true) {
            response
                .status(429)
                .set("Retry-After", String(block.retryAfterSeconds))
                .json({
                    error: "temporarily_blocked",
                    retry_after_seconds: block.retryAfterSeconds,
                })
            return
        }
        response.status(status).json({ error })
    }
}

function externallyObservedOrigin(request: Request): string {
    const protocol =
        firstForwardedValue(request.get("x-forwarded-proto")) ??
        request.protocol
    const host =
        firstForwardedValue(request.get("x-forwarded-host")) ??
        request.get("host")
    if (host === undefined) throw new Error("Request host is unavailable")
    return `${protocol}://${host}`
}

function externallyObservedUrl(request: Request): string {
    const url = new URL(request.originalUrl, externallyObservedOrigin(request))
    url.search = ""
    url.hash = ""
    return url.href
}

function firstForwardedValue(value: string | undefined): string | undefined {
    return value?.split(",", 1)[0]?.trim()
}

function publicAccount(account: {
    uid: string
    email?: string
    platformRole: "user" | "admin"
}): object {
    return {
        uid: account.uid,
        ...(account.email === undefined ? {} : { email: account.email }),
        platform_role: account.platformRole,
    }
}

function isLoopbackRedirect(value: string): boolean {
    try {
        const url = new URL(value)
        const port = Number.parseInt(url.port, 10)
        return (
            url.protocol === "http:" &&
            url.hostname === "127.0.0.1" &&
            url.pathname === "/callback" &&
            url.username === "" &&
            url.password === "" &&
            url.search === "" &&
            url.hash === "" &&
            Number.isInteger(port) &&
            port >= 1 &&
            port <= 65535
        )
    } catch {
        return false
    }
}

function isRequestBodyError(error: unknown): boolean {
    return (
        typeof error === "object" &&
        error !== null &&
        "type" in error &&
        (error.type === "entity.parse.failed" ||
            error.type === "entity.too.large")
    )
}
