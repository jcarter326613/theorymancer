import type { IdTokenClient } from "google-auth-library"
import { z } from "zod"

const reportResponseSchema = z.discriminatedUnion("blocked", [
    z.object({
        blocked: z.literal(true),
        retry_after_seconds: z.number().int().nonnegative(),
    }),
    z.object({
        blocked: z.literal(false),
        retry_after_seconds: z.number().int().nonnegative().optional(),
    }),
])

export interface AuthFailureReport {
    blocked: boolean
    retryAfterSeconds?: number
}

export interface AuthFailureReporter {
    report(ip: string): Promise<AuthFailureReport>
}

export class ParentAuthFailureReporter implements AuthFailureReporter {
    public constructor(
        private readonly client: IdTokenClient,
        private readonly url: string,
    ) {}

    public async report(ip: string): Promise<AuthFailureReport> {
        const response = await this.client.request<unknown>({
            url: this.url,
            method: "POST",
            data: { ip },
            timeout: 3_000,
        })
        const result = reportResponseSchema.parse(response.data)
        return {
            blocked: result.blocked,
            retryAfterSeconds: result.retry_after_seconds,
        }
    }
}
