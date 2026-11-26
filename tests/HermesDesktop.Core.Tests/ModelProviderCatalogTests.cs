using System.Text.Json;
using HermesDesktop.Core.Models;
using HermesDesktop.Core.Rest;
using HermesDesktop.Core.Rpc;
using Xunit;

namespace HermesDesktop.Core.Tests;

public class ModelProviderCatalogTests
{
    [Fact]
    public void DecodeModelProviders_HandlesStringAndObjectModelEntries()
    {
        using var doc = JsonDocument.Parse("""
        {
          "providers": [
            {
              "slug": "anthropic",
              "name": "Anthropic",
              "is_current": true,
              "aliases": ["claude"],
              "default_model": "claude-sonnet-4-5",
              "models": [
                "claude-sonnet-4-5",
                { "id": "claude-opus-4-1" },
                { "model": "claude-haiku-4-5" },
                { "name": "claude-3-7-sonnet" }
              ]
            },
            {
              "id": "custom:my-vllm",
              "name": "Local vLLM",
              "models": [
                { "model": "qwen-2.5-72b" }
              ]
            }
          ]
        }
        """);

        var providers = HermesWsClient.DecodeModelProviders(doc.RootElement);
        Assert.Equal(2, providers.Count);

        var anthropic = providers[0];
        Assert.Equal("anthropic", anthropic.Slug);
        Assert.Equal("Anthropic", anthropic.DisplayName);
        Assert.True(anthropic.IsCurrent);
        Assert.Contains("claude", anthropic.Aliases);
        Assert.Equal(["claude-sonnet-4-5", "claude-opus-4-1", "claude-haiku-4-5", "claude-3-7-sonnet"], anthropic.Models);

        var vllm = providers[1];
        Assert.Equal("custom:my-vllm", vllm.Slug);
        Assert.Equal("Local vLLM", vllm.DisplayName);
        Assert.Equal(["qwen-2.5-72b"], vllm.Models);
    }

    [Fact]
    public void DecodeCustomEndpointProviders_PrefixesCustomSlugAndCollectsModels()
    {
        using var doc = JsonDocument.Parse("""
        {
          "endpoints": [
            {
              "id": "custom",
              "name": "Custom",
              "base_url": "https://api.example.com/v1",
              "model": "gpt-4o-mini"
            },
            {
              "id": "deepseek-proxy",
              "name": "DeepSeek Enterprise",
              "base_url": "https://api.deepseek.internal/v1",
              "model": "deepseek-chat",
              "models": ["deepseek-chat", { "id": "deepseek-reasoner" }],
              "is_current": true
            },
            {
              "endpoint_id": "custom:local-ollama",
              "name": "Ollama",
              "base_url": "http://127.0.0.1:11434/v1",
              "model": "qwen3:32b"
            }
          ]
        }
        """);

        var endpoints = HermesRestClient.DecodeCustomEndpointProviders(doc.RootElement);
        Assert.Equal(3, endpoints.Count);

        Assert.Equal("custom", endpoints[0].Slug);
        Assert.Contains("custom:custom", endpoints[0].Aliases);
        Assert.Equal(["gpt-4o-mini"], endpoints[0].Models);

        Assert.Equal("custom:deepseek-proxy", endpoints[1].Slug);
        Assert.Equal("DeepSeek Enterprise", endpoints[1].DisplayName);
        Assert.True(endpoints[1].IsCurrent);
        Assert.Contains("deepseek-proxy", endpoints[1].Aliases);
        Assert.Equal(["deepseek-chat", "deepseek-reasoner"], endpoints[1].Models);

        Assert.Equal("custom:local-ollama", endpoints[2].Slug);
        Assert.Equal("Ollama", endpoints[2].DisplayName);
        Assert.Equal(["qwen3:32b"], endpoints[2].Models);
    }

    [Fact]
    public void DecodeEnvProviders_IncludesUnconfiguredPublicProvidersWithEnvKeyNames()
    {
        using var envDoc = JsonDocument.Parse("""
        {
          "OPENROUTER_API_KEY": {
            "category": "provider",
            "is_set": false,
            "is_password": true,
            "provider": "openrouter",
            "provider_label": "OpenRouter"
          },
          "GEMINI_API_KEY": {
            "category": "provider",
            "is_set": false,
            "is_password": true,
            "provider": "gemini",
            "provider_label": "Google Gemini"
          }
        }
        """);

        var allEnv = HermesRestClient.DecodeEnvProviders(envDoc.RootElement, includeUnconfigured: true);
        Assert.Equal(2, allEnv.Count);
        Assert.Contains(allEnv, p => p.Slug == "openrouter" && p.DisplayName == "OpenRouter" && !p.IsConfigured && p.EnvVarName == "OPENROUTER_API_KEY");
        Assert.Contains(allEnv, p => p.Slug == "gemini" && p.DisplayName == "Google Gemini" && !p.IsConfigured && p.EnvVarName == "GEMINI_API_KEY");
    }

