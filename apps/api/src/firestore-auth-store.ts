import { Firestore } from "@google-cloud/firestore"

import type {
    Account,
    AuthStore,
    DesktopAuthorization,
    GameGrant,
    Identity,
    Installation,
    RefreshFamily,
    RefreshRotationResult,
    RefreshTokenRecord,
    WebSession,
} from "./auth-types.js"
import { gameId } from "./auth-types.js"

const failureWindowMs = 60 * 1000
const blockDurationMs = 5 * 24 * 60 * 60 * 1000

export class FirestoreAuthStore implements AuthStore {
    private readonly firestore: Firestore

    public constructor(projectId: string, databaseId: string) {
        this.firestore = new Firestore({ projectId, databaseId })
    }

    public async upsertAccount(
        identity: Identity,
        now: number,
    ): Promise<Account> {
        const reference = this.firestore
            .collection("accounts")
            .doc(identity.uid)
        return this.firestore.runTransaction(async (transaction) => {
            const current = (await transaction.get(reference)).data() as
                Account | undefined
            const account: Account = {
                uid: identity.uid,
                email: identity.email ?? current?.email ?? "",
                passwordHash: current?.passwordHash ?? "",
                platformRole: current?.platformRole ?? "user",
                createdAt: current?.createdAt ?? now,
                updatedAt: now,
            }
            transaction.set(reference, account)
            return account
        })
    }

    public async getAccount(uid: string): Promise<Account | undefined> {
        return (
            await this.firestore.collection("accounts").doc(uid).get()
        ).data() as Account | undefined
    }

    public async getAccountByEmailHash(
        emailHash: string,
    ): Promise<Account | undefined> {
        const index = await this.firestore
            .collection("accountEmailIndexes")
            .doc(emailHash)
            .get()
        const uid = index.data()?.uid as string | undefined
        return uid === undefined ? undefined : this.getAccount(uid)
    }

    public async createAccount(account: Account, emailHash: string): Promise<void> {
        const accountReference = this.firestore.collection("accounts").doc(account.uid)
        const emailReference = this.firestore
            .collection("accountEmailIndexes")
            .doc(emailHash)
        await this.firestore.runTransaction(async (transaction) => {
            if ((await transaction.get(emailReference)).exists) {
                throw new Error("email_exists")
            }
            transaction.create(accountReference, account)
            transaction.create(emailReference, { uid: account.uid, createdAt: account.createdAt })
        })
    }

    public async updatePassword(
        uid: string,
        passwordHash: string,
        now: number,
    ): Promise<void> {
        await this.firestore.collection("accounts").doc(uid).update({ passwordHash, updatedAt: now })
    }

    public async createWebSession(session: WebSession): Promise<void> {
        await this.firestore.collection("webSessions").doc(session.tokenHash).create(session)
    }

    public async getWebSession(tokenHash: string): Promise<WebSession | undefined> {
        return (await this.firestore.collection("webSessions").doc(tokenHash).get())
            .data() as WebSession | undefined
    }

    public async rotateWebSessionCsrf(
        tokenHash: string,
        csrfTokenHash: string,
        now: number,
    ): Promise<boolean> {
        const reference = this.firestore.collection("webSessions").doc(tokenHash)
        return this.firestore.runTransaction(async (transaction) => {
            const session = (await transaction.get(reference)).data() as WebSession | undefined
            if (session === undefined || session.revokedAt !== undefined || session.expiresAt <= now) return false
            transaction.update(reference, { csrfTokenHash, lastUsedAt: now })
            return true
        })
    }

    public async revokeWebSession(tokenHash: string, now: number): Promise<void> {
        const reference = this.firestore.collection("webSessions").doc(tokenHash)
        await this.firestore.runTransaction(async (transaction) => {
            if ((await transaction.get(reference)).exists) transaction.update(reference, { revokedAt: now })
        })
    }

    public async listGameGrants(uid: string): Promise<GameGrant[]> {
        const snapshot = await this.firestore
            .collection("accounts")
            .doc(uid)
            .collection("gameGrants")
            .get()
        return snapshot.docs.map((document) => document.data() as GameGrant)
    }

    public async getGameGrant(
        uid: string,
        game: string,
    ): Promise<GameGrant | undefined> {
        return (
            await this.firestore
                .collection("accounts")
                .doc(uid)
                .collection("gameGrants")
                .doc(game)
                .get()
        ).data() as GameGrant | undefined
    }

