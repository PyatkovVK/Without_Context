# Without Context

**Without Context** is a desktop app for working with AI using the context of a local folder. It collects text documents and source code, replaces sensitive values with placeholders, and lets you review the result before sending it to a selected model.

Built with **C# / .NET 10 / Avalonia**, the app targets Windows, Linux Mint and macOS on Intel and Apple Silicon. The entire interface is in **English by default**. Use **Settings → Language** to switch between **English** and **Русский** without restarting the app or clearing your session.

## How it works

```text
Folder → local collection and anonymization → local trimming → review
       → message anonymization and review → selected model
       → reply with optional local restoration of values
```

Original files stay unchanged. The app does not save chat history, the API key or the replacement map to disk. The chat model receives a system instruction, reviewed context with file aliases, and anonymized conversation history.

## Features

- Select a folder through the system dialog or enter a path.
- Chat with a downloaded local **Ollama** model or a remote model through a compatible Chat Completions API.
- Four protection profiles: everything; secrets and personal data; internal data and secrets; keys, passwords and tokens only.
- Custom values to replace, such as companies, projects or surnames, one value per line.
- Optional local Ollama agent for additional sensitive entity detection.
- Review and edit both context and messages before sending.
- Streaming replies, cancellation, independent chats in memory and session cleanup.
- Collapsible chat and model sidebars, with preferences in a modal Settings window.
- Separate connection indicators for the cleaner and assistant models.
- Minimal, moderate, maximum and custom context trimming, with recent-message retention.
- Archive and restore chats in memory.
- Formatted replies with headings, bold text, lists, code blocks and tables.
- Local mapping between file aliases and real files, and optional restoration of values in replies.
- English and Russian interface text, including labels, hints, progress messages and errors.

## Run the app

Install .NET SDK 10. Local chat also requires Ollama to be installed and running, with a model downloaded. Remote APIs require an endpoint, a model ID and an API key if the provider requires one.

From the repository root:

```sh
dotnet run --project src/ContextGuard.App/ContextGuard.App.csproj
```

Alternatively, use the launch script for your platform:

```powershell
# Windows
./scripts/run.ps1
```

```sh
# macOS / Linux Mint
sh scripts/run.sh
```

Both scripts disable .NET and Avalonia build telemetry. The Windows script uses a local SDK in `.tools/dotnet` if one is available; otherwise it uses `dotnet` from PATH.

