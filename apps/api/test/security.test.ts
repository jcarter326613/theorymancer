import assert from "node:assert/strict"
import { test } from "node:test"

import type { Request } from "express"

import { clientIp, normalizeIp } from "../src/security.js"

void test("normalizes supported IPv4 and IPv6 client addresses", () => {
    assert.equal(normalizeIp(" 192.168.001.010 "), undefined)
    assert.equal(normalizeIp("::ffff:192.168.1.10"), "192.168.1.10")
    assert.equal(normalizeIp("2001:db8::192.0.2.1"), "2001:db8::c000:201")
    assert.equal(normalizeIp("[2001:0DB8:0:0:0:0:0:1]"), "2001:db8::1")
    assert.equal(normalizeIp("fe80::1%eth0"), "fe80::1")
    assert.equal(normalizeIp("not-an-ip"), undefined)
})

void test("uses the rightmost forwarded client IP", () => {
    const request = {
        headers: {
            "x-forwarded-for": "198.51.100.8, 2001:0db8:0:0:0:0:0:1",
        },
        socket: { remoteAddress: "127.0.0.1" },
    } as unknown as Request

    assert.equal(clientIp(request), "2001:db8::1")
})
