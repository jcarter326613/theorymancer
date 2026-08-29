import type { JWK, JWTPayload } from "jose"

export const gameId = "guild-wars-2"
export const gameScope = "guild-wars-2.assets.read"

export interface Identity {
    uid: string
    email?: string
}

export interface IdentityVerifier {
    verify(token: string): Promise<Identity>
}

export interface ServiceIdentityVerifier {
    verify(token: string): Promise<{ email: string }>
}

export interface Account {
    uid: string
    email?: string
    platformRole: "user" | "admin"
    createdAt: number
    updatedAt: number
}

export interface GameGrant {
    gameId: string
    active: boolean
    version: number
    createdAt: number
    updatedAt: number
}

export interface Installation {
    id: string
    uid: string
    publicJwk: JWK
    jwkThumbprint: string
    active: boolean
    createdAt: number
    updatedAt: number
}

export interface DesktopAuthorization {
    codeHash: string
    uid: string
    codeChallenge: string
    redirectUri: string
    installationId: string
    installationJwk: JWK
    installationJkt: string
    grantVersion: number
    expiresAt: number
    consumedAt?: number
    createdAt: number
}

export interface RefreshFamily {
    id: string
    uid: string
    installationId: string
    installationJkt: string
    grantVersion: number
    createdAt: number
    expiresAt: number
    revokedAt?: number
}

export interface RefreshTokenRecord {
    tokenHash: string
    familyId: string
    createdAt: number
    expiresAt: number
    consumedAt?: number
}

export type RefreshRotationResult =
    | { status: "rotated"; family: RefreshFamily }
    | { status: "invalid" | "reused" }

export interface AuthStore {
    upsertAccount(identity: Identity, now: number): Promise<Account>
    getAccount(uid: string): Promise<Account | undefined>
    listGameGrants(uid: string): Promise<GameGrant[]>
    getGameGrant(uid: string, game: string): Promise<GameGrant | undefined>
    putGameGrant(uid: string, game: string, now: number): Promise<GameGrant>
    deleteGameGrant(uid: string, game: string, now: number): Promise<void>
    revokeRefreshFamilies(uid: string, now: number): Promise<void>
    revokeRefreshFamily(familyId: string, now: number): Promise<void>
    putInstallation(installation: Installation): Promise<void>
    getInstallation(
        uid: string,
        installationId: string,
    ): Promise<Installation | undefined>
    createDesktopAuthorization(
        authorization: DesktopAuthorization,
    ): Promise<void>
    getDesktopAuthorization(
        codeHash: string,
    ): Promise<DesktopAuthorization | undefined>
    consumeDesktopAuthorization(codeHash: string, now: number): Promise<boolean>
    createRefreshFamily(
        family: RefreshFamily,
        token: RefreshTokenRecord,
    ): Promise<void>
    getRefreshContext(
        tokenHash: string,
    ): Promise<{ family: RefreshFamily; token: RefreshTokenRecord } | undefined>
    rotateRefreshToken(
        oldTokenHash: string,
        newToken: RefreshTokenRecord,
        now: number,
    ): Promise<RefreshRotationResult>
    consumeDpopProof(
        proofHash: string,
        expiresAt: number,
        now: number,
    ): Promise<boolean>
    getIpBlock(
        ipHash: string,
        now: number,
    ): Promise<{ blocked: boolean; retryAfterSeconds: number }>
    recordIpFailure(
        ipHash: string,
        now: number,
    ): Promise<{ blocked: boolean; retryAfterSeconds: number }>
}

export interface AccessTokenClaims extends JWTPayload {
    sub: string
    jti: string
    installation_id: string
    scope: string
    cnf: { jkt: string }
}

export interface TokenIssuer {
    issueAccessToken(claims: AccessTokenClaims): Promise<string>
    getJwks(): Promise<{ keys: JWK[] }>
}

export interface AppConfig {
    issuer: string
    audience: string
    webOrigin: string
    internalFailureReporterServiceAccounts: ReadonlySet<string>
    ipHashSecret: string
}

export interface AppDependencies {
    store: AuthStore
    identityVerifier: IdentityVerifier
    serviceIdentityVerifier: ServiceIdentityVerifier
    tokenIssuer: TokenIssuer
    config: AppConfig
    now?: () => number
    randomBytes?: (size: number) => Buffer
}
