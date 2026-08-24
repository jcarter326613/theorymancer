import { StrictMode } from "react"
import { createRoot } from "react-dom/client"

import "./styles.css"

function App() {
    return (
        <main>
            <p className="eyebrow">Theorymancer</p>
            <h1>Something is taking shape.</h1>
            <p className="summary">
                A new project is in the works. More when it is ready.
            </p>
            <p className="status">Coming eventually.</p>
        </main>
    )
}

createRoot(document.getElementById("root")!).render(
    <StrictMode>
        <App />
    </StrictMode>,
)
