import { StrictMode, useEffect, useRef, useState } from "react"
import { createRoot } from "react-dom/client"

import "./styles.css"

const runtimeConfig = window.__THEORYMANCER_CONFIG__
if (runtimeConfig?.apiUrl) {
    const apiUrl = new URL(runtimeConfig.apiUrl)
    if (apiUrl.protocol !== "https:" && apiUrl.hostname !== "localhost") {
        throw new Error("The Theorymancer API URL must use HTTPS.")
    }
}

type User = { uid: string; email: string }
type AuthState = {
    user: User | null
    loading: boolean
    error: string | null
}
let csrfToken: string | undefined

type DesktopRequest = {
    code_challenge: string
    redirect_uri: string
    state: string
    installation_jwk: {
        kty: string
        crv: string
        x: string
        y: string
        [key: string]: unknown
    }
}

type AccountDetails = {
    uid?: string
    displayName?: string
    email?: string
    platformRole?: string
}

type GameGrant = {
    game?: string
    gameId?: string
    name?: string
    displayName?: string
    active?: boolean
}

function readableError(error: unknown, fallback: string): string {
    if (typeof error === "object" && error !== null && "message" in error) {
        const message = (error as { message?: unknown }).message
        if (typeof message === "string" && message.trim()) return message
    }
    return fallback
}

function errorCode(error: unknown): string | undefined {
    if (typeof error !== "object" || error === null || !("code" in error)) {
        return undefined
    }
    const code = (error as { code?: unknown }).code
    return typeof code === "string" ? code : undefined
}

function useAuthentication(): AuthState {
    const [state, setState] = useState<AuthState>({
        user: null,
        loading: true,
        error: runtimeConfig?.apiUrl ? null : "The API is not configured for this environment.",
    })

    useEffect(() => {
        let active = true
        fetch(apiEndpoint("/v1/auth/session"), { credentials: "include" })
            .then(async (response) => {
                if (response.status === 401) return null
                if (!response.ok) throw new Error(await responseMessage(response, "Unable to check sign-in status"))
                return response.json() as Promise<{ account: AccountDetails; csrf_token: string }>
            })
            .then((session) => {
                if (!active) return
                csrfToken = session?.csrf_token
                const account = session?.account
                setState({ user: account?.uid && account.email ? { uid: account.uid, email: account.email } : null, loading: false, error: null })
            })
            .catch((error: unknown) => active && setState({ user: null, loading: false, error: readableError(error, "Unable to check sign-in status.") }))

        return () => {
            active = false
        }
    }, [])

    return state
}

function apiEndpoint(path: string): string {
    const base = runtimeConfig?.apiUrl.trim().replace(/\/$/, "") ?? ""
    return `${base}${path}`
}

async function authenticatedRequest(
    path: string,
    init?: RequestInit,
) {
    return fetch(apiEndpoint(path), {
        ...init,
        credentials: "include",
        headers: {
            ...init?.headers,
            ...(init?.method && !["GET", "HEAD"].includes(init.method) && csrfToken ? { "X-CSRF-Token": csrfToken } : {}),
            ...(init?.body ? { "Content-Type": "application/json" } : {}),
        },
    })
}

async function responseMessage(
    response: Response,
    fallback: string,
): Promise<string> {
    try {
        const body = (await response.json()) as {
            message?: unknown
            error?: unknown
        }
        if (typeof body.message === "string") return body.message
        if (typeof body.error === "string") return body.error
    } catch {
        // The status-based fallback is safe when the API does not return JSON.
    }
    return `${fallback} (${response.status})`
}

function accountFrom(payload: unknown): AccountDetails {
    if (typeof payload !== "object" || payload === null) return {}
    const value = payload as Record<string, unknown>
    const candidate =
        typeof value.account === "object" && value.account !== null
            ? (value.account as Record<string, unknown>)
            : value
    return {
        uid: typeof candidate.uid === "string" ? candidate.uid : undefined,
        displayName:
            typeof candidate.displayName === "string"
                ? candidate.displayName
                : undefined,
        email:
            typeof candidate.email === "string" ? candidate.email : undefined,
        platformRole:
            typeof candidate.platform_role === "string"
                ? candidate.platform_role
                : undefined,
    }
}

