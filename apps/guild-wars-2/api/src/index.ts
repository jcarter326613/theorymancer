import { Storage } from "@google-cloud/storage"

import { createApp } from "./app.js"

const bucketName = process.env.GAME_ASSETS_BUCKET
if (bucketName === undefined || bucketName.length === 0) {
    throw new Error("GAME_ASSETS_BUCKET is required.")
}

const storage = new Storage()
const bucket = storage.bucket(bucketName)
const app = createApp({
    async download(objectPath) {
        const [bytes] = await bucket.file(objectPath).download()
        return bytes
    },
})
const port = Number.parseInt(process.env.API_PORT ?? "3002", 10)

app.listen(port, () => {
    console.log(`Theorymancer Guild Wars 2 API listening on port ${port}`)
})
