# Security Model & Deployment Guidance

RadiopaediaConnect is designed to run **inside a trusted clinical network** (hospital,
clinic, imaging practice). Read this before distributing or installing it.

## What the built-in authentication does — and does not — protect

The only user-level login in this application is **Radiopaedia OAuth**. That login is an
*identity*, not an *authorization*: **anyone can create a free Radiopaedia account**, so a
Radiopaedia login proves who the user is, but says nothing about whether they are allowed
to access your PACS.

Concretely, anyone who can both reach the web UI on your network **and** sign in with any
Radiopaedia account can:

- search the configured PACS nodes by patient name, ID, or accession number,
- retrieve and view full studies (including burnt-in demographics),
- submit cases to Radiopaedia under their own account.

> **Example**: a staff member with no PACS credentials at all — but with a browser on the
> hospital network and a free Radiopaedia account — gains effective read access to the
> entire PACS through this tool.

Admin functions (API keys, DICOM nodes, logs, all-cases view) are separately protected by
the local admin password set during first-run setup.

The DICOM C-STORE listener (port 104) accepts associations based on the **calling AE title
allowlist** only (every configured remote node's AE title is allowed). DICOM traffic is
not encrypted or authenticated beyond that — standard for intra-network DICOM, but it
means port 104 must also be treated as trusted-network-only.

## Deployment requirements

1. **Never expose this application to the internet.** No exceptions.
2. Deploy it on a network segment where every user is already trusted with PACS access —
   *or* place a secondary authentication layer in front (next section).
3. Restrict port 104 with a firewall so only your PACS hosts can reach it.
4. Protect the data directory (`/data`, or `C:\data` on Windows). It contains:
   - the SQLite database (OAuth tokens, Radiopaedia client secret, SMTP password, and
     patient demographics for submitted cases),
   - retrieved DICOM files, cached on disk for ~30 minutes before automatic purge.

## Recommended: secondary authentication in front of the app

If you cannot guarantee that everyone on the network should have PACS access, put an
authenticating reverse proxy in front of the web UI. The application is designed to be
friendly to this:

- All app authentication is cookie-based, so proxy-injected auth (basic auth, SSO
  forward-auth) layers cleanly on top without conflict.
- `X-Forwarded-For` / `X-Forwarded-Proto` headers are honoured (forward limit 2), so the
  app works correctly behind one proxy hop with TLS termination.
- The OAuth callback path `/signin-radiopaedia` is a normal browser redirect — the user's
  proxy session already exists when it fires, so it needs no special exemption. Just make
  sure the **public URL of the proxy** is what you register as the redirect URI in your
  Radiopaedia OAuth application settings.

### Bind the app so the proxy is the only way in

When running in Docker, publish the web port to loopback only and let the proxy connect
to it:

```bash
docker run -p 127.0.0.1:8080:5000 -p 104:104 -v /data:/data radiopaedia-connect
```

The DICOM listener port inside the container defaults to 104 and can be changed with the
`RCONNECT_SCP_PORT` environment variable (e.g. to run on an unprivileged port and map
`-p 104:11104` externally).

### Example: Caddy with basic auth

```caddyfile
rconnect.internal.example {
    basic_auth {
        # caddy hash-password
        clinician $2a$14$...hashed...
    }
    reverse_proxy 127.0.0.1:8080
}
```

### Example: nginx with basic auth

```nginx
server {
    listen 443 ssl;
    server_name rconnect.internal.example;

    auth_basic           "RadiopaediaConnect";
    auth_basic_user_file /etc/nginx/rconnect.htpasswd;

    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $remote_addr;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

### Example: SSO (Authelia / authentik / Keycloak)

Use your proxy's forward-auth integration (Traefik `forwardAuth`, nginx `auth_request`,
Caddy `forward_auth`) in front of the same `reverse_proxy` block. Because the app's own
session is an independent cookie, no app-side configuration is needed.

### Alternative: network-level control

A VPN/overlay network (e.g. WireGuard, Tailscale) or firewall rules restricting the web
port to a known set of workstation IPs achieve the same goal where a proxy is impractical.

## In-app enforcement

All API endpoints require an authenticated Radiopaedia session by default (enforced
globally, not per-endpoint). The only anonymous endpoints are the login flow, first-run
setup/status, and the admin endpoints, which are guarded by the admin session instead.
This is a floor, not a substitute for the network controls above.

## Reporting

If you find a security issue in RadiopaediaConnect, please report it privately to the
maintainer rather than opening a public issue.