function grantsFrom(payload: unknown): GameGrant[] {
    if (Array.isArray(payload)) return payload as GameGrant[]
    if (typeof payload !== "object" || payload === null) return []
    const value = payload as Record<string, unknown>
    const grants = value.gameGrants ?? value.grants ?? value.games
    return Array.isArray(grants) ? (grants as GameGrant[]) : []
}

function grantName(grant: GameGrant): string {
    const name =
        grant.displayName ??
        grant.name ??
        grant.game ??
        grant.gameId ??
        "Unknown game"
    return name === "guild-wars-2" ? "Guild Wars 2" : name
}

function Brand() {
    return <p className="eyebrow">Theorymancer</p>
}

function AccountPage({ authState }: { authState: AuthState }) {
    const [account, setAccount] = useState<AccountDetails | null>(null)
    const [grants, setGrants] = useState<GameGrant[]>([])
    const [accountLoading, setAccountLoading] = useState(false)
    const [actionPending, setActionPending] = useState(false)
    const [grantUid, setGrantUid] = useState("")
    const [grantStatus, setGrantStatus] = useState<string | null>(null)
    const [error, setError] = useState<string | null>(null)

    useEffect(() => {
        if (!authState.user) {
            setAccount(null)
            setGrants([])
            return
        }

        let active = true
        setAccountLoading(true)
        setError(null)

        Promise.all([
            authenticatedRequest("/v1/account"),
            authenticatedRequest("/v1/account/game-grants"),
        ])
            .then(async ([accountResponse, grantsResponse]) => {
                if (!accountResponse.ok && accountResponse.status !== 404) {
                    throw new Error(
                        await responseMessage(
                            accountResponse,
                            "Unable to load your account",
                        ),
                    )
                }
                if (!grantsResponse.ok) {
                    throw new Error(
                        await responseMessage(
                            grantsResponse,
                            "Unable to load game access",
                        ),
                    )
                }
                const [accountPayload, grantsPayload] = await Promise.all([
                    accountResponse.status === 404
                        ? Promise.resolve({})
                        : (accountResponse.json() as Promise<unknown>),
                    grantsResponse.json() as Promise<unknown>,
                ])
                if (active) {
                    setAccount(accountFrom(accountPayload))
                    setGrants(
                        grantsFrom(grantsPayload).filter(
                            (grant) => grant.active !== false,
                        ),
                    )
                }
            })
            .catch((requestError: unknown) => {
                if (active) {
                    setError(
                        readableError(
                            requestError,
                            "Unable to load your account.",
                        ),
                    )
                }
            })
            .finally(() => {
                if (active) setAccountLoading(false)
            })

        return () => {
            active = false
        }
    }, [authState.user])

    async function handleSignIn(register: boolean) {
        const email = window.prompt("Email address")
        const password = window.prompt("Password (at least 12 characters)")
        if (!email || !password) return
        setActionPending(true)
        setError(null)
        try {
            const response = await fetch(apiEndpoint(register ? "/v1/auth/register" : "/v1/auth/login"), {
                method: "POST", credentials: "include", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ email, password }),
            })
            if (!response.ok) throw new Error(await responseMessage(response, "Sign-in did not complete"))
            const body = await response.json() as { csrf_token: string }
            csrfToken = body.csrf_token
            window.location.reload()
        } catch (signInError) {
            setError(
                readableError(signInError, "Sign-in did not complete."),
            )
        } finally {
            setActionPending(false)
        }
    }

    async function handleSignOut() {
        setActionPending(true)
        setError(null)
        try {
            const response = await authenticatedRequest("/v1/auth/logout", { method: "POST" })
            if (!response.ok) throw new Error(await responseMessage(response, "Sign-out did not complete"))
            csrfToken = undefined
            window.location.reload()
        } catch (signOutError) {
            setError(readableError(signOutError, "Sign-out did not complete."))
        } finally {
            setActionPending(false)
        }
    }

    async function updateGuildWars2Grant(method: "PUT" | "DELETE") {
        if (!authState.user || !grantUid.trim()) return
        setActionPending(true)
        setError(null)
        setGrantStatus(null)
        try {
            const response = await authenticatedRequest(
                `/v1/admin/accounts/${encodeURIComponent(grantUid.trim())}/game-grants/guild-wars-2`,
                { method },
            )
            if (!response.ok) {
                throw new Error(
                    await responseMessage(
                        response,
                        "Unable to update the game grant",
                    ),
                )
            }
            setGrantStatus(
                method === "PUT"
                    ? "Guild Wars 2 access granted."
                    : "Guild Wars 2 access revoked.",
            )
        } catch (grantError) {
            setError(
                readableError(grantError, "Unable to update the game grant."),
            )
        } finally {
            setActionPending(false)
        }
    }

    const user = authState.user
    const displayName =
        account?.displayName ?? "Theorymancer player"
    const email = account?.email ?? user?.email

    return (
        <main className="account-page">
            <section className="account-intro" aria-labelledby="account-title">
                <Brand />
                <h1 id="account-title">Practice what matters.</h1>
                <p className="summary">
                    Theorymancer turns performance data into the next concrete
                    thing worth improving.
                </p>
                <p className="edition">Account access / early edition</p>
            </section>

            <section className="account-panel" aria-label="Central account">
                <div className="panel-rule" aria-hidden="true" />
                <p className="section-label">Central account</p>
                {authState.loading ? (
                    <p className="notice" role="status">
                        Checking sign-in status...
                    </p>
                ) : user ? (
                    <>
                        <div className="identity">
                            <span className="avatar-fallback" aria-hidden="true">
                                {displayName.charAt(0).toUpperCase()}
                            </span>
                            <div>
                                <h2 id="account-heading">{displayName}</h2>
                                {email && <p>{email}</p>}
                                {account?.uid && (
                                    <p className="account-id">
                                        Account ID: {account.uid}
                                    </p>
                                )}
                            </div>
                        </div>
                        <button
                            className="text-button"
                            type="button"
                            disabled={actionPending}
                            onClick={handleSignOut}
                        >
                            {actionPending ? "Signing out..." : "Sign out"}
                        </button>
                    </>
                ) : (
                    <>
                        <h2 id="account-heading">One account, every tool.</h2>
                        <p className="panel-copy">
                            Sign in to see the games and desktop tools granted
                            to your account.
                        </p>
                        <button
                            className="primary-button"
                            type="button"
                            disabled={actionPending || !runtimeConfig?.apiUrl}
                            onClick={() => handleSignIn(false)}
                        >
                            {actionPending
                                ? "Signing in..."
                                : "Sign in"}
                        </button>
                        <button
                            className="text-button"
                            type="button"
                            disabled={actionPending || !runtimeConfig?.apiUrl}
                            onClick={() => handleSignIn(true)}
                        >
                            Create account
                        </button>
                    </>
                )}

                {(error ?? authState.error) && (
                    <p className="error-message" role="alert">
                        {error ?? authState.error}
                    </p>
                )}

                {user && (
                    <div className="grants" aria-busy={accountLoading}>
                        <div className="grants-heading">
                            <p className="section-label">Granted games</p>
                            <span>
                                {accountLoading ? "Loading" : grants.length}
                            </span>
                        </div>
                        {accountLoading ? (
                            <p className="notice" role="status">
                                Loading central grants...
                            </p>
                        ) : grants.length ? (
                            <ul>
                                {grants.map((grant, index) => (
                                    <li key={`${grantName(grant)}-${index}`}>
                                        <span>
                                            {String(index + 1).padStart(2, "0")}
                                        </span>
                                        {grantName(grant)}
                                    </li>
                                ))}
                            </ul>
                        ) : (
                            <p className="notice">
                                No games have been granted yet.
                            </p>
                        )}
                    </div>
                )}

                {user && account?.platformRole === "admin" && (
                    <div className="grant-admin">
                        <p className="section-label">Grant management</p>
                        <label htmlFor="grant-user-id">
                            Theorymancer user ID
                        </label>
                        <input
                            id="grant-user-id"
                            value={grantUid}
                            onChange={(event) =>
                                setGrantUid(event.target.value)
                            }
                            placeholder="Theorymancer account ID"
                        />
                        <div className="grant-actions">
                            <button
                                type="button"
                                disabled={actionPending || !grantUid.trim()}
                                onClick={() => updateGuildWars2Grant("PUT")}
                            >
                                Grant Guild Wars 2
                            </button>
                            <button
                                type="button"
                                disabled={actionPending || !grantUid.trim()}
                                onClick={() => updateGuildWars2Grant("DELETE")}
                            >
                                Revoke
                            </button>
                        </div>
                        {grantStatus && <p className="notice">{grantStatus}</p>}
                    </div>
                )}
            </section>
        </main>
    )
}

