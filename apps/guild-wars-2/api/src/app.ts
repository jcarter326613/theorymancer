import cors from "cors"
import express from "express"
import type { Express } from "express"
import { z } from "zod"

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

export function createApp(objectStore: ObjectStore): Express {
    const app = express()

    app.use(cors())
    app.get("/health", (_request, response) => {
        response.json({ status: "ok", service: "guild-wars-2-api" })
    })

    app.get("/icons/manifest", async (_request, response) => {
        const manifest = await loadManifest(objectStore)
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

        const manifest = await loadManifest(objectStore)
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
            const iconBytes = await objectStore.download(asset.object_path)
            response
                .type("png")
                .set("Cache-Control", "public, max-age=31536000, immutable")
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
