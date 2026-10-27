# Local privacy model and Ollama setup

Status: Implemented on Windows. Automatic terminal installation on macOS/Linux is planned.

## Purpose

Guide the user from a missing or stopped Ollama installation to choosing a downloaded local privacy model, without leaving them to diagnose a generic connection error.

## Behavior

- Each opening of the local privacy model card checks the installation and configured loopback API again. Refresh buttons repeat both checks and obtain a fresh model list, including downloads completed outside the application.
- On Windows, look first for `%LOCALAPPDATA%\Programs\Ollama\ollama.exe`, then `%ProgramFiles%\Ollama\ollama.exe`, then at most 64 nonempty PATH entries. Do not recursively scan the disk. A file picker lets the user locate `ollama.exe` in a custom installation directory.
- Display the discovered executable path with ellipsis and a full-path tooltip. The path remains in this chat's memory and is never included in stored chats, exports, requests or diagnostics.
- If neither the CLI nor the server is found, show an explanation, "Locate Ollama" and an action opening the official Windows download page at https://ollama.com/download/windows. The application does not silently install software. After installation the user repeats the check.
- If the executable is found but the server does not respond, offer "Start Ollama". Launch `ollama serve` in the background only after that action, with the configured loopback host and cloud disabled. Wait at most 25 seconds for the metadata API; show a safe error on failure. Do not start another server when one is already available.
- A successful `/api/tags` response means the server is running, including an empty model list. Display the count and an "Installed models" selector with a refresh button beside it, even while the cleaner is disabled. Exclude cloud entries. With no models, keep the disabled selector and refresh visible and explain how to download a model. A running server without a discoverable executable still shows both model lists and offers locating the CLI for installation.
- Detection and refreshing do not enable the cleaner, select a model, approve context or clear reviewed context, chat history or drafts. The cleaner checkbox and explicit model selection retain their existing approval invalidation rules.
- When the server is running, show a separate "Free models to download" selector with a fixed catalog: `qwen3:0.6b`, `qwen3:1.7b`, `qwen3:4b`, `qwen3:8b`, `llama3.2:3b`, `gemma3:1b`, `gemma3:4b`. Display the approximate download size for the chosen tag. The catalog is presentation data, not a live remote catalog or an installation cache. Keep manual model-name entry in an optional expander.
- Choosing a download updates only the installation input; it never changes the active cleaner or revokes review. Mark an already installed exact tag and disable duplicate installation. Enable "Install in terminal" only when the executable is available and the model name is valid. Keep endpoint and manual active-model settings in an optional connection expander.
- Preserve a still-installed active model on refresh. If it was removed, show an empty selection and a missing-model hint, reset the cleaner connection indicator, and leave settings and reviewed context unchanged until the user explicitly chooses another model.
- On Windows, the install action opens a visible PowerShell terminal with `ollama pull` for the chosen model and the same local host. Keep the terminal open so progress and errors can be inspected. Validate the model name, reject cloud names and shell expressions, and quote the executable path safely. Do not require administrator privileges.
- Opening the terminal does not imply a successful download. Instruct the user to refresh after the command finishes and choose the installed model. No background polling or automatic approval is introduced.
- Server startup and pull actions require a loopback HTTP address with no path. Custom metadata endpoints still pass the existing local endpoint validation. Errors from an answering server are not described as an absent installation.
- Checks use metadata only, have cancellation and deadlines, and block chat navigation while running. Changing the endpoint or closing the session cancels its operation and rejects late results. Cancelled or failed checks cannot publish old models as a fresh success.
- Each chat owns its setup state in RAM. No model-list cache or durable installation preference is introduced. An explicitly started Ollama server and external download terminal can continue after the application closes.
- English and Russian labels, statuses and safe errors are available.
- Before every raw-text request, the existing endpoint and downloaded-local-model validation still runs. A setup result never authorizes sending raw text.

## Acceptance criteria

1. With no installation and no local server, open the card and verify locate, download and refresh actions.
2. Check discovery in the standard Windows directory and PATH, and manual selection in a path containing spaces or an apostrophe. Cancelling the picker preserves the current state.
3. With a stopped installation, start it from the card. Verify readiness, retry after failure and the configured loopback port; an already running server is reused.
4. Check empty and populated installed-model selectors, cloud exclusion, a server without a discoverable CLI and a fresh list after installing a model in the terminal. Refresh and reopening must make a new metadata request; a failure clears the displayed installation list. Removed selections are explained without silently selecting another model.
5. Inspect the terminal command and progress. Reject shell fragments, oversized names and cloud models. Do not claim successful installation before refresh.
6. Preserve review, history, draft and cleaner enablement during discovery. Confirm metadata checks contain no user text. Change endpoint or close during a delayed check and verify late state is ignored.
7. Switch language, use the compact window and verify that actions remain reachable through the settings scroll area.
8. Choose different free download options, verify their approximate size and the exact terminal tag, then refresh after completion. An installed tag is marked and cannot be redundantly downloaded. Changing only the download choice preserves cleaner settings and review; explicitly choosing a different installed cleaner revokes review.

## Official references

- [Ollama on Windows](https://docs.ollama.com/windows)
- [Ollama CLI](https://docs.ollama.com/cli)
- [Windows download](https://ollama.com/download/windows)
- [Installed model metadata](https://docs.ollama.com/api/tags)
- [Qwen 3 tags and download sizes](https://ollama.com/library/qwen3/tags)
- [Llama 3.2 tags and download sizes](https://ollama.com/library/llama3.2/tags)
- [Gemma 3 tags and download sizes](https://ollama.com/library/gemma3/tags)
