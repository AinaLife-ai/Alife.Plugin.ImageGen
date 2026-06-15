using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;
using Microsoft.Extensions.Logging;

namespace Alife.Plugin.ImageGen;

[Module(
    "AI 图片生成",
    "AI 图片生成功能，支持自定义接口地址、API Key、模型和尺寸。兼容 OpenAI 格式接口及 Stable Diffusion API。"
)]
public class ImageGenModule(
    XmlFunctionCaller functionService,
    ILogger<ImageGenModule> logger
) : InteractiveModule<ImageGenModule>, IConfigurable<ImageGenConfig>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public ImageGenConfig? Configuration { get; set; }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("配置图片生成参数 - 设置 API 接口地址、Key、模型和默认尺寸")]
    public Task SetConfig(
        [Description("API 接口地址，如 https://api.openai.com/v1/images/generations")] string? endpoint = null,
        [Description("API Key")] string? apiKey = null,
        [Description("模型名称，如 dall-e-3、stable-diffusion-xl-1024-v1-0")] string? model = null,
        [Description("默认宽度，如 1024")] int? width = null,
        [Description("默认高度，如 1024")] int? height = null
    )
    {
        Configuration ??= new ImageGenConfig();
        if (!string.IsNullOrEmpty(endpoint)) Configuration.ApiEndpoint = endpoint;
        if (!string.IsNullOrEmpty(apiKey)) Configuration.ApiKey = apiKey;
        if (!string.IsNullOrEmpty(model)) Configuration.Model = model;
        if (width.HasValue) Configuration.DefaultWidth = width.Value;
        if (height.HasValue) Configuration.DefaultHeight = height.Value;

        var msg = $"配置已更新
  接口：{Configuration.ApiEndpoint}
  Key：{(string.IsNullOrEmpty(Configuration.ApiKey) ? "未设置" : "已设置")}
  模型：{Configuration.Model}
  尺寸：{Configuration.DefaultWidth}x{Configuration.DefaultHeight}";
        Poke(msg);
        return Task.CompletedTask;
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("生成图片 - 根据提示词生成 AI 图片，需要先通过 SetConfig 配置接口参数")]
    public async Task GenerateImage(
        [Description("图片描述提示词，英文更佳")] string prompt,
        [Description("图片宽度，默认使用配置中的尺寸")] int? width = null,
        [Description("图片高度，默认使用配置中的尺寸")] int? height = null,
        [Description("生成数量，默认 1，最大 4")] int? n = null
    )
    {
        if (Configuration == null || string.IsNullOrWhiteSpace(Configuration.ApiKey))
        {
            Poke("请先通过 SetConfig 配置 API 参数");
            return;
        }

        if (string.IsNullOrWhiteSpace(prompt))
        {
            Poke("提示词不能为空");
            return;
        }

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Configuration.ApiKey);

        var w = width ?? Configuration.DefaultWidth;
        var h = height ?? Configuration.DefaultHeight;
        var count = Math.Clamp(n ?? 1, 1, 4);

        var requestBody = new Dictionary<string, object>
        {
            ["prompt"] = prompt,
            ["n"] = count,
            ["size"] = $"{w}x{h}",
            ["model"] = Configuration.Model,
            ["response_format"] = "url",
        };

        try
        {
            logger.LogInformation("请求图片生成: {Endpoint} | {Prompt} | {W}x{H}",
                Configuration.ApiEndpoint, prompt, w, h);

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(requestBody, JsonOptions),
                Encoding.UTF8,
                "application/json"
            );

            var response = await httpClient.PostAsync(Configuration.ApiEndpoint, jsonContent);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Poke($"API 请求失败 ({response.StatusCode}): {body}");
                return;
            }

            var result = JsonSerializer.Deserialize<ImageGenResult>(body, JsonOptions);
            if (result?.Data == null || result.Data.Count == 0)
            {
                Poke("API 返回了空的图片数据");
                return;
            }

            var urls = result.Data
                .Where(d => !string.IsNullOrEmpty(d.Url))
                .Select(d => d.Url!)
                .ToList();

            if (urls.Count == 0)
            {
                Poke("API 返回的图片 URL 为空");
                return;
            }

            var msg = $"✅ 已生成 {urls.Count} 张图片";
            var revised = result.Data.FirstOrDefault()?.RevisedPrompt;
            if (!string.IsNullOrEmpty(revised))
                msg += $"
> 优化提示词: {revised}";
            msg += $"
{string.Join("
", urls.Select((u, i) => $"[图片{i + 1}]({u})"))}";

            Poke(msg);
        }
        catch (TaskCanceledException)
        {
            Poke("请求超时，接口响应较慢，请稍后重试或检查接口地址");
        }
        catch (HttpRequestException ex)
        {
            Poke($"网络请求失败: {ex.Message}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "图片生成异常");
            Poke($"生成失败: {ex.Message}");
        }
    }

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);

        var xmlHandler = new XmlHandler(this);
        functionService.RegisterHandlerWithoutDocument(xmlHandler);

        var doc = xmlHandler.FunctionDocument();
        Prompt(
            "此服务支持 AI 图片生成功能。
" +
            "你可以让我根据描述生成图片，也可以让我帮你配置 API 参数。

" +
            "## 提供工具
" + doc
        );
    }
}

public class ImageGenConfig
{
    public string ApiEndpoint { get; set; } = "https://api.openai.com/v1/images/generations";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "dall-e-3";
    public int DefaultWidth { get; set; } = 1024;
    public int DefaultHeight { get; set; } = 1024;
}

public class ImageGenResult
{
    public long Created { get; set; }
    public List<ImageData> Data { get; set; } = new();
}

public class ImageData
{
    public string? Url { get; set; }
    public string? B64Json { get; set; }
    public string? RevisedPrompt { get; set; }
}
