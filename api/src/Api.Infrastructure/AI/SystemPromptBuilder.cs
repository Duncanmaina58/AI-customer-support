using System.Text;
using Api.Application.Abstractions;
using Api.Domain.Entities;
using Api.Domain.Enums;

namespace Api.Infrastructure.AI;

/// <summary>
/// Builds the system-prompt string passed as the first message in every GPT-4o
/// chat completion request. The prompt grounds the AI in:
///   (a) the company's brand voice and language
///   (b) the knowledge base chunks retrieved by RagService for the current query
///   (c) Sprint 9: the company's registered actions, plus the &lt;intent&gt;
///       classification block every response must end with
///
/// A deliberately grounding approach:
///   - instructs the model to ONLY answer from provided context
///   - instructs it to say it doesn't know (and offer a human agent) if the
///     answer isn't in the context — preventing hallucination
///   - handles the "no knowledge base yet" case gracefully so Sprint 4 works
///     on a fresh deployment where no documents have been uploaded
/// </summary>
public class SystemPromptBuilder
{
    /// <summary>
    /// Builds and returns the complete system prompt.
    /// Pure function — no DB access, no async, safe to call on any thread.
    /// </summary>
    public string Build(Company company, IReadOnlyList<string> knowledgeChunks, IReadOnlyList<AvailableAction>? availableActions = null)
    {
        availableActions ??= [];

        var tone = company.BrandVoice switch
        {
            BrandVoice.Formal    => "formal and professional",
            BrandVoice.Friendly  => "warm, friendly, and approachable",
            _                    => "clear and helpful",
        };

        var languageName = company.PrimaryLanguage switch
        {
            "sw" => "Swahili",
            "en" => "English",
            "fr" => "French",
            "de" => "German",
            "ar" => "Arabic",
            var code => code,   // fall back to the raw BCP-47 tag
        };

        var sb = new StringBuilder();

        sb.AppendLine($"You are a {tone} AI customer support assistant for {company.Name}.");
        sb.AppendLine($"Always respond in {languageName}. Keep replies concise and accurate — no more than 3–4 short paragraphs.");
        sb.AppendLine();
        sb.AppendLine("RULES:");
        sb.AppendLine("1. Answer ONLY from the knowledge base context provided below.");
        sb.AppendLine("2. If the answer is not in the context, say you don't have that information yet and invite the customer to reply with the word \"agent\" to be connected to a human support agent.");
        sb.AppendLine("3. Never invent, guess, or extrapolate facts beyond what is explicitly stated in the context.");
        sb.AppendLine("4. If a question is off-topic (not related to the company's products or services), politely redirect.");

        if (knowledgeChunks.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("=== KNOWLEDGE BASE CONTEXT ===");
            for (int i = 0; i < knowledgeChunks.Count; i++)
            {
                sb.AppendLine();
                sb.AppendLine($"[Source {i + 1}]");
                sb.AppendLine(knowledgeChunks[i]);
            }
            sb.AppendLine();
            sb.AppendLine("=== END CONTEXT ===");
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine("NOTE: No knowledge base documents have been uploaded yet for this company.");
            sb.AppendLine("Until the knowledge base is populated, let the customer know you can't answer their question yet and suggest they contact the team directly or reply with \"agent\" for human support.");
        }

        AppendActionInstructions(sb, availableActions);

        return sb.ToString();
    }

    /// <summary>
    /// Sprint 9 Action Engine: teaches the model (a) what it's allowed to DO,
    /// not just answer, and (b) the exact trailing JSON block every response
    /// must end with so GroqChatProvider can parse a DetectedIntent back out.
    /// Always appended, even with zero registered actions — the intent block
    /// still classifies question vs. escalate, and keeping the response shape
    /// constant means the parser has exactly one code path, not two.
    /// </summary>
    private static void AppendActionInstructions(StringBuilder sb, IReadOnlyList<AvailableAction> availableActions)
    {
        sb.AppendLine();
        sb.AppendLine("=== ACTIONS ===");

        if (availableActions.Count > 0)
        {
            sb.AppendLine("In addition to answering questions, you can perform the following actions on the customer's behalf when they clearly ask for one of them:");
            foreach (var action in availableActions)
            {
                sb.Append($"- action_type \"{action.ActionType}\": {action.DisplayName}");
                if (!string.IsNullOrWhiteSpace(action.ParameterSchema))
                    sb.Append($" (parameters: {action.ParameterSchema})");
                sb.AppendLine();
            }
            sb.AppendLine("Only use an action_type from this exact list. Extract every parameter you can find directly from the customer's message — never invent a value (like an order number) that wasn't actually given; if a required parameter is missing, ask the customer for it in your reply instead of guessing, and classify this turn as \"question\", not \"action\", until you have what you need.");
        }
        else
        {
            sb.AppendLine("You have no actions available to perform for this company right now — treat every message as a question or, if the customer clearly needs a human, as an escalation.");
        }

        sb.AppendLine();
        sb.AppendLine("After your reply to the customer, on a new line, you MUST append exactly one classification block in this exact format (the customer will never see this — it is stripped out before your reply is shown to them):");
        sb.AppendLine("<intent>{\"type\":\"question|action|escalate\",\"action_type\":\"<action_type or null>\",\"parameters\":{},\"confidence\":0.0-1.0}</intent>");
        sb.AppendLine("- type \"action\" only when the customer clearly wants one of the actions listed above performed, AND you have every required parameter.");
        sb.AppendLine("- type \"escalate\" when the request needs a human regardless of actions (angry customer, legal/medical/financial risk, or anything outside your scope).");
        sb.AppendLine("- type \"question\" for everything else, including when an action was requested but a parameter is still missing.");
        sb.AppendLine("- parameters must be a flat JSON object of string values only, extracted from the customer's own words. Use {} (empty) when type is not \"action\".");
    }
}
