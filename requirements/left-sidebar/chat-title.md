# Chat title

Status: Implemented.

## Purpose

Briefly describe the conversation's goal: what the user wants to obtain or accomplish.

## Behavior

- The temporary localized title "Chat N" is used until the first successful reply.
- After a successful reply, a short title is derived from the first anonymized task, without greetings or introductory phrases.
- The selected model asynchronously refines the title using that task and an excerpt of already reviewed, anonymized context.
- Example: "Hi! Help me create a product launch plan" becomes "Product launch plan".
- Titles contain at most 64 characters. The model is instructed to produce 3–7 words in the task's language.
- Known secrets and restored original values are excluded. Placeholders are removed from the title.
- Failed refinement preserves the local fallback title and does not cancel the chat reply.
- Titles remain stable during follow-up messages and interface language changes.
- Titles are saved, restored after startup and included in exports.
- Clearing the conversation or preparing new context resets the title. A late result cannot rename a cleared or deleted chat.
- Older chat files without a title field remain supported: a fallback is derived from the stored anonymized task.

## Acceptance criteria

Check meaningful titles after the first task, English and Russian requests, model failure, cancellation during refinement, chat deletion and title restoration after restarting.
