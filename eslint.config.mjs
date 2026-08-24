import eslintConfigPrettier from "eslint-config-prettier"
import typescriptEslintPlugin from "@typescript-eslint/eslint-plugin"
import typescriptEslintParser from "@typescript-eslint/parser"

export default [
    {
        ignores: ["**/dist/**", "**/node_modules/**"],
    },
    {
        files: ["**/*.{ts,tsx}"],
        languageOptions: {
            parser: typescriptEslintParser,
            parserOptions: {
                sourceType: "module",
                projectService: true,
                tsconfigRootDir: import.meta.dirname,
            },
        },
        plugins: {
            "@typescript-eslint": typescriptEslintPlugin,
        },
        rules: {
            ...typescriptEslintPlugin.configs.recommended.rules,
            ...eslintConfigPrettier.rules,
            "@typescript-eslint/no-unused-vars": [
                "warn",
                { argsIgnorePattern: "^_", varsIgnorePattern: "^_" },
            ],
            "@typescript-eslint/no-floating-promises": "error",
        },
    },
]
