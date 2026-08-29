FROM node:22-alpine AS build

WORKDIR /app

COPY package.json pnpm-lock.yaml pnpm-workspace.yaml tsconfig.base.json ./
COPY apps/guild-wars-2/api/package.json apps/guild-wars-2/api/tsconfig.json ./apps/guild-wars-2/api/
RUN corepack enable && pnpm install --frozen-lockfile

COPY apps/guild-wars-2/api/src ./apps/guild-wars-2/api/src
RUN pnpm --filter @theorymancer/guild-wars-2-api build

FROM node:22-alpine

WORKDIR /app
ENV NODE_ENV=production

COPY --from=build /app/node_modules ./node_modules
COPY --from=build /app/apps/guild-wars-2/api ./apps/guild-wars-2/api

USER node
EXPOSE 8080
ENV API_PORT=8080
CMD ["node", "apps/guild-wars-2/api/dist/index.js"]
