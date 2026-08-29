import { afterEach, test } from "node:test"
import assert from "node:assert/strict"
import { readFile } from "node:fs/promises"
import type { AddressInfo } from "node:net"
import type { Server } from "node:http"

import { createApp, parseManifest } from "./app.js"
import type { ObjectStore } from "./app.js"

const iconAssetId = "a784986f-696d-4c63-8f46-4cc53efc9b47"
const manifestPath = "guild-wars-2/icons.manifest.json"
const iconPath = `guild-wars-2/icons/${iconAssetId}.png`
const manifest = JSON.stringify({
    version: 2,
    assets: [
        {
            asset_id: iconAssetId,
            source_url: "https://render.guildwars2.com/nightfall.png",
            object_path: iconPath,
        },
    ],
    skills: [
        {
            skill_id: 29855,
            name: "Nightfall",
            type: "Weapon",
            professions: ["Necromancer"],
            weapon_type: "Greatsword",
            slot: "Weapon_4",
            specialization_ids: [34],
            categories: [],
            attunement: null,
            icon_asset_id: iconAssetId,
        },
    ],
    effects: [
        {
            name: "Blinded",
            fact_type: "Buff",
            description: "Next outgoing attack misses; stacks duration.",
            icon_asset_id: iconAssetId,
        },
    ],
})

const servers: Server[] = []

afterEach(async () => {
    for (const server of servers.splice(0)) {
        await server.close()
    }
})

void test("returns the validated manifest", async () => {
    const response = await request(
        new MapObjectStore(new Map([[manifestPath, manifest]])),
        "/icons/manifest",
    )

    assert.equal(response.status, 200)
    assert.deepEqual(await response.json(), JSON.parse(manifest))
})

void test("validates the source icon manifest", async () => {
    const sourceManifest = await readFile(
        new URL("../../assets/icons.manifest.json", import.meta.url),
        "utf8",
    )
    assert.doesNotThrow(() => parseManifest(sourceManifest))
    const response = await request(
        new MapObjectStore(new Map([[manifestPath, sourceManifest]])),
        "/icons/manifest",
    )

    assert.equal(response.status, 200)
})

void test("returns only manifest-listed icon objects", async () => {
    const response = await request(
        new MapObjectStore(
            new Map([
                [manifestPath, manifest],
                [iconPath, "png-bytes"],
            ]),
        ),
        `/icons/${iconAssetId}.png`,
    )

    assert.equal(response.status, 200)
    assert.equal(response.headers.get("content-type"), "image/png")
    assert.equal(
        response.headers.get("cache-control"),
        "public, max-age=31536000, immutable",
    )
    assert.equal(await response.text(), "png-bytes")
})

void test("rejects unknown and malformed icon asset IDs", async () => {
    const objectStore = new MapObjectStore(new Map([[manifestPath, manifest]]))

    assert.equal(
        (
            await request(
                objectStore,
                "/icons/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa.png",
            )
        ).status,
        404,
    )
    assert.equal(
        (await request(objectStore, "/icons/not-a-hash.png")).status,
        404,
    )
})

void test("returns not found when a manifest-listed object is absent", async () => {
    const response = await request(
        new MapObjectStore(new Map([[manifestPath, manifest]])),
        `/icons/${iconAssetId}.png`,
    )

    assert.equal(response.status, 404)
})

void test("does not serve an invalid manifest", async () => {
    const invalidManifest = JSON.stringify({
        version: 2,
        assets: [
            {
                ...JSON.parse(manifest).assets[0],
                object_path: "outside-the-game-namespace.png",
            },
        ],
        skills: JSON.parse(manifest).skills,
        effects: JSON.parse(manifest).effects,
    })
    const response = await request(
        new MapObjectStore(new Map([[manifestPath, invalidManifest]])),
        "/icons/manifest",
    )

    assert.equal(response.status, 500)
})

class MapObjectStore implements ObjectStore {
    public constructor(private readonly objects: Map<string, string>) {}

    public async download(objectPath: string): Promise<Buffer> {
        const object = this.objects.get(objectPath)
        if (object === undefined) {
            throw { code: 404 }
        }

        return Buffer.from(object)
    }
}

async function request(
    objectStore: ObjectStore,
    path: string,
): Promise<Response> {
    const server = createApp(objectStore).listen(0)
    servers.push(server)
    await new Promise<void>((resolve) => server.once("listening", resolve))
    const { port } = server.address() as AddressInfo
    return fetch(`http://127.0.0.1:${port}${path}`)
}
