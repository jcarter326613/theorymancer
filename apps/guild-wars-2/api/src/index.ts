import { Storage } from "@google-cloud/storage"
import { GoogleAuth } from "google-auth-library"

import { createApp } from "./app.js"
import { TheorymancerAuthenticator } from "./auth.js"
import { ParentAuthFailureReporter } from "./auth-failure-reporter.js"

const bucketName = requiredEnvironmentVariable("GAME_ASSETS_BUCKET")
const authIssuer = requiredEnvironmentVariable("AUTH_ISSUER")
const authAudience = requiredEnvironmentVariable("AUTH_AUDIENCE")
const authJwksUrl = new URL(requiredEnvironmentVariable("AUTH_JWKS_URL"))
const parentAuthFailureUrl = new URL(
    requiredEnvironmentVariable("PARENT_AUTH_FAILURE_URL"),
)
const parentApiAudience = requiredEnvironmentVariable("PARENT_API_AUDIENCE")

const storage = new Storage()
const bucket = storage.bucket(bucketName)
const authenticator = new TheorymancerAuthenticator({
    issuer: authIssuer,
    audience: authAudience,
    jwksUrl: authJwksUrl,
})
const googleAuth = new GoogleAuth()
const parentClient = await googleAuth.getIdTokenClient(parentApiAudience)
const authFailureReporter = new ParentAuthFailureReporter(
    parentClient,
    parentAuthFailureUrl.href,
)
const app = createApp({
    objectStore: {
        async download(objectPath) {
            const [bytes] = await bucket.file(objectPath).download()
            return bytes
        },
    },
    authenticator,
    authFailureReporter,
})
const port = parsePort(process.env.API_PORT)

app.listen(port, () => {
    console.log(`Theorymancer Guild Wars 2 API listening on port ${port}`)
})

function requiredEnvironmentVariable(name: string): string {
    const value = process.env[name]
    if (value === undefined || value.length === 0) {
        throw new Error(`${name} is required.`)
    }
    return value
}

function parsePort(value: string | undefined): number {
    const port = Number(value ?? "3002")
    if (!Number.isInteger(port) || port < 1 || port > 65_535) {
        throw new Error("API_PORT must be a valid TCP port.")
    }
    return port
}
