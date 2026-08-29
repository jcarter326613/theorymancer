import { StrictMode, useEffect, useRef, useState } from "react"
import { createRoot } from "react-dom/client"
import { initializeApp } from "firebase/app"
import {
    getAuth,
    getRedirectResult,
    GoogleAuthProvider,
    onAuthStateChanged,
    signInWithPopup,
    signInWithRedirect,
    signOut,
    type Auth,
    type User,
} from "firebase/auth"

import "./styles.css"

const runtimeConfig = window.__THEORYMANCER_CONFIG__
const firebaseFields = [
    "apiUrl",
    "apiKey",
    "authDomain",
    "projectId",
    "appId",
    "tenantId",
] as const
const missingFirebaseFields = runtimeConfig
    ? firebaseFields.filter((field) => !runtimeConfig[field])
    : [...firebaseFields]

let auth: Auth | null = null
let redirectResultPromise: Promise<unknown> | null = null

if (runtimeConfig && missingFirebaseFields.length === 0) {
    const apiUrl = new URL(runtimeConfig.apiUrl)
    if (apiUrl.protocol !== "https:" && apiUrl.hostname !== "localhost") {
        throw new Error("The Theorymancer API URL must use HTTPS.")
    }
    const firebaseApp = initializeApp({
        apiKey: runtimeConfig.apiKey,
        authDomain: runtimeConfig.authDomain,
        projectId: runtimeConfig.projectId,
        appId: runtimeConfig.appId,
    })
    auth = getAuth(firebaseApp)
    auth.tenantId = runtimeConfig.tenantId || null
    redirectResultPromise = getRedirectResult(auth)
}

type AuthState = {
    user: User | null
    loading: boolean
    error: string | null
}

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
        loading: auth !== null,
        error:
            auth === null
                ? "Google sign-in is not configured for this environment."
                : null,
    })

    useEffect(() => {
        if (!auth) return

        let active = true
        redirectResultPromise?.catch((error: unknown) => {
            if (active) {
                setState((current) => ({
                    ...current,
                    error: readableError(
                        error,
                        "Google sign-in did not complete.",
                    ),
                }))
            }
        })

        const unsubscribe = onAuthStateChanged(
            auth,
            (user) => {
                if (active) {
                    setState((current) => ({
                        user,
                        loading: false,
                        error: user ? null : current.error,
                    }))
                }
            },
            (error) => {
                if (active) {
                    setState({
                        user: null,
                        loading: false,
                        error: readableError(
                            error,
                            "Unable to check sign-in status.",
                        ),
                    })
                }
            },
        )

        return () => {
            active = false
            unsubscribe()
        }
    }, [])

    return state
}

async function googleSignIn(): Promise<void> {
    if (!auth || !runtimeConfig)
        throw new Error("Google sign-in is unavailable.")

    auth.tenantId = runtimeConfig.tenantId || null
    const provider = new GoogleAuthProvider()
    provider.setCustomParameters({ prompt: "select_account" })

    try {
        await signInWithPopup(auth, provider)
    } catch (error) {
        const code = errorCode(error)
        if (
            code !== "auth/popup-blocked" &&
            code !== "auth/operation-not-supported-in-this-environment"
        ) {
            throw error
        }
        await signInWithRedirect(auth, provider)
    }
}

function apiEndpoint(path: string): string {
    const base = runtimeConfig?.apiUrl.trim().replace(/\/$/, "") ?? ""
    return `${base}${path}`
}

async function authenticatedRequest(
    user: User,
    path: string,
    init?: RequestInit,
) {
    const idToken = await user.getIdToken()
    return fetch(apiEndpoint(path), {
        ...init,
        headers: {
            ...init?.headers,
            Authorization: `Bearer ${idToken}`,
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
            authenticatedRequest(authState.user, "/v1/account"),
            authenticatedRequest(authState.user, "/v1/account/game-grants"),
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

    async function handleSignIn() {
        setActionPending(true)
        setError(null)
        try {
            await googleSignIn()
        } catch (signInError) {
            setError(
                readableError(signInError, "Google sign-in did not complete."),
            )
        } finally {
            setActionPending(false)
        }
    }

    async function handleSignOut() {
        if (!auth) return
        setActionPending(true)
        setError(null)
        try {
            await signOut(auth)
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
                authState.user,
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
        account?.displayName ?? user?.displayName ?? "Theorymancer player"
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
                            {user.photoURL ? (
                                <img
                                    src={user.photoURL}
                                    alt=""
                                    referrerPolicy="no-referrer"
                                />
                            ) : (
                                <span
                                    className="avatar-fallback"
                                    aria-hidden="true"
                                >
                                    {displayName.charAt(0).toUpperCase()}
                                </span>
                            )}
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
                            disabled={actionPending || auth === null}
                            onClick={handleSignIn}
                        >
                            <span className="google-mark" aria-hidden="true">
                                G
                            </span>
                            {actionPending
                                ? "Opening Google..."
                                : "Continue with Google"}
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
                            placeholder="Identity Platform UID"
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
            authState.user,
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
            if (!authState.user) await googleSignIn()
            setApproved(true)
        } catch (signInError) {
            setPending(false)
            setError(
                readableError(signInError, "Google sign-in did not complete."),
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
                    that opened this page. Your Google credentials never pass
                    through the desktop app.
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
                            disabled={auth === null}
                            onClick={handleAuthorize}
                        >
                            <span className="google-mark" aria-hidden="true">
                                G
                            </span>
                            {authState.user
                                ? "Authorize this desktop"
                                : "Continue with Google"}
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