Linux needs an X11 desktop environment and system libraries for X11, fontconfig and ICU. See the platform requirements for [Avalonia 11](https://v11.docs.avaloniaui.net/docs/overview/supported-platforms/) and [.NET 10](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md). Startup has been verified on Windows; Linux Mint and macOS startup have not yet been tested.

## Your first conversation

1. Open **Settings** in the left sidebar to choose the interface language, anonymization profile and custom values to replace.
2. Open **Folder context** and select a folder containing documents or source code.
3. In the right **Chat settings** panel, expand **Local privacy model** to optionally enable a downloaded Ollama cleaner. Expand **Model for your requests** to select the assistant:
   - **Ollama:** the default address is `http://localhost:11434`. Download a model, refresh the list or enter its name manually.
   - **API:** enter a full HTTPS endpoint such as `https://<provider>/v1/chat/completions`, a model ID and an API key if required. Bearer authorization, SSE and ordinary JSON responses are supported.
4. Select the **Context trimming** mode. Custom instructions require an enabled downloaded local cleaner.
5. In **Folder context**, click **Prepare context**, review the anonymized, reduced text and confirm the review. Use **Back to chat** to return to the conversation.
6. Write your task and click the arrow or press Ctrl+Enter to **Review message**. Review the preview, then click **Send**.

The MVP interface follows the supplied three-panel design: a dark navy chat list, a central conversation with right-aligned user bubbles and a bottom composer, and compact model cards and trimming controls on the right. Both sidebars collapse. The conversation title menu opens the chat, context, replacement map and file list. Model cards expand to reveal connection details; general preferences open in a modal window.

Use **Archive chat** to hide a conversation, **Archive** in the left sidebar to open it, and **Restore chat** to return it to the main list. Archiving preserves that chat's context, messages, model settings and draft. Deleting releases its state; clearing all or closing the app releases active and archived chats. The archive is kept only in RAM and does not survive a restart.

Each chat owns its context, messages and replacement map. New chats copy model, protection and trimming configuration but require their own context preparation. Titles use the first anonymized user message, with a numbered fallback for empty chats.

Changing the folder, anonymization rules, trimming mode or active custom instructions requires preparing context again. Editing context revokes approval and clears the conversation. Edits to a message preview are also checked before sending. Changing the interface language preserves these settings, context, approval, message drafts and chat history; it does not translate your documents or existing model replies. The language choice applies to the current session and starts in English on the next launch.

## Connection indicators

The top bar shows separate states for the **Cleaner** and **Assistant**: not checked, connecting, connected or failed. Expand a model card in **Chat settings** and click **Connect**. Editing its model, endpoint or credentials resets its state.

Ollama checks confirm that the selected model is available locally. Remote API checks use the compatible `/models` endpoint and verify the selected model ID using the supplied credentials, without sending documents, prompts or generating a reply. APIs that do not support this check stay **Not verified** until a successful chat request. Indicators reflect the last check or request, not continuous background monitoring. With the Ollama cleaner disabled, the cleaner indicator shows **Local rules**.

## Context trimming

Trimming runs after anonymization and before review. Presets shorten file excerpts locally while preserving file aliases and complete placeholders. They allocate the available space across collected files; they do not generate semantic summaries. Short context is kept unchanged when it fits the budget.

| Mode | Folder text budget | Previous messages sent |
| --- | --- | --- |
| Minimal | 75% of the collection limit | Up to 12 |
| Moderate (default) | 50% | Up to 6 |
| Maximum | 25% | Up to 2 |
| Custom | 50%, with local editorial instructions | Up to 6 |

With the default 24,000-character collection limit, the text budgets are 18,000 / 12,000 / 6,000 / 12,000 characters. Message limits retain complete user/assistant pairs and exclude the new message and required system/context messages. Older messages stay visible in the chat and are omitted from subsequent model requests. These are character and message limits, not exact token counts.

**Custom** sends anonymized folder text and anonymized instructions (up to 1,000 input characters) to the enabled local Ollama cleaner. Its model metadata is validated before the request. The result is anonymized again and limited to the text budget, then shown for review. Custom instructions apply to folder text; history uses the fixed six-message limit. Failed or cancelled preparation produces no approved context. There is no remote fallback.

## Context protection

All profiles check supported key and token formats, password fields, JWTs, Bearer tokens, private keys and connection strings. Additional profiles cover contacts, personal data, internal IPs/domains and paths. Custom values apply to every profile.

When the local agent is enabled, the original text is sent to the selected **local Ollama model**. Its endpoint is restricted to loopback addresses, model metadata is checked before requests, and cloud models and redirects are blocked. An agent failure stops processing; there is no fallback to a remote service.

The replacement map stays in session memory. Restoring values and real file names affects local display only; outbound history stays anonymized. Remote APIs require HTTPS, with the API key used in the authorization header. HTTP redirects and the system HTTP proxy are disabled.

**Anonymization may miss sensitive fragments or replace more than intended.** The Everything profile enables all implemented categories, but cannot guarantee removal of every secret. Review the text before sending. Protection assumes a trusted OS and local Ollama installation; the app does not control third-party logs or system memory dumps.

## Limitations

- Supports UTF-8 and UTF-16 text files, including Markdown, source code, JSON/YAML/XML, CSV and logs. PDF, Word, Excel and other binary formats are skipped.
- Excludes `.env*`, keys, `secrets.*`/`credentials*`, service folders, dependencies, build output and links/junctions.
- Limits: 128 KiB per file, 160 included files and 15,000 inspected entries. The default text limit is 24,000 characters, adjustable from 4,000 to 96,000. Oversized files are skipped entirely; the summary indicates incomplete context.
- Context may exceed the selected model's capacity. Ollama is requested to use a 32,768-token window; reduce the context limit if memory is insufficient.
- The model suggests changes as text. It has no disk access, tools or ability to apply patches automatically.

Ollama API references: [chat](https://docs.ollama.com/api/chat), [model list](https://docs.ollama.com/api/tags), [model metadata](https://github.com/ollama/ollama/blob/main/api/types.go).

## Project structure

```text
src/
  ContextGuard.App/       Without Context UI, workspace and per-chat state
  ContextGuard.Core/      preparation workflow, collection, anonymization, trimming and model APIs
    Localization/        shared English and Russian text resources
tests/
  ContextGuard.Tests/     data processing and HTTP request tests
  ContextGuard.UiTests/   interface scenarios without a visible window
scripts/                 application launch scripts for Windows and macOS/Linux
licenses/                third-party licenses
```

To update a translation, edit `Strings.resx` for English or `Strings.ru.resx` for Russian in `src/ContextGuard.Core/Localization/`. Keep resource keys and format placeholders consistent across both files.

Local SDKs, dependencies, builds, screenshots and temporary files are kept in ignored `.tools/`, `.dotnet-home/`, `artifacts/`, `bin/`, `obj/` and `.test-work/` directories.

## License

See [LICENSE](LICENSE) for the project license. Third-party components are listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md), with license texts in `licenses/`.
