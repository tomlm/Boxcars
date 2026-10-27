// Makes BoxCars installable as a PWA. Nothing is cached: the game runs over a live Blazor Server connection, so every
// request goes straight to the network, and new deployments are picked up without stale copies hanging around.
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));
