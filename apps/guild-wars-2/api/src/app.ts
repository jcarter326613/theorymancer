import cors from "cors"
import express from "express"
import type { Express } from "express"
import { z } from "zod"

const manifestObjectPath = "guild-wars-2/icons.manifest.json"
const sha256Pattern = /^[0-9a-f]{64}$/

const manifestIconSchema = z
    .object({
        skill_id: z.number().int().nonnegative(),
        name: z.string().min(1),
        source_url: z.string().url(),
        sha256: z.string().regex(sha256Pattern),
        object_path: z.string(),
    })
    .superRefine((icon, context) => {
        if (icon.object_path !== `guild-wars-2/icons/${icon.sha256}.png`) {
            context.addIssue({
                code: z.ZodIssueCode.custom,
                message:
                    "Icon object path must be content-addressed by its SHA-256.",
            })
        }
    })

const manifestSchema = z.object({
    version: z.literal(1),
    icons: z.array(manifestIconSchema),
})

type IconManifest = z.infer<typeof manifestSchema>

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

    app.get("/icons/:sha256.png", async (request, response) => {
        const { sha256 } = request.params
        if (!sha256Pattern.test(sha256)) {
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

        const icon = manifest.icons.find((entry) => entry.sha256 === sha256)
        if (icon === undefined) {
            response.sendStatus(404)
            return
        }

        try {
            const iconBytes = await objectStore.download(icon.object_path)
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
        return manifestSchema.parse(
            JSON.parse(
                (await objectStore.download(manifestObjectPath)).toString(
                    "utf8",
                ),
            ),
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