    public async putGameGrant(
        uid: string,
        game: string,
        now: number,
    ): Promise<GameGrant> {
        const reference = this.firestore
            .collection("accounts")
            .doc(uid)
            .collection("gameGrants")
            .doc(game)
        return this.firestore.runTransaction(async (transaction) => {
            const current = (await transaction.get(reference)).data() as
                GameGrant | undefined
            const grant: GameGrant = {
                gameId: game,
                active: true,
                version: current?.active
                    ? current.version
                    : (current?.version ?? 0) + 1,
                createdAt: current?.createdAt ?? now,
                updatedAt: now,
            }
            transaction.set(reference, grant)
            return grant
        })
    }

    public async deleteGameGrant(
        uid: string,
        game: string,
        now: number,
    ): Promise<void> {
        const reference = this.firestore
            .collection("accounts")
            .doc(uid)
            .collection("gameGrants")
            .doc(game)
        await this.firestore.runTransaction(async (transaction) => {
            if (!(await transaction.get(reference)).exists) return
            transaction.update(reference, { active: false, updatedAt: now })
        })
    }

    public async revokeRefreshFamilies(
        uid: string,
        now: number,
    ): Promise<void> {
        const snapshot = await this.firestore
            .collection("refreshFamilies")
            .where("uid", "==", uid)
            .get()
        const activeFamilies = snapshot.docs.filter(
            (document) =>
                (document.data() as RefreshFamily).revokedAt === undefined,
        )
        if (activeFamilies.length === 0) return

        const batch = this.firestore.batch()
        for (const document of activeFamilies) {
            batch.update(document.ref, { revokedAt: now })
        }
        await batch.commit()
    }

    public async revokeRefreshFamily(
        familyId: string,
        now: number,
    ): Promise<void> {
        await this.firestore
            .collection("refreshFamilies")
            .doc(familyId)
            .update({ revokedAt: now })
    }

    public async putInstallation(installation: Installation): Promise<void> {
        await this.firestore
            .collection("accounts")
            .doc(installation.uid)
            .collection("installations")
            .doc(installation.id)
            .create(installation)
    }

    public async getInstallation(
        uid: string,
        installationId: string,
    ): Promise<Installation | undefined> {
        return (
            await this.firestore
                .collection("accounts")
                .doc(uid)
                .collection("installations")
                .doc(installationId)
                .get()
        ).data() as Installation | undefined
    }

    public async createDesktopAuthorization(
        authorization: DesktopAuthorization,
    ): Promise<void> {
        await this.firestore
            .collection("desktopAuthorizations")
            .doc(authorization.codeHash)
            .create(authorization)
    }

    public async getDesktopAuthorization(
        codeHash: string,
    ): Promise<DesktopAuthorization | undefined> {
        return (
            await this.firestore
                .collection("desktopAuthorizations")
                .doc(codeHash)
                .get()
        ).data() as DesktopAuthorization | undefined
    }

    public async consumeDesktopAuthorization(
        codeHash: string,
        now: number,
    ): Promise<boolean> {
        const reference = this.firestore
            .collection("desktopAuthorizations")
            .doc(codeHash)
        return this.firestore.runTransaction(async (transaction) => {
            const authorization = (await transaction.get(reference)).data() as
                DesktopAuthorization | undefined
            if (
                authorization === undefined ||
                authorization.consumedAt !== undefined ||
                authorization.expiresAt <= now
            ) {
                return false
            }
            transaction.update(reference, { consumedAt: now })
            return true
        })
    }

    public async createRefreshFamily(
        family: RefreshFamily,
        token: RefreshTokenRecord,
    ): Promise<void> {
        const batch = this.firestore.batch()
        batch.create(
            this.firestore.collection("refreshFamilies").doc(family.id),
            family,
        )
        batch.create(
            this.firestore.collection("refreshTokens").doc(token.tokenHash),
            token,
        )
        await batch.commit()
    }

    public async getRefreshContext(
        tokenHash: string,
    ): Promise<
        { family: RefreshFamily; token: RefreshTokenRecord } | undefined
    > {
        const token = (
            await this.firestore
                .collection("refreshTokens")
                .doc(tokenHash)
                .get()
        ).data() as RefreshTokenRecord | undefined
        if (token === undefined) return undefined
        const family = (
            await this.firestore
                .collection("refreshFamilies")
                .doc(token.familyId)
                .get()
        ).data() as RefreshFamily | undefined
        return family === undefined ? undefined : { family, token }
    }

