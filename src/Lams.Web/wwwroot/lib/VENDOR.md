# Vendored front-end assets

Self-hosted so that the Content-Security-Policy can stay `'self'` only (no third-party CDN at runtime).

| Asset | Version | Source | SHA-256 | License |
|---|---|---|---|---|
| `htmx/htmx.min.js` | 2.0.11 | https://unpkg.com/htmx.org@2.0.11/dist/htmx.min.js | `d6fdc75f204e6bdefa99b69bf1e6d4ac69b8a364f77929f45c13476b4000f717` | 0BSD (`htmx/LICENSE`) |
| `../fonts/inter-latin-{400,500,600,700}-normal.woff2` | @fontsource/inter 5.3.0 | https://cdn.jsdelivr.net/npm/@fontsource/inter@5.3.0/files/ | 400 `8909904a…07edc`, 500 `f3779f1e…ec4bdcdf`, 600 `f9a06e79…78f21a`, 700 `6f56409f…be2491` | SIL OFL 1.1 (`../fonts/Inter-OFL.txt`) |
| `../fonts/jetbrains-mono-latin-{400,500}-normal.woff2` | @fontsource/jetbrains-mono 5.3.0 | https://cdn.jsdelivr.net/npm/@fontsource/jetbrains-mono@5.3.0/files/ | 400 `14425ba9…89eb`, 500 `cb182fee…553a` | SIL OFL 1.1 (`../fonts/JetBrainsMono-OFL.txt`) |

Only the Latin subset is included. Inter has no Georgian glyphs; Georgian text falls back to the system UI font
(see the `font-family` stack in `css/site.css`).

To update: download the new files, update this table (version, source, `sha256sum`), and test the pages.
