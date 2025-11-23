const { env } = require('process');

const target = env.ASPNETCORE_HTTPS_PORT ? `https://localhost:${env.ASPNETCORE_HTTPS_PORT}` :
  env.ASPNETCORE_URLS ? env.ASPNETCORE_URLS.split(';')[0] : 'https://localhost:7233';

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
    headers: {
      Connection: 'Keep-Alive'
    }
  }
]

module.exports = PROXY_CONFIG;
