# Memory Index

## Project / env gotchas
- [Secure cookie needs HTTPS proxy](secure-cookie-needs-https-proxy.md) — refresh cookie always Secure; session-survives-reload only over real TLS (nginx https://acme.myhrm.org), not plain HTTP. Self-signed *.myhrm.org cert → ignore-HTTPS-errors. Token rotates per refresh.
- [Unknown-subdomain repro](unknown-subdomain-repro.md) — nope.myhrm.org NOT in /etc/hosts (real browser dies at DNS); workspace-not-found wall is path-dependent: SPA shell on /, "workspace does not exist" HTML only on /api/*.
