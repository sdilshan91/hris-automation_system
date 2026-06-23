# ===== Build stage =====
FROM node:22-alpine AS build
WORKDIR /app

# Angular build configuration. Defaults to `development` because this image is built
# by the LOCAL full-stack docker-compose — the dev config uses environment.ts, so the
# browser calls the API at http://localhost:5000 instead of the prod api.yourhrm.com.
# Override with `--build-arg BUILD_CONFIGURATION=production` for a real prod image.
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
