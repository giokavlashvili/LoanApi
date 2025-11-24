# Dockerfile for Angular Development Server
FROM node:22-alpine

WORKDIR /app

# Copy package files
COPY package*.json ./

# Install dependencies
RUN npm ci

# Copy Angular application source
COPY . .

# Expose Angular dev server port
EXPOSE 44448

# Start Angular development server
# Using ng serve directly with proxy config for Docker environment
CMD ["npx", "ng", "serve", "--host", "0.0.0.0", "--port", "44448", "--proxy-config", "proxy.conf.js", "--disable-host-check"]

