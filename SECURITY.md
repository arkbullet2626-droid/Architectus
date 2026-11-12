# Security

## How Architectus handles API keys

- Stored only in Windows Credential Manager (generic credential, per Windows user, DPAPI-encrypted).
- Never written to disk by Architectus in any other form: not in settings, logs, the library, or crash output.
- Sent only over HTTPS to the provider you selected, in its standard auth header (`Authorization`, `x-api-key` or `x-goog-api-key`). Never in a URL.
- Never shown again in the app after saving. Removing a key deletes the credential.

## Network

Architectus only contacts `api.openai.com`, `api.anthropic.com` and `generativelanguage.googleapis.com`, and only when you generate a prompt, test a key or load models. There is no update check, analytics or telemetry.

## Reporting a vulnerability

Please email support@opifexstudios.ca rather than opening a public issue. We'll reply as soon as we can.
