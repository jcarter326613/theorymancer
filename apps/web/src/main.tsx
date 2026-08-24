import { StrictMode } from "react"
import { createRoot } from "react-dom/client"

import "./styles.css"

function App() {
    return (
        <main>
            <p className="eyebrow">Theorymancer</p>
            <h1>Practice what changes the outcome.</h1>
            <p className="summary">
                Performance coaching for players who want an answer better than
                a stat sheet. Guild Wars 2 combat analysis is the first chapter.
            </p>
            <p className="status">
                The first analysis tools are in development.
            </p>
        </main>
    )
}

createRoot(document.getElementById("root")!).render(
    <StrictMode>
        <App />
    </StrictMode>,
)
