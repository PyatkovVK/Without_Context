# Without_Context

**ContextGuard** is a desktop app for working with AI using the context of a local folder. It collects text documents and source code, replaces sensitive values with placeholders, and lets you review the result before sending it to a selected model.

Built with **C# / .NET 10 / Avalonia**, the app targets Windows, Linux Mint and macOS on Intel and Apple Silicon. The entire interface is in **English by default**. Use **Additional options → Language** to switch between **English** and **Русский** without restarting the app or clearing your session.

## How it works

```text
Folder → local collection and anonymization → context review
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
- Streaming replies, cancellation, a new chat and session cleanup.
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

1. Choose the interface language under **Additional options → Language**, if needed.
2. Select a folder containing documents or source code.
3. Select an anonymization profile. Optionally enter custom values, one per line, with at least two characters. Matching is case-insensitive.
4. Select a model:
   - **Ollama:** the default address is `http://localhost:11434`. Download a model in Ollama, then click **Refresh Ollama models** or enter its name manually.
   - **API:** enter a full HTTPS endpoint such as `https://<provider>/v1/chat/completions`, the model ID and an API key. Bearer authorization, SSE and ordinary JSON responses are supported.
5. Click **Prepare context**, review the **Context** tab, remove unnecessary content and confirm the review.
6. In **Chat**, describe your task and click **Review message** or press Ctrl+Enter. Review the message, then click **Send**.

Changing the folder or anonymization rules requires preparing context again. Editing context revokes approval and clears the conversation. Edits to a message preview are also checked before sending. Changing the interface language preserves these settings, context, approval, message drafts and chat history; it does not translate your documents or existing model replies. The language choice applies to the current session and starts in English on the next launch.

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
  ContextGuard.App/       Avalonia interface and conversation state
  ContextGuard.Core/      file collection, anonymization and model APIs
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
