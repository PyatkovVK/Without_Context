# Without Context

**Without Context** is a desktop app for working with AI using the context of a local folder. It collects text documents and source code, replaces sensitive values with placeholders, and lets you review the result before sending it to a selected model.

Built with **C# / .NET 10 / Avalonia**, the app targets Windows, Linux Mint and macOS on Intel and Apple Silicon. The entire interface is in **English by default**. Use **Settings → Language** to switch between **English** and **Русский** without restarting the app or clearing your session.

## How it works

```text
Folder → local collection and anonymization → local trimming → review
       → message anonymization and review → selected model
       → reply with optional local restoration of values
```

Original files stay unchanged. Anonymized chat history and context are saved locally; API keys, original values and the replacement map stay in memory. The chat model receives a system instruction, reviewed context with file aliases, and anonymized conversation history.

## Features

- Select a folder through the system dialog or enter a path.
- Chat with a downloaded local **Ollama** model or a remote model through a compatible Chat Completions API.
- Four protection profiles: everything; secrets and personal data; internal data and secrets; keys, passwords and tokens only.
- Custom values to replace, such as companies, projects or surnames, one value per line.
- Optional local Ollama agent for additional sensitive entity detection.
- Review and edit both context and messages before sending.
- Streaming replies, cancellation, independent chats and session cleanup.
- Collapsible chat and model sidebars, with preferences in a modal Settings window.
- Separate connection indicators for the cleaner and assistant models.
- Minimal, moderate, maximum and custom context trimming, with recent-message retention.
- Automatically save chats and archives in a separate local folder, and restore them after restarting.
- Export one chat or all chats as JSON and Markdown.
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

## macOS and Linux application icons

All logo variants and platform icons are in `src/ContextGuard.App/images`, which contains only image files. The selected logo is `icon.png` for the interface and window, `icon.ico` for Windows, `icon.icns` for macOS, and `icon-256.png` for the Linux launcher. The same logo appears beside the application name on every platform.

On macOS, `sh scripts/run.sh` builds and opens a native `.app` bundle so Finder and the Dock use the application icon. To prepare a Release bundle on an Intel or Apple Silicon Mac:

```sh
sh scripts/package.sh
```

