interface TheorymancerRuntimeConfig {
    apiUrl: string
}

interface Window {
    __THEORYMANCER_CONFIG__?: TheorymancerRuntimeConfig
}
