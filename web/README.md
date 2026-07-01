# Web — AI Support Platform Dashboard

React 19 + TypeScript + Vite + Tailwind v4.

## Setup

```bash
npm install
cp .env.example .env   # then point VITE_API_BASE_URL at your running API
npm run dev
```

Dashboard at `http://localhost:5173`. Requires the backend (`/api`) running
for login/data — see the root README.

## Scripts

| Script             | Purpose                                  |
|---------------------|-------------------------------------------|
| `npm run dev`        | Vite dev server with HMR                  |
| `npm run build`      | Type-check (`tsc -b`) + production build  |
| `npm run typecheck`  | Type-check only, no build output          |
| `npm run lint`       | `oxlint` — fast Rust-based linter          |
| `npm run preview`    | Preview the production build locally      |

## Structure

```
src/
├── components/   Sidebar, DashboardLayout, ProtectedRoute, shared bits
├── context/      AuthContext (+ split-out useAuth hook, fast-refresh friendly)
├── lib/          api.ts (axios client), types.ts (DTOs), useChatHub.ts (SignalR widget hook)
├── routes/
│   ├── auth/       Login, Register
│   ├── dashboard/  Overview, Conversations (more pages land per sprint)
│   └── widget/     WidgetPage — the embeddable chat UI, outside the dashboard shell
└── App.tsx        Route tree

public/
├── widget-loader.js   Vanilla-JS embed script clients paste on their own site
└── widget-test.html   Plain HTML page for manually verifying the embed
```

## Web chat widget (Sprint 3)

`widget-loader.js` is intentionally framework-free — it's served as a static
asset (not bundled through React) so it loads instantly on a client's site
with zero dependency on this app's own build. It injects a floating bubble
that, on click, lazily creates an iframe pointed at `/widget/chat?key=...`
— that route renders `WidgetPage.tsx`, which talks to the backend's
`ChatHub` over SignalR via the `useChatHub` hook. See the root README's
"Testing the web chat widget end-to-end" section to try it locally.

## Auth flow

1. `POST /api/auth/login` → `{ accessToken, agent }`.
2. Token stored in `localStorage` (swap for an httpOnly cookie + refresh-token
   rotation before going to production — current setup is foundation-grade,
   not hardened).
3. `axios` request interceptor (`lib/api.ts`) attaches `Authorization: Bearer`
   to every request; a response interceptor clears the token on `401`.
4. `ProtectedRoute` redirects to `/login` when there's no authenticated agent.

## Design tokens

Defined in `src/index.css` via Tailwind v4's `@theme` block — `ink-*` (dark
surfaces), `teal-*` / `mint-300` (brand), `coral-500` / `amber-500` /
`green-500` (status colors). Same palette as the product roadmap deck, so the
dashboard and the pitch materials read as one product.