The result is `artifacts/osx-x64/Without Context.app` or `artifacts/osx-arm64/Without Context.app`. You can move the bundle to Applications. Packaging follows the [Avalonia macOS bundle structure](https://docs.avaloniaui.net/docs/deployment/macos) and applies an ad-hoc signature when run on macOS.

On Linux Mint, publish the app and register its launcher for the current user:

```sh
sh scripts/package.sh
sh scripts/install-linux.sh artifacts/linux-x64
```

For ARM64 Linux, use `artifacts/linux-arm64` in the second command. Registration puts the launcher and icon in the user's XDG data directory, normally `~/.local/share/applications` and `~/.local/share/icons`. The launcher points to the published directory, so keep that directory in place or run registration again after moving it. Its `StartupWMClass` matches the application's X11 class to associate the running window with the launcher.

The published Linux directory also includes `install.sh` and its launcher template. After moving the whole directory to another Linux machine, run `sh install.sh` from that directory to register the icon and launcher there.

You can also select a target explicitly, for example `sh scripts/package.sh osx-arm64` or `sh scripts/package.sh linux-x64`. Published applications require .NET Runtime 10 on the destination machine. Native Finder/Dock and Linux desktop display still need verification on their respective operating systems.

When a macOS bundle is created on Windows, restore its executable permission on the Mac with `chmod +x "Without Context.app/Contents/MacOS/WithoutContext"`, or build it directly on the Mac with `sh scripts/package.sh`. The Linux launcher installer restores the executable permission itself.

## Your first conversation

1. Open **Settings** in the left sidebar to choose the interface language, anonymization profile and custom values to replace.
2. In the right sidebar's **Folder context** section, select a folder containing documents or source code.
3. In the right **Chat settings** panel, expand **Local privacy model** to optionally enable a downloaded Ollama cleaner. Expand **Model for your requests** to select the assistant:
   - **Ollama:** the default address is `http://localhost:11434`. Download a model, refresh the list or enter its name manually.
   - **API:** enter a full HTTPS endpoint such as `https://<provider>/v1/chat/completions`, a model ID and an API key if required. Bearer authorization, SSE and ordinary JSON responses are supported.
4. Select the **Context trimming** mode. Custom instructions require an enabled downloaded local cleaner.
5. In the right sidebar, click **Prepare context**. Review the anonymized, reduced text in the center and confirm the review. Use **Review** to reopen the preview and **Back to chat** to return to the conversation.
6. Write your task and click the arrow or press Ctrl+Enter to **Review message**. Review the preview, then click **Send**.

The MVP interface follows the supplied three-panel design: a dark navy chat list, a central conversation with right-aligned user bubbles and a bottom composer, and folder context, model cards and trimming controls on the right. Both sidebars collapse with eased width transitions. The conversation title menu opens the chat, context, replacement map and file list. Model cards expand to reveal connection details; general preferences open in a modal window with a dimmed, inactive background and no page scrollbar.

Use the **⋮** menu on any chat in the left sidebar to archive or delete that particular chat. Archived rows offer **Restore chat**. Actions on an inactive chat preserve your current conversation and draft. **Archive** at the bottom of the sidebar opens the archive. Archiving preserves that chat's live state while the app is open, and its anonymized transcript, context and archive membership across restarts. Deleting a chat or choosing **Clear all chats and keys** also removes the saved chats. Closing the app waits for pending saves, then releases sensitive session memory.

## Local storage and export

Chats are stored as versioned JSON files with opaque IDs in the operating system's local application-data folder, under **WithoutContext/chats**. On Windows this is `%LOCALAPPDATA%\WithoutContext\chats`. The exact path is displayed in **Settings → Chat storage folder**, where you can select and copy it.

**Export chat** at the bottom of the right sidebar exports the selected conversation. **Settings → Export all chats** also includes archived chats. Choose an output folder using the system dialog; each chat produces `chat-<opaque-id>.json` and `chat-<opaque-id>.md`. JSON includes the title, anonymized context, completed message pairs, timestamps and archive flag. Markdown provides the same title with a readable transcript and context. Exported copies remain in that folder after you delete a chat in the application. They are also ignored by Git when exported into this repository. Version 2 adds the title; existing version 1 chats without it remain readable.

Stored and exported data uses the anonymized messages, including when local display restores original values. Connection settings, API keys, original folder/file paths, original-value mappings, custom replacement lists, unsent drafts, incomplete responses and review approval are excluded. Edited context is excluded until a new safe snapshot is available. These are plain-text files, not encrypted files; the selected protection profile and your review still determine what information is retained.

After restarting, chats reopen with anonymized text and a fresh replacement map. Select the assistant model, re-enter credentials if needed, optionally re-enable and configure the local cleaner, then use **Review** in the right sidebar and confirm the saved context before continuing. Original values cannot be restored from old placeholders because the original map is deliberately not saved. Initial model configuration preserves the reopened transcript; preparing new folder context or editing context resets it as usual.

Storage limits are 200 chats, 512 completed messages per chat, 8 MiB per JSON file and 64 MiB in total. Invalid or unsupported saved files produce a visible error without replacing existing files. Save failures are shown in the main window; if pending changes cannot be saved when closing, you can keep the app open or explicitly close without saving. Export or delete old conversations before reaching the limits.

Each chat owns its context, messages and replacement map. New chats copy model, protection and trimming configuration but require their own context preparation. After the first successful reply, a short title describes the user's goal. The selected assistant refines it in the background with one short additional request containing at most 2,000 characters of the anonymized task and 4,000 characters of the already reviewed anonymized context. The output is anonymized again and limited to 64 characters. Failure keeps the local title without affecting the completed reply. Names stay stable across follow-up messages and language changes, survive restart and appear in exports. Empty chats use a numbered name; clearing history or preparing new context resets it.

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
requirements/           product requirements in English, grouped by interface area
```

To update a translation, edit `Strings.resx` for English or `Strings.ru.resx` for Russian in `src/ContextGuard.Core/Localization/`. Keep resource keys and format placeholders consistent across both files.

Product behavior and acceptance criteria are described in [the requirements index](requirements/README.md). The left-sidebar folder contains separate documents for new chats, recent chats, names, the ⋮ menu, archives, settings and collapsing. Keep the relevant requirement document in sync when changing a feature; use fictional examples and keep private inputs out of these files.

Local SDKs, dependencies, builds, screenshots and temporary files are kept in ignored `.tools/`, `.dotnet-home/`, `artifacts/`, `bin/`, `obj/` and `.test-work/` directories.

## License

See [LICENSE](LICENSE) for the project license. Third-party components are listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md), with license texts in `licenses/`.