    [Fact]
    public void MergeModelProviders_UnifiesBareCustomWithCustomEndpointAndSortsConfiguredFirst()
    {
        var modelOptions = new[]
        {
            new ModelProviderOption("custom", "Custom endpoint", ["custom"], true, ["custom-model-1"], IsConfigured: true),
            new ModelProviderOption("openrouter", "OpenRouter", ["openrouter"], false, ["openai/gpt-4o"], IsConfigured: false)
        };
        var endpoints = new[]
        {
            new ModelProviderOption("custom", "Custom", ["custom", "custom:custom"], true, ["custom-model-1"], IsConfigured: true, SourceTag: "custom")
        };

        var merged = HermesWsClient.MergeModelProviders(modelOptions, endpoints);
        Assert.Equal(2, merged.Count);
        Assert.Equal("custom", merged[0].Slug);
        Assert.True(merged[0].IsCurrent);
        Assert.Equal("openrouter", merged[1].Slug);
        Assert.False(merged[1].IsConfigured);
    }

    [Fact]
    public void DecodeOAuthAndConfiguredEnvProviders_OnlyIncludeConnectedEntries()
    {
        using var oauthDoc = JsonDocument.Parse("""
        {
          "providers": [
            { "id": "openai-codex", "name": "OpenAI Codex", "status": { "logged_in": true } },
            { "id": "anthropic-oauth", "name": "Claude OAuth", "status": { "logged_in": false } }
          ]
        }
        """);

        var oauth = HermesRestClient.DecodeOAuthProviders(oauthDoc.RootElement);
        Assert.Single(oauth);
        Assert.Equal("openai-codex", oauth[0].Slug);
        Assert.Equal("OpenAI Codex", oauth[0].DisplayName);

        using var envDoc = JsonDocument.Parse("""
        {
          "OPENAI_API_KEY": {
            "category": "provider",
            "is_set": true,
            "is_password": true,
            "provider_profiles": [
              { "provider": "openai", "provider_label": "OpenAI", "primary": true },
              { "provider": "azure-compat", "provider_label": "Azure Compat", "primary": false }
            ]
          },
          "GROQ_API_KEY": {
            "category": "provider",
            "is_set": false,
            "is_password": true,
            "provider": "groq",
            "provider_label": "Groq"
          },
          "OPENROUTER_API_KEY": {
            "category": "provider",
            "is_set": true,
            "is_password": true,
            "provider": "openrouter",
            "provider_label": "OpenRouter"
          }
        }
        """);

        var envProviders = HermesRestClient.DecodeConfiguredEnvProviders(envDoc.RootElement);
        Assert.Equal(2, envProviders.Count);
        Assert.Contains(envProviders, p => p.Slug == "openai" && p.DisplayName == "OpenAI");
        Assert.Contains(envProviders, p => p.Slug == "openrouter" && p.DisplayName == "OpenRouter");
        Assert.DoesNotContain(envProviders, p => p.Slug == "groq");
        Assert.DoesNotContain(envProviders, p => p.Slug == "azure-compat");
    }

    [Fact]
    public void MergeModelProviders_CombinesSourcesWithoutCollidingBuiltInAndCustom()
    {
        var wsOptions = new[]
        {
            new ModelProviderOption("openai", "OpenAI", ["openai"], false, ["gpt-4o", "gpt-4.1"]),
            new ModelProviderOption("custom:deepseek", "custom:deepseek", ["deepseek"], false, ["deepseek-chat"])
        };
        var restEndpoints = new[]
        {
            new ModelProviderOption("custom:deepseek", "DeepSeek Enterprise", ["custom:deepseek", "deepseek"], true, ["deepseek-chat", "deepseek-reasoner"]),
            new ModelProviderOption("custom:openai", "Corporate OpenAI Proxy", ["custom:openai", "openai"], false, ["gpt-4o-corp"])
        };
        var restEnv = new[]
        {
            new ModelProviderOption("openai", "OpenAI", ["openai"], false, []),
            new ModelProviderOption("zai", "Z.AI", ["zai"], false, [])
        };

        var merged = HermesWsClient.MergeModelProviders(wsOptions, restEndpoints, restEnv);
        Assert.Equal(4, merged.Count);

        var openai = merged.Single(p => p.Slug == "openai");
        Assert.Equal(["gpt-4o", "gpt-4.1"], openai.Models);

        var deepseek = merged.Single(p => p.Slug == "custom:deepseek");
        Assert.Equal("DeepSeek Enterprise", deepseek.DisplayName);
        Assert.True(deepseek.IsCurrent);
        Assert.Equal(["deepseek-chat", "deepseek-reasoner"], deepseek.Models);

        var customOpenAi = merged.Single(p => p.Slug == "custom:openai");
        Assert.Equal("Corporate OpenAI Proxy", customOpenAi.DisplayName);
        Assert.Equal(["gpt-4o-corp"], customOpenAi.Models);

        var zai = merged.Single(p => p.Slug == "zai");
        Assert.Equal("Z.AI", zai.DisplayName);
    }
}