function readDesktopRequest(): { request?: DesktopRequest; error?: string } {
    const params = new URLSearchParams(window.location.search)
    const codeChallenge = params.get("code_challenge") ?? ""
    const redirectUri = params.get("redirect_uri") ?? ""
    const state = params.get("state") ?? ""
    const installationJwkValue = params.get("installation_jwk") ?? ""
    const codeChallengeMethod = params.get("code_challenge_method")

    if (!codeChallenge || !redirectUri || !state || !installationJwkValue) {
        return {
            error: "This authorization link is incomplete. Return to the desktop app and try again.",
        }
    }
    if (!/^[A-Za-z0-9_-]{43}$/.test(codeChallenge)) {
        return {
            error: "This authorization link has an invalid S256 code challenge.",
        }
    }
    if (codeChallengeMethod !== null && codeChallengeMethod !== "S256") {
        return {
            error: "This authorization link does not use the required S256 challenge method.",
        }
    }
    if (state.length > 1024) {
        return {
            error: "This authorization link contains an invalid state value.",
        }
    }

    const callbackMatch = /^http:\/\/127\.0\.0\.1:(\d{1,5})\/callback$/.exec(
        redirectUri,
    )
    const port = callbackMatch ? Number(callbackMatch[1]) : 0
    if (!callbackMatch || port < 1 || port > 65535) {
        return {
            error: "The desktop callback address is not a valid loopback callback.",
        }
    }

    let installationJwk: unknown
    try {
        installationJwk = JSON.parse(installationJwkValue)
    } catch {
        return {
            error: "This authorization link contains an invalid installation key.",
        }
    }
    if (
        typeof installationJwk !== "object" ||
        installationJwk === null ||
        !("kty" in installationJwk) ||
        installationJwk.kty !== "EC" ||
        !("crv" in installationJwk) ||
        installationJwk.crv !== "P-256" ||
        !("x" in installationJwk) ||
        typeof installationJwk.x !== "string" ||
        !/^[A-Za-z0-9_-]{43}$/.test(installationJwk.x) ||
        !("y" in installationJwk) ||
        typeof installationJwk.y !== "string" ||
        !/^[A-Za-z0-9_-]{43}$/.test(installationJwk.y) ||
        "d" in installationJwk
    ) {
        return {
            error: "This authorization link contains an invalid installation key.",
        }
    }

    return {
        request: {
            code_challenge: codeChallenge,
            redirect_uri: redirectUri,
            state,
            installation_jwk:
                installationJwk as DesktopRequest["installation_jwk"],
        },
    }
}

