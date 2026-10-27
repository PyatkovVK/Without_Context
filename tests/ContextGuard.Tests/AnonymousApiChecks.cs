using System.Net;
using System.Text;
using System.Text.Json;
using ContextGuard.Core;

internal static class AnonymousApiChecks
{
    public static async Task Run()
    {
        bool metadata = true, reject = false, malformed = false;
        int probes = 0;
        using var client = new ModelClient(new StubHandler(async request =>
        {
            Check(request.Headers.Authorization is null, "Anonymous requests contain no credentials");
            if (reject) return new(HttpStatusCode.Unauthorized) { Content = new StringContent("echoed-secret") };
            if (request.Method == HttpMethod.Get)
                return metadata ? Response("{\"data\":[{\"id\":\"public-model\"}]}") : new(HttpStatusCode.NotFound);
            probes++;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Check(body.RootElement.GetProperty("max_tokens").GetInt32() == 1 && body.RootElement.GetProperty("stream").GetBoolean() == false,
                "Fallback probe has bounded nonstreaming generation");
            var messages = body.RootElement.GetProperty("messages");
            Check(messages.GetArrayLength() == 1 && messages[0].GetProperty("content").GetString() == "Reply OK.",
                "Connection fallback sends only fixed neutral text");
            return Response(malformed ? "{\"choices\":[]}" : "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"OK\"}}]}");
        }));
        var model = new ModelSettings(ModelProvider.RemoteApi, "https://example.com/v1/chat/completions", "public-model");
        await client.CheckChatModelAsync(model, default, allowNeutralProbe: true);
        Check(probes == 0, "Available metadata never needs a generation probe");
        metadata = false;
        await Reject(() => client.CheckChatModelAsync(model, default), "ConnectionCheckUnavailable");
        Check(probes == 0, "Metadata-only callers never generate a reply");
        await client.CheckChatModelAsync(model, default, allowNeutralProbe: true);
        Check(probes == 1, "Explicit Connect supports anonymous services without model metadata");
        malformed = true;
        await Reject(() => client.CheckChatModelAsync(model, default, allowNeutralProbe: true), "ConnectionCheckUnavailable");
        reject = true;
        await Reject(() => client.CheckChatModelAsync(model, default, allowNeutralProbe: true), "AuthenticationRejected");
        Check(probes == 2, "Authentication failures never trigger another endpoint or bypass");
    }
    private static async Task Reject(Func<Task> action, string key)
    {
        try { await action(); }
        catch (SafeOperationException e) when (e.MessageKey == key && !e.Message.Contains("echoed-secret")) { return; }
        throw new Exception("Expected safe connection rejection: " + key);
    }
    private static HttpResponseMessage Response(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
