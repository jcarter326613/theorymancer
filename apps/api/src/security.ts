import { createHash, createHmac, timingSafeEqual } from "node:crypto"
import { isIP } from "node:net"

import argon2 from "argon2"

import type { Request } from "express"
import {
    calculateJwkThumbprint,
    decodeProtectedHeader,
    importJWK,
    jwtVerify,
} from "jose"
import type { JWK } from "jose"

export function hashSecret(value: string): string {
    return createHash("sha256").update(value).digest("base64url")
}

const argonOptions = {
    type: argon2.argon2id,
    memoryCost: 19 * 1024,
    timeCost: 2,
    parallelism: 1,
    hashLength: 32,
}

export function normalizeEmail(value: string): string | undefined {
    const email = value.trim().toLowerCase()
    return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email) && email.length <= 254
        ? email
        : undefined
}

export async function hashPassword(password: string): Promise<string> {
    return argon2.hash(password, argonOptions)
}

export async function verifyPassword(
    password: string,
    encoded: string,
): Promise<boolean> {
    try {
        return encoded.startsWith("$argon2id$") && await argon2.verify(encoded, password)
    } catch {
        return false
    }
}

export function hashIp(ip: string, secret: string): string {
    return createHmac("sha256", secret).update(ip).digest("base64url")
}

export function constantTimeEqual(left: string, right: string): boolean {
    const leftBytes = Buffer.from(left)
    const rightBytes = Buffer.from(right)
    return (
        leftBytes.length === rightBytes.length &&
        timingSafeEqual(leftBytes, rightBytes)
    )
}

export function extractBearer(
    authorization: string | undefined,
): string | undefined {
    const match = /^Bearer ([^\s]+)$/.exec(authorization ?? "")
    return match?.[1]
}

export function normalizeIp(input: string): string | undefined {
    let value = input.trim()
    if (value.startsWith("[") && value.endsWith("]")) {
        value = value.slice(1, -1)
    }
    const zoneIndex = value.indexOf("%")
    if (zoneIndex !== -1) value = value.slice(0, zoneIndex)

    if (isIP(value) === 4) return value.split(".").map(Number).join(".")
    if (isIP(value) !== 6) return undefined

    const mapped = /^::ffff:(\d+\.\d+\.\d+\.\d+)$/i.exec(value)
    if (mapped?.[1] !== undefined && isIP(mapped[1]) === 4) {
        return mapped[1].split(".").map(Number).join(".")
    }

    return normalizeIpv6(value)
}

export function clientIp(request: Request): string | undefined {
    const forwarded = request.headers["x-forwarded-for"]
    const value = Array.isArray(forwarded) ? forwarded.at(-1) : forwarded
    if (value !== undefined) {
        const rightmost = value.split(",").at(-1)
        return rightmost === undefined ? undefined : normalizeIp(rightmost)
    }
    return normalizeIp(request.socket.remoteAddress ?? "")
}

export async function validatePublicP256Jwk(jwk: JWK): Promise<string> {
    if (
        jwk.kty !== "EC" ||
        jwk.crv !== "P-256" ||
        typeof jwk.x !== "string" ||
        typeof jwk.y !== "string" ||
        "d" in jwk
    ) {
        throw new Error("Invalid installation JWK")
    }
    await importJWK(jwk, "ES256")
    return calculateJwkThumbprint(jwk, "sha256")
}

export async function verifyDpopProof(input: {
    proof: string | undefined
    expectedJwk: JWK
    expectedJkt: string
    expectedHtu: string
    now: number
    athValue?: string
}): Promise<{ jti: string; expiresAt: number }> {
    if (input.proof === undefined) throw new Error("Missing DPoP proof")
    const header = decodeProtectedHeader(input.proof)
    if (
        header.typ?.toLowerCase() !== "dpop+jwt" ||
        header.alg !== "ES256" ||
        header.jwk === undefined
    ) {
        throw new Error("Invalid DPoP header")
    }
    const headerJkt = await validatePublicP256Jwk(header.jwk)
    if (
        !constantTimeEqual(headerJkt, input.expectedJkt) ||
        !constantTimeEqual(
            await calculateJwkThumbprint(input.expectedJwk, "sha256"),
            input.expectedJkt,
        )
    ) {
        throw new Error("DPoP key mismatch")
    }
    const key = await importJWK(header.jwk, "ES256")
    const { payload } = await jwtVerify(input.proof, key, {
        algorithms: ["ES256"],
        typ: "dpop+jwt",
    })
    if (
        payload.htm !== "POST" ||
        payload.htu !== input.expectedHtu ||
        typeof payload.iat !== "number" ||
        Math.abs(Math.floor(input.now / 1000) - payload.iat) > 300 ||
        typeof payload.jti !== "string" ||
        payload.jti.length < 1 ||
        payload.jti.length > 200
    ) {
        throw new Error("Invalid DPoP claims")
    }
    const expectedAth =
        input.athValue === undefined ? undefined : hashSecret(input.athValue)
    if (
        (expectedAth === undefined && payload.ath !== undefined) ||
        (expectedAth !== undefined &&
            (typeof payload.ath !== "string" ||
                !constantTimeEqual(payload.ath, expectedAth)))
    ) {
        throw new Error("Invalid DPoP ath")
    }
    return {
        jti: payload.jti,
        expiresAt: Math.max((payload.iat + 300) * 1000, input.now + 1_000),
    }
}

function normalizeIpv6(value: string): string {
    let address = value.toLowerCase()
    const ipv4Match = /^(.*:)(\d+\.\d+\.\d+\.\d+)$/.exec(address)
    if (ipv4Match?.[1] !== undefined && ipv4Match[2] !== undefined) {
        const bytes = ipv4Match[2].split(".").map(Number)
        const high = (((bytes[0] ?? 0) << 8) | (bytes[1] ?? 0)).toString(16)
        const low = (((bytes[2] ?? 0) << 8) | (bytes[3] ?? 0)).toString(16)
        address = `${ipv4Match[1]}${high}:${low}`
    }
    const sides = address.split("::")
    const left = (sides[0] ?? "").split(":").filter(Boolean)
    const right = (sides[1] ?? "").split(":").filter(Boolean)
    const groups = [
        ...left,
        ...Array(Math.max(0, 8 - left.length - right.length)).fill("0"),
        ...right,
    ].map((group) => Number.parseInt(group, 16).toString(16))

    let bestStart = -1
    let bestLength = 0
    for (let start = 0; start < groups.length;) {
        if (groups[start] !== "0") {
            start += 1
            continue
        }
        let end = start
        while (groups[end] === "0") end += 1
        if (end - start > bestLength && end - start >= 2) {
            bestStart = start
            bestLength = end - start
        }
        start = end
    }
    if (bestStart === -1) return groups.join(":")
    const before = groups.slice(0, bestStart).join(":")
    const after = groups.slice(bestStart + bestLength).join(":")
    return `${before}::${after}`
}
