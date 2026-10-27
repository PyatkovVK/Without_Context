# External API authentication

Status: Implemented for OpenAI-compatible endpoints that permit anonymous requests.

## Behavior

- Show the endpoint, model and an explicit "Use an API key" switch. No-key mode is the initial state. Entering a nonempty key enables key mode; switching it off clears that credential, resets connection verification and omits authorization from later requests.
- Authentication mode and credentials are per-chat RAM configuration only. They are not stored with chats or sidebar preferences. A new live chat may copy the current configuration but must obtain its own connection verification.
- A provider that accepts unauthenticated requests can be used without creating an application account or supplying an API key. Blank credentials are not proof of free access. Pricing, quotas and registration requirements belong to the chosen provider.
- Official OpenAI and Claude APIs require authenticated access. Do not advertise them as anonymous free services, embed a shared key, scrape their web chats or route documents through an unselected proxy.
- Explicit Connect first checks metadata. If the API has no supported model-list check, it may send one fixed neutral prompt (`Reply OK.`), `stream: false`, and `max_tokens: 1`. The fallback has a 15-second deadline, a 16 KiB response bound, and validates an assistant-message response. It never includes a folder, original user text, context, conversation history or image. Inform the user that this request counts toward provider limits.
- Metadata-only callers retain their existing behavior and never generate a response. Authentication failure never invokes a bypass or a different provider. In no-key mode, explain that the chosen service requires authentication and show the key-mode action in the existing editor.
- Actual chat and optional title requests still use only anonymized text, validated HTTPS endpoints, disabled redirects/proxy, bounded responses and normal review. Anonymous mode does not weaken data protection.

## Acceptance criteria

Verify metadata without credentials; a fixed bounded fallback only after unsupported metadata; malformed response and authentication rejection; switching off a previously entered key; external-only reviewed chat and folder preparation without local inference. Use synthetic handlers without calling unapproved public proxies.

## Official references

- [OpenAI API authentication](https://developers.openai.com/api/reference/overview)
- [Claude API prerequisites and authentication](https://platform.claude.com/docs/en/api/overview)