function DesktopAuthorizePage({ authState }: { authState: AuthState }) {
    const [{ request, error: requestError }] = useState(readDesktopRequest)
    const [pending, setPending] = useState(false)
    const [error, setError] = useState<string | null>(null)
    const [authorizationAttempt, setAuthorizationAttempt] = useState(0)
    const [approved, setApproved] = useState(false)
    const authorizationStarted = useRef(false)

    useEffect(() => {
        if (
            !request ||
            !authState.user ||
            !approved ||
            authorizationStarted.current
        )
            return
        authorizationStarted.current = true
        setPending(true)
        setError(null)

        authenticatedRequest(
            "/v1/auth/desktop/authorizations",
            {
                method: "POST",
                body: JSON.stringify(request),
            },
        )
            .then(async (response) => {
                if (!response.ok) {
                    throw new Error(
                        await responseMessage(
                            response,
                            "Desktop authorization failed",
                        ),
                    )
                }
                const body = (await response.json()) as {
                    code?: unknown
                    state?: unknown
                }
                if (
                    typeof body.code !== "string" ||
                    typeof body.state !== "string"
                ) {
                    throw new Error(
                        "The authorization server returned an invalid response.",
                    )
                }
                const callback = new URL(request.redirect_uri)
                callback.searchParams.set("code", body.code)
                callback.searchParams.set("state", body.state)
                window.location.assign(callback.toString())
            })
            .catch((authorizationError: unknown) => {
                authorizationStarted.current = false
                setPending(false)
                setError(
                    readableError(
                        authorizationError,
                        "Desktop authorization did not complete.",
                    ),
                )
            })
    }, [approved, authState.user, request, authorizationAttempt])

    async function handleAuthorize() {
        setPending(true)
        setError(null)
        try {
            if (!authState.user) {
                const email = window.prompt("Email address")
                const password = window.prompt("Password")
                if (!email || !password) throw new Error("Email and password are required.")
                const response = await fetch(apiEndpoint("/v1/auth/login"), {
                    method: "POST", credentials: "include", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ email, password }),
                })
                if (!response.ok) throw new Error(await responseMessage(response, "Sign-in did not complete"))
                const body = await response.json() as { csrf_token: string }
                csrfToken = body.csrf_token
                window.location.reload()
                return
            }
            setApproved(true)
        } catch (signInError) {
            setPending(false)
            setError(
                readableError(signInError, "Sign-in did not complete."),
            )
        }
    }

    function retryAuthorization() {
        setError(null)
        setPending(true)
        setApproved(true)
        setAuthorizationAttempt((attempt) => attempt + 1)
    }

    const visibleError = requestError ?? error ?? authState.error

    return (
        <main className="authorize-page">
            <section
                className="authorize-card"
                aria-labelledby="authorize-title"
            >
                <Brand />
                <p className="section-label">Desktop connection</p>
                <h1 id="authorize-title">Authorize Theorymancer Desktop</h1>
                <p className="summary">
                    Connect this browser account to the desktop installation
                    that opened this page. Your password never passes through
                    the desktop app.
                </p>

                <div className="security-note">
                    <span aria-hidden="true">127.0.0.1</span>
                    <p>
                        Approval returns a short-lived code only to the local
                        app on this device.
                    </p>
                </div>

                {visibleError && (
                    <p className="error-message" role="alert">
                        {visibleError}
                    </p>
                )}

                {!requestError &&
                    (authState.loading || pending ? (
                        <p className="notice progress" role="status">
                            {authState.loading
                                ? "Checking sign-in status..."
                                : "Completing secure authorization..."}
                        </p>
                    ) : authState.user && error ? (
                        <button
                            className="primary-button"
                            type="button"
                            onClick={retryAuthorization}
                        >
                            Try authorization again
                        </button>
                    ) : (
                        <button
                            className="primary-button"
                            type="button"
                            disabled={!runtimeConfig?.apiUrl}
                            onClick={handleAuthorize}
                        >
                            {authState.user
                                ? "Authorize this desktop"
                                : "Sign in to authorize"}
                        </button>
                    ))}
                <p className="fine-print">
                    Only continue if you initiated this request from
                    Theorymancer Desktop.
                </p>
            </section>
        </main>
    )
}

function App() {
    const authState = useAuthentication()
    return window.location.pathname === "/desktop/authorize" ? (
        <DesktopAuthorizePage authState={authState} />
    ) : (
        <AccountPage authState={authState} />
    )
}

createRoot(document.getElementById("root")!).render(
    <StrictMode>
        <App />
    </StrictMode>,
)
