import { afterEach, test } from "node:test"
import assert from "node:assert/strict"
import type { AddressInfo } from "node:net"
import type { Server } from "node:http"

import { createApp } from "./app.js"
import type { ObjectStore } from "./app.js"

const iconHash =
    "21d0d01d269b0f0a1e708a301e8e08e70b717f05c1721dd29b9a31415acac581"
const manifestPath = "guild-wars-2/icons.manifest.json"
const iconPath = `guild-wars-2/icons/${iconHash}.png`
const manifest = JSON.stringify({
    version: 1,
    icons: [
        {
            skill_id: 29855,
            name: "Nightfall",
            source_url: "https://render.guildwars2.com/nightfall.png",
            sha256: iconHash,
            object_path: iconPath,
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

void test("returns only manifest-listed icon objects", async () => {
    const response = await request(
        new MapObjectStore(
            new Map([
                [manifestPath, manifest],
                [iconPath, "png-bytes"],
            ]),
        ),
        `/icons/${iconHash}.png`,
    )

    assert.equal(response.status, 200)
    assert.equal(response.headers.get("content-type"), "image/png")
    assert.equal(
        response.headers.get("cache-control"),
        "public, max-age=31536000, immutable",
    )
    assert.equal(await response.text(), "png-bytes")
})

void test("rejects unknown and malformed icon hashes", async () => {
    const objectStore = new MapObjectStore(new Map([[manifestPath, manifest]]))

    assert.equal(
        (await request(objectStore, `/icons/${"a".repeat(64)}.png`)).status,
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
        `/icons/${iconHash}.png`,
    )

    assert.equal(response.status, 404)
})

void test("does not serve an invalid manifest", async () => {
    const invalidManifest = JSON.stringify({
        version: 1,
        icons: [
            {
                ...JSON.parse(manifest).icons[0],
                object_path: "outside-the-game-namespace.png",
            },
        ],
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
