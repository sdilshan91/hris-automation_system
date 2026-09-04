# ===== Build stage =====
FROM node:22-alpine AS build
WORKDIR /app

# Angular build configuration. Defaults to `development` because this image is built
# by the LOCAL full-stack docker-compose.
#
# CORRECTED 2026-09-04 (queue item E4): this comment used to say the dev config makes "the browser
# call the API at http://localhost:5000". That stopped being true on 2026-06-30 (e257ab63), when
# environment.ts moved to a RELATIVE `apiBaseUrl: '/api/v1'`. The browser calls THIS server, which is
# why nginx.conf must carry an /api proxy — and why, for three months, the comment said the missing
# proxy was harmless when it was the reason the compose stack's login page could not log in.
# Override with `--build-arg BUILD_CONFIGURATION=production` for a real prod image (that config uses
# environment.prod.ts, whose apiBaseUrl IS absolute).
ARG BUILD_CONFIGURATION=development

# Install deps from lockfile first (cached unless package*.json changes).
COPY package.json package-lock.json ./
RUN npm ci

# Build the bundle (apiBaseUrl is baked in here from the matching environment.*.ts).
COPY . .
RUN npm run build -- --configuration ${BUILD_CONFIGURATION}

# ===== Serve stage =====
FROM nginx:1.27-alpine AS runtime
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/hris-frontend/browser /usr/share/nginx/html
EXPOSE 80
