# Model connections and workspace readiness

Status: Implemented.

## Behavior

- Each verified model shows a compact card containing its selected name, connection status, location and an accessible pencil action. Local cleaner and request assistant use the same presentation.
- The pencil opens the corresponding editor. Opening the editor alone preserves configuration, connection verification, reviewed context and drafts. Successful reconnection closes the editor and restores the card.
- Editing model names, endpoints or API credentials revokes that connection's verification. Changing the cleaner also invalidates prepared context under the existing session rules. Failed and cancelled checks cannot retain a green verified state; late results cannot verify changed settings.
- One verified request model is sufficient: local Ollama or an external API. The main area and message drafting become available immediately. A folder and a second local model are optional. Before connection, explain these alternatives clearly; ordinary local protection rules always work.
- Explicitly connecting a local model also enables that same downloaded local model for local processing when no separate cleaner has been chosen. Connecting the local cleaner alone assigns it as the request assistant when no assistant is connected. Discovery and refreshing still do not enable models or approve content.
- Automatically linked local processing follows later local assistant selections. Explicitly switching to an external assistant releases that automatic link and uses local protection rules; an independently configured optional local cleaner remains the user's choice. A failed local request never silently falls back to remote processing.
- With no folder, initialize an isolated in-memory protection session for ordinary chat; do not fabricate context approval. With a selected folder, an explicit connection or folder-picker choice prepares context automatically. After a typed folder/settings change, the first message preview prepares missing context and asks for review. Nonempty folder or restored context still requires approval; messages always require their preview/review step.
- Custom trimming with unavailable prerequisites uses the ordinary Moderate preset and an inline explanation. Keep the requested mode and instructions in session memory for when a local model and instructions become available. Do not display a missing-Ollama setup error for this normal workflow.
- A request in progress retains the previous successful verification while its activity indicator changes. A failed or cancelled request revokes verification and restores setup guidance. A new chat does not inherit verification from another session.
- Reopened chats require their selected assistant connection again. A restored nonempty context requires review even if no source folder is selected. Initial optional folder/cleaner configuration retains their safe saved transcript and revokes review; preparing new context resets the transcript as before.
- External APIs offer a visible API-key switch. With it off, clear the old key and omit authorization headers. Providers must actually allow anonymous requests; no registration or payment bypass is implied. Explicit Connect can use a fixed one-token test when model metadata is unavailable, without session data.
- English and Russian presentation is available. Readiness and connection state stay in session RAM only.
- [Header switches](../main-area/model-switches.md) can pause each role independently without changing model settings or stopping the server. A manually disabled cleaner stays disabled when reconnecting the assistant; enabling the cleaner does not restore a manually disabled assistant. Existing conversations remain readable while the assistant is paused.

## Acceptance criteria

1. Names alone do not unlock the main area. Connect Ollama alone and complete a chat without a folder; separately connect an external API with no local model and complete a chat.
2. Show each connected card, edit its settings, reconnect, and confirm review is preserved until a setting actually changes.
3. Change the assistant or endpoint, fail or cancel a check, and complete an old request after changing settings. None may leave the workspace incorrectly verified. Removing the optional folder must preserve ordinary chat availability.
4. Verify automatic folder preparation and Moderate fallback for unavailable custom trimming; preserve explicit content review, responsiveness, session isolation and reopening behavior.
5. Toggle key authentication, verify that anonymous metadata/chat/title requests contain no old credentials, and reject providers that require authentication with a clear explanation. Preserve the approved animation baseline.
