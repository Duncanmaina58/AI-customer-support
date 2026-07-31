using Api.Domain.Entities;
using Api.Domain.Enums;
using Api.Infrastructure.AI;

namespace Api.Tests;

/// <summary>
/// Unit tests for Sprint 4 components that don't need a real DB or API key.
///
/// GroqChatProvider, CohereEmbeddingProvider, and RagService are NOT unit-tested
/// here because they make real HTTP calls — those are covered by integration tests
/// (not yet wired) that run against a local PostgreSQL + real or stubbed API endpoints.
/// For CI, the canary is: if the build compiles and ConversationServiceTests pass,
/// the tenant-isolation guarantee that Sprint 4 builds on top of is intact.
/// </summary>
public class Sprint4Tests
{
    private static Company MakeCompany(BrandVoice voice = BrandVoice.Friendly, string lang = "en") => new()
    {
        Id              = Guid.NewGuid(),
        Name            = "Acme Hardware",
        PublicApiKey    = "pub_test",
        BrandVoice      = voice,
        PrimaryLanguage = lang,
    };

    // -------------------------------------------------------------------------
    // SystemPromptBuilder
    // -------------------------------------------------------------------------

    [Fact]
    public void Build_with_chunks_includes_knowledge_base_header_and_all_chunk_texts()
    {
        var builder = new SystemPromptBuilder();
        var company = MakeCompany();
        var chunks  = new[] { "[FAQ]\nRefunds are processed in 5 days.", "[Policy]\nNo returns after 30 days." };

        var prompt = builder.Build(company, chunks);

        Assert.Contains("KNOWLEDGE BASE CONTEXT", prompt);
        Assert.Contains("Refunds are processed in 5 days.", prompt);
        Assert.Contains("No returns after 30 days.", prompt);
        Assert.Contains("[Source 1]", prompt);
        Assert.Contains("[Source 2]", prompt);
    }

    [Fact]
    public void Build_without_chunks_includes_no_knowledge_base_notice()
    {
        var builder = new SystemPromptBuilder();
        var company = MakeCompany();

        var prompt = builder.Build(company, []);

        Assert.Contains("No knowledge base documents", prompt);
        // Should NOT have a context section
        Assert.DoesNotContain("KNOWLEDGE BASE CONTEXT", prompt);
    }

    [Fact]
    public void Build_uses_company_name_in_prompt()
    {
        var builder = new SystemPromptBuilder();
        var company = MakeCompany();
        company.Name = "Savannah Safaris Ltd";

        var prompt = builder.Build(company, []);

        Assert.Contains("Savannah Safaris Ltd", prompt);
    }

    [Fact]
    public void Build_formal_voice_uses_formal_tone_phrase()
    {
        var builder = new SystemPromptBuilder();
        var company = MakeCompany(voice: BrandVoice.Formal);

        var prompt = builder.Build(company, []);

        Assert.Contains("formal and professional", prompt);
        Assert.DoesNotContain("warm, friendly", prompt);
    }

    [Fact]
    public void Build_friendly_voice_uses_friendly_tone_phrase()
    {
        var builder = new SystemPromptBuilder();
        var company = MakeCompany(voice: BrandVoice.Friendly);

        var prompt = builder.Build(company, []);

        Assert.Contains("warm, friendly", prompt);
    }

    [Fact]
    public void Build_swahili_language_code_expands_to_Swahili()
    {
        var builder = new SystemPromptBuilder();
        var company = MakeCompany(lang: "sw");

        var prompt = builder.Build(company, []);

        Assert.Contains("Swahili", prompt);
        Assert.DoesNotContain(" sw ", prompt); // raw code should not appear
    }

    [Fact]
    public void Build_always_includes_no_hallucination_rules()
    {
        var builder = new SystemPromptBuilder();
        var company = MakeCompany();

        var prompt = builder.Build(company, []);

        // Rule 1: context-only answers
        Assert.Contains("ONLY", prompt);
        // Rule 3: no invented facts
        Assert.Contains("Never invent", prompt);
    }

    // -------------------------------------------------------------------------
    // PlaceholderAiProvider backward-compatibility
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PlaceholderAiProvider_returns_result_with_zero_tokens_used()
    {
        var provider = new PlaceholderAiProvider(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PlaceholderAiProvider>.Instance);

        var request = new Api.Application.Abstractions.AiReplyRequest(
            CompanyId:                Guid.NewGuid(),
            ConversationId:           Guid.NewGuid(),
            CustomerMessage:          "Hello",
            RecentHistory:            [],
            RetrievedKnowledgeChunks: []);

        var result = await provider.GenerateReplyAsync(request);

        Assert.NotEmpty(result.ReplyText);
        Assert.Equal(0, result.TokensUsed);   // placeholder doesn't consume real tokens
        Assert.Equal("placeholder-v1-sprint3", result.ModelUsed);
    }

    [Fact]
    public async Task PlaceholderAiProvider_result_includes_pipeline_confirmation_text()
    {
        var provider = new PlaceholderAiProvider(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PlaceholderAiProvider>.Instance);

        var request = new Api.Application.Abstractions.AiReplyRequest(
            CompanyId:                Guid.NewGuid(),
            ConversationId:           Guid.NewGuid(),
            CustomerMessage:          "Test",
            RecentHistory:            [],
            RetrievedKnowledgeChunks: []);

        var result = await provider.GenerateReplyAsync(request);

        // Placeholder should still confirm pipeline is working (Sprint 3 design)
        Assert.Contains("pipeline", result.ReplyText, StringComparison.OrdinalIgnoreCase);
    }
}
