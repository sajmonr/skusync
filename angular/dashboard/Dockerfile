# syntax=docker/dockerfile:1

FROM node:22-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY . ./

# Angular build configuration. Defaults to production so real production builds (Dokploy) are
# unchanged. The compose stack passes "development" so the dashboard runs in the dev environment,
# matching the .NET services (source maps, no optimisation, environment.ts values).
ARG BUILD_CONFIGURATION=production

# Optional override of the API base URL baked into the build. Empty by default so production
# builds keep the committed environment untouched. The compose stack passes API_BASE_URL=/api to
# route the browser through this container's nginx reverse proxy to the web-api service. The value
# is written into whichever environment file the chosen configuration actually consumes: the
# production configuration file-replaces environment.ts with environment.production.ts (which
# imports environment.production.json); every other configuration uses environment.ts as-is.
ARG API_BASE_URL=
RUN if [ -n "$API_BASE_URL" ]; then \
      if [ "$BUILD_CONFIGURATION" = "production" ]; then \
        printf '{\n  "apiBaseUrl": "%s",\n  "environmentName": "Docker"\n}\n' "$API_BASE_URL" \
          > src/environments/environment.production.json; \
      else \
        printf "const environment = {\n  apiBaseUrl: '%s',\n  environmentName: 'Development',\n} as const;\n\nexport default environment;\n" "$API_BASE_URL" \
          > src/environments/environment.ts; \
      fi; \
    fi

RUN npm run build -- --configuration "$BUILD_CONFIGURATION"

FROM nginx:1.27-alpine AS final
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/dashboard/browser /usr/share/nginx/html

EXPOSE 80

HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD wget -q -O - http://127.0.0.1/health >/dev/null 2>&1 || exit 1

