// This file MUST stay at the wwwroot root, next to index.html. A service worker's
// scope is limited to its own directory and below, and GitHub Pages can't set the
// 'Service-Worker-Allowed' header to widen it — so moving it into js/ would stop it
// from controlling the app and break offline support. index.html registers it by the
// bare name 'service-worker.js', and on publish MSBuild swaps in service-worker.published.js
// under this same name/location (see the <ServiceWorker> item in WebApp.csproj).

// In development, always fetch from the network and do not enable offline support.
// This is because caching would make development more difficult (changes would not
// be reflected on the first load after each change).
self.addEventListener('fetch', () => { });
