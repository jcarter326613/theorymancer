FROM node:22-alpine AS build

WORKDIR /app

COPY package.json pnpm-lock.yaml pnpm-workspace.yaml tsconfig.base.json ./
COPY apps/web/package.json apps/web/tsconfig.json apps/web/tsconfig.app.json apps/web/tsconfig.node.json ./apps/web/
RUN corepack enable && pnpm install --frozen-lockfile

COPY apps/web ./apps/web
ARG VITE_API_URL
RUN pnpm --filter @theorymancer/web build

FROM nginx:1.27-alpine

COPY apps/web/nginx.conf /etc/nginx/conf.d/default.conf
COPY apps/web/config.template.js /etc/theorymancer/config.template.js
COPY apps/web/docker-entrypoint.d/40-theorymancer-config.sh /docker-entrypoint.d/40-theorymancer-config.sh
COPY --from=build /app/apps/web/dist /usr/share/nginx/html
RUN sed -i 's/\r$//' /docker-entrypoint.d/40-theorymancer-config.sh && \
    chmod +x /docker-entrypoint.d/40-theorymancer-config.sh

EXPOSE 8080
