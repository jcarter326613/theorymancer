interface TheorymancerRuntimeConfig {
    apiUrl: string
    apiKey: string
    authDomain: string
    projectId: string
    appId: string
    tenantId: string
}

interface Window {
    __THEORYMANCER_CONFIG__?: TheorymancerRuntimeConfig
}
