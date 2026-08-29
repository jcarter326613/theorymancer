import express from "express"
import type { Express, NextFunction, Request, Response } from "express"
import { z } from "zod"

import { AuthenticationRejectedError } from "./auth.js"
import type { Authenticator } from "./auth.js"
import type { AuthFailureReporter } from "./auth-failure-reporter.js"

const manifestObjectPath = "guild-wars-2/icons.manifest.json"
const assetIdPattern = /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/

const manifestAssetSchema = z
    .object({
        asset_id: z.string().regex(assetIdPattern),
        source_url: z.string().url(),
        object_path: z.string(),
    })
    .superRefine((asset, context) => {
        if (asset.object_path !== `guild-wars-2/icons/${asset.asset_id}.png`) {
            context.addIssue({
                code: z.ZodIssueCode.custom,
                message: "Icon object path must be addressed by its asset ID.",
            })
        }
    })

const manifestSkillSchema = z.object({
    skill_id: z.number().int().nonnegative(),
    name: z.string(),
    type: z.string().nullable(),
    professions: z.array(z.string()),
    weapon_type: z.string().nullable(),
    slot: z.string().nullable(),
    specialization_ids: z.array(z.number().int().nonnegative()),
    categories: z.array(z.string()),
    attunement: z.string().nullable(),
    icon_asset_id: z.string().regex(assetIdPattern),
})

const manifestEffectSchema = z.object({
    name: z.string().min(1),
    fact_type: z.string().min(1),
    description: z.string().nullable(),
    icon_asset_id: z.string().regex(assetIdPattern),
})

const manifestSchema = z
    .object({
        version: z.literal(2),
        assets: z.array(manifestAssetSchema),
        skills: z.array(manifestSkillSchema),
        effects: z.array(manifestEffectSchema),
    })
    .superRefine((manifest, context) => {
        const assetIds = new Set<string>()
        for (const asset of manifest.assets) {
            if (assetIds.has(asset.asset_id)) {
                context.addIssue({
                    code: z.ZodIssueCode.custom,
                    message: `Duplicate asset ID: ${asset.asset_id}.`,
                })
            }
            assetIds.add(asset.asset_id)
        }

        for (const entry of [...manifest.skills, ...manifest.effects]) {
            if (!assetIds.has(entry.icon_asset_id)) {
                context.addIssue({
                    code: z.ZodIssueCode.custom,
                    message: `Unknown icon asset ID: ${entry.icon_asset_id}.`,
                })
            }
        }
    })

type IconManifest = z.infer<typeof manifestSchema>

export function parseManifest(contents: string): IconManifest {
    return manifestSchema.parse(JSON.parse(contents))
}

export interface ObjectStore {
    download(objectPath: string): Promise<Buffer>
}

export interface CreateAppOptions {
    objectStore: ObjectStore
    authenticator: Authenticator
    authFailureReporter: AuthFailureReporter
}

export function createApp(options: CreateAppOptions): Express {
    const app = express()

    app.get("/health", (_request, response) => {
        response.json({ status: "ok", service: "guild-wars-2-api" })
    })

    app.use("/icons", createAuthenticationMiddleware(options))

    app.get("/icons/manifest", async (_request, response) => {
        const manifest = await loadManifest(options.objectStore)
        if (manifest === undefined) {
            response.status(500).json({
                error: "The Guild Wars 2 icon manifest is unavailable.",
            })
            return
        }

        response.json(manifest)
    })

    app.get("/icons/:assetId.png", async (request, response) => {
        const { assetId } = request.params
        if (!assetIdPattern.test(assetId)) {
            response.sendStatus(404)
            return
        }

        const manifest = await loadManifest(options.objectStore)
        if (manifest === undefined) {
            response.status(500).json({
                error: "The Guild Wars 2 icon manifest is unavailable.",
            })
            return
        }

        const asset = manifest.assets.find(
            (entry) => entry.asset_id === assetId,
        )
        if (asset === undefined) {
            response.sendStatus(404)
            return
        }

        try {
            const iconBytes = await options.objectStore.download(
                asset.object_path,
            )
            response
                .type("png")
                .set("Cache-Control", "private, max-age=31536000, immutable")
                .send(iconBytes)
        } catch (error) {
            if (isNotFound(error)) {
                response.sendStatus(404)
                return
            }

            response.status(500).json({
                error: "The requested Guild Wars 2 icon is unavailable.",
            })
        }
    })

    return app
}

function createAuthenticationMiddleware(options: CreateAppOptions) {
    return async (
        request: Request,
        response: Response,
        next: NextFunction,
    ): Promise<void> => {
        try {
            await options.authenticator.authenticate({
                accessToken: parseDpopAuthorization(
                    request.get("authorization"),
                ),
                dpopProof: request.get("dpop"),
                method: request.method,
                url: externallyObservedUrl(request),
            })
            next()
            return
        } catch (error) {
            response.set("Cache-Control", "no-store")
            if (!(error instanceof AuthenticationRejectedError)) {
                response.status(503).json({
                    error: "Authentication verification is unavailable.",
                })
                return
            }
            let report
            try {
                report = await options.authFailureReporter.report(
                    clientIp(request),
                )
            } catch {
                response.status(503).json({
                    error: "Authentication failure tracking is unavailable.",
                })
                return
            }

            if (report.blocked) {
                response
                    .set(
                        "Retry-After",
                        String(Math.max(0, report.retryAfterSeconds ?? 0)),
                    )
                    .status(429)
                    .json({ error: "Too many authentication failures." })
                return
            }

            response
                .set("WWW-Authenticate", "DPoP")
                .status(401)
                .json({ error: "Valid DPoP authentication is required." })
        }
    }
}

function parseDpopAuthorization(value: string | undefined): string | undefined {
    if (value === undefined) {
        return undefined
    }
    const match = /^DPoP ([^\s]+)$/iu.exec(value)
    return match?.[1]
}

function externallyObservedUrl(request: Request): string {
    const forwardedProtocol = firstForwardedValue(
        request.get("x-forwarded-proto"),
    )
    const protocol = forwardedProtocol ?? request.protocol
    const host = request.get("host")
    if (host === undefined) {
        throw new Error("The request host is unavailable.")
    }

    const url = new URL(request.originalUrl, `${protocol}://${host}`)
    url.search = ""
    url.hash = ""
    return url.href
}

function firstForwardedValue(value: string | undefined): string | undefined {
    return value?.split(",", 1)[0]?.trim()
}

function clientIp(request: Request): string {
    const forwardedFor = request.get("x-forwarded-for")
    if (forwardedFor !== undefined) {
        const values = forwardedFor.split(",")
        const rightmost = values.at(-1)?.trim()
        if (rightmost !== undefined && rightmost.length > 0) {
            return rightmost
        }
    }
    return request.socket.remoteAddress ?? "unknown"
}

async function loadManifest(
    objectStore: ObjectStore,
): Promise<IconManifest | undefined> {
    try {
        return parseManifest(
            (await objectStore.download(manifestObjectPath)).toString("utf8"),
        )
    } catch {
        return undefined
    }
}

function isNotFound(error: unknown): boolean {
    return (
        typeof error === "object" &&
        error !== null &&
        "code" in error &&
        error.code === 404
    )
}