    public async rotateRefreshToken(
        oldTokenHash: string,
        newToken: RefreshTokenRecord,
        now: number,
    ): Promise<RefreshRotationResult> {
        const oldReference = this.firestore
            .collection("refreshTokens")
            .doc(oldTokenHash)
        return this.firestore.runTransaction(async (transaction) => {
            const oldToken = (await transaction.get(oldReference)).data() as
                RefreshTokenRecord | undefined
            if (oldToken === undefined) return { status: "invalid" }
            const familyReference = this.firestore
                .collection("refreshFamilies")
                .doc(oldToken.familyId)
            const family = (await transaction.get(familyReference)).data() as
                RefreshFamily | undefined
            if (family === undefined) return { status: "invalid" }
            const grant = (
                await transaction.get(
                    this.firestore
                        .collection("accounts")
                        .doc(family.uid)
                        .collection("gameGrants")
                        .doc(gameId),
                )
            ).data() as GameGrant | undefined
            if (
                oldToken.consumedAt !== undefined ||
                family.revokedAt !== undefined
            ) {
                if (family.revokedAt === undefined) {
                    transaction.update(familyReference, { revokedAt: now })
                }
                return { status: "reused" }
            }
            if (
                grant === undefined ||
                !grant.active ||
                grant.version !== family.grantVersion
            ) {
                transaction.update(familyReference, { revokedAt: now })
                return { status: "invalid" }
            }
            if (oldToken.expiresAt <= now || family.expiresAt <= now) {
                return { status: "invalid" }
            }
            transaction.update(oldReference, { consumedAt: now })
            transaction.create(
                this.firestore
                    .collection("refreshTokens")
                    .doc(newToken.tokenHash),
                newToken,
            )
            return { status: "rotated", family }
        })
    }

    public async consumeDpopProof(
        proofHash: string,
        expiresAt: number,
        now: number,
    ): Promise<boolean> {
        const reference = this.firestore.collection("dpopProofs").doc(proofHash)
        return this.firestore.runTransaction(async (transaction) => {
            const existing = (await transaction.get(reference)).data() as
                { expiresAt?: { toMillis(): number } } | undefined
            if ((existing?.expiresAt?.toMillis() ?? 0) > now) return false
            transaction.set(reference, { expiresAt: new Date(expiresAt) })
            return true
        })
    }

    public async getIpBlock(
        ipHash: string,
        now: number,
    ): Promise<{ blocked: boolean; retryAfterSeconds: number }> {
        const data = (
            await this.firestore.collection("ipSecurity").doc(ipHash).get()
        ).data() as { blockedUntil?: number } | undefined
        return blockResult(data?.blockedUntil, now)
    }

    public async recordIpFailure(
        ipHash: string,
        now: number,
    ): Promise<{ blocked: boolean; retryAfterSeconds: number }> {
        const reference = this.firestore.collection("ipSecurity").doc(ipHash)
        return this.firestore.runTransaction(async (transaction) => {
            const current = (await transaction.get(reference)).data() as
                | { failureTimestamps?: number[]; blockedUntil?: number }
                | undefined
            if ((current?.blockedUntil ?? 0) > now) {
                return blockResult(current?.blockedUntil, now)
            }
            const failures = (current?.failureTimestamps ?? []).filter(
                (timestamp) => timestamp > now - failureWindowMs,
            )
            failures.push(now)
            const blockedUntil =
                failures.length >= 6 ? now + blockDurationMs : undefined
            transaction.set(reference, {
                failureTimestamps: blockedUntil === undefined ? failures : [],
                ...(blockedUntil === undefined ? {} : { blockedUntil }),
                updatedAt: now,
            })
            return blockResult(blockedUntil, now)
        })
    }
}

function blockResult(
    blockedUntil: number | undefined,
    now: number,
): { blocked: boolean; retryAfterSeconds: number } {
    const remaining = Math.max(0, (blockedUntil ?? 0) - now)
    return {
        blocked: remaining > 0,
        retryAfterSeconds: Math.ceil(remaining / 1000),
    }
}
