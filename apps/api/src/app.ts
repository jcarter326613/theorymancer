import cors from "cors"
import express from "express"
import type { Express } from "express"

import { healthResponseSchema } from "@theorymancer/contracts"

export function createApp(): Express {
    const app = express()

    app.use(cors())
    app.get("/health", (_request, response) => {
        response.json(
            healthResponseSchema.parse({ status: "ok", service: "api" }),
        )
    })

    return app
}
