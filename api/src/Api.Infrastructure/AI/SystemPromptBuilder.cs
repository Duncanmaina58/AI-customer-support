using System.Text;
using Api.Domain.Entities;
using Api.Domain.Enums;

namespace Api.Infrastructure.AI;

/// <summary>
/// Builds the system-prompt string passed as the first message in every GPT-4o
/// chat completion request. The prompt grounds the AI in:
///   (a) the company's brand voice and language
///   (b) the knowledge base chunks retrieved by RagService for the current query
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
    public string Build(Company company, IReadOnlyList<string> knowledgeChunks)
    {
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

        return sb.ToString();
    }
}
