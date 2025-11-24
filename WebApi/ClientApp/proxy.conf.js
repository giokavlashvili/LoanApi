const { env } = require('process');

// Determine target based on environment
// In Docker, use service name; locally use localhost
const isDocker = env.DOCKER_ENV === 'true';
let target;

if (isDocker) {
  // In Docker, proxy to the webapi service
  target = 'http://webapi:80';
} else {
  // Local development
  target = env.ASPNETCORE_HTTPS_PORT ? `https://localhost:${env.ASPNETCORE_HTTPS_PORT}` :
    env.ASPNETCORE_URLS ? env.ASPNETCORE_URLS.split(';')[0] : 'https://localhost:7233';
}

const PROXY_CONFIG = [
  {
    context: [
      "/api",
      "/Identity",
      "/signin-google",
      "/signout-google",
      "/weatherforecast",
      "/WeatherForecast"
   ],
    proxyTimeout: 600000,
    target: target,
    secure: false,
    changeOrigin: true,
    headers: {
      Connection: 'Keep-Alive'
    }
  }
]

module.exports = PROXY_CONFIG;
