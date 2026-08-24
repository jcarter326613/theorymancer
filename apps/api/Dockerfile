FROM node:22-alpine AS build

WORKDIR /app

COPY package.json pnpm-lock.yaml pnpm-workspace.yaml tsconfig.base.json ./
COPY packages/contracts/package.json packages/contracts/tsconfig.json ./packages/contracts/
COPY apps/api/package.json apps/api/tsconfig.json ./apps/api/
RUN corepack enable && pnpm install --frozen-lockfile

COPY packages/contracts/src ./packages/contracts/src
COPY apps/api/src ./apps/api/src
RUN pnpm --filter @theorymancer/api... build

FROM node:22-alpine

WORKDIR /app
ENV NODE_ENV=production

COPY --from=build /app/node_modules ./node_modules
COPY --from=build /app/packages/contracts ./packages/contracts
COPY --from=build /app/apps/api ./apps/api

USER node
EXPOSE 8080
ENV API_PORT=8080
CMD ["node", "apps/api/dist/index.js"]
