using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
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
using Alife.Platform;
using Microsoft.Extensions.Logging;

namespace Alife.Plugin.ImageGen;

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
    public List<ImageData> Data { get; set; } = [];
}

public class ImageData
{
    public string? Url { get; set; }
    public string? B64Json { get; set; }
    public string? RevisedPrompt { get; set; }
}

[Module(
    "AI 图片生成",
    "AI 图片生成功能，支持自定义 API 接口地址、Key、模型和尺寸。兼容 OpenAI 格式接口及 Stable Diffusion API。",
    defaultCategory: "用户自制/图片生成"
)]
public class ImageGenModule(
    XmlFunctionCaller functionService,
    ILogger<ImageGenModule> logger
) : InteractiveModule<ImageGenModule>, IConfigurable<ImageGenConfig>
{
    private static readonly JsonSerializerOptions JsonOpt = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public ImageGenConfig? Configuration { get; set; } = new();

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);

        if (Configuration == null)
            LoadPersistedConfig();

        var handler = new XmlHandler(this);
        functionService.RegisterHandlerWithoutDocument(handler);

        Prompt($$"""
        此服务支持 AI 图片生成功能。你可以让我根据描述生成图片，也可以让我帮你配置 API 参数。

        ## 提供工具
        {{handler.FunctionDocument()}}
        """);
    }

    private void PersistConfig()
    {
        if (Configuration == null) return;
        try
        {
            var key = Path.Combine(
                Character.StorageKey,
                "Configuration",
                GetType().FullName!
            );
            var path = Path.Combine(AlifePath.StorageFolderPath, $"{key}.json");
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            var json = System.Text.Json.JsonSerializer.Serialize(Configuration, new JsonSerializerOptions
            {
                WriteIndented = true,
            });
            File.WriteAllText(path, json);
            logger.LogInformation("配置已持久化: {Path}", path);
        }
        catch (Exception ex)
        {
            logger.LogWarning("配置持久化失败: {Msg}", ex.Message);
        }
    }

    private void LoadPersistedConfig()
    {
        try
        {
            var key = Path.Combine(
                Character.StorageKey,
                "Configuration",
                GetType().FullName!
            );
            var path = Path.Combine(AlifePath.StorageFolderPath, $"{key}.json");
            if (!File.Exists(path)) return;
            var json = File.ReadAllText(path);
            var loaded = System.Text.Json.JsonSerializer.Deserialize<ImageGenConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            if (loaded != null) Configuration = loaded;
        }
        catch (Exception ex)
        {
            logger.LogWarning("加载已持久化配置失败: {Msg}", ex.Message);
        }
    }

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

        PersistConfig();

        Poke($"""
        配置已更新：
          接口：{Configuration.ApiEndpoint}
          Key：{(string.IsNullOrEmpty(Configuration.ApiKey) ? "未设置" : "已设置")}
          模型：{Configuration.Model}
          尺寸：{Configuration.DefaultWidth}x{Configuration.DefaultHeight}
        """);
        return Task.CompletedTask;
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("生成图片 - 根据提示词生成 AI 图片，需先通过 SetConfig 配置接口参数")]
    public async Task GenerateImage(
        [Description("图片描述提示词，英文更佳")] string prompt,
        [Description("图片宽度，默认使用配置中的尺寸")] int? width = null,
        [Description("图片高度，默认使用配置中的尺寸")] int? height = null,
        [Description("生成数量，默认 1，最大 4")] int? n = null
    )
    {
        if (Configuration == null || string.IsNullOrWhiteSpace(Configuration.ApiKey))
        {
            logger.LogError("API未配置");
            Poke("请先通过 SetConfig 配置 API 参数");
            return;
        }

        if (string.IsNullOrWhiteSpace(prompt))
        {
            logger.LogError("提示词为空");
            Poke("提示词不能为空");
            return;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Configuration.ApiKey);

        var w = width ?? Configuration.DefaultWidth;
        var h = height ?? Configuration.DefaultHeight;
        var count = Math.Clamp(n ?? 1, 1, 4);

        var body = new Dictionary<string, object>
        {
            ["prompt"] = prompt,
            ["n"] = count,
            ["size"] = $"{w}x{h}",
            ["model"] = Configuration.Model,
            ["response_format"] = "url",
        };

        try
        {
            logger.LogInformation("生成图片: {Endpoint} | {Prompt} | {W}x{H}",
                Configuration.ApiEndpoint, prompt, w, h);

            var resp = await http.PostAsync(Configuration.ApiEndpoint,
                new StringContent(JsonSerializer.Serialize(body, JsonOpt),
                    Encoding.UTF8, "application/json"));

            var raw = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                logger.LogError("API请求失败: {StatusCode}", resp.StatusCode);
                Poke($"API 请求失败 ({resp.StatusCode})");
                return;
            }

            var result = JsonSerializer.Deserialize<ImageGenResult>(raw, JsonOpt);
            if (result?.Data == null || result.Data.Count == 0)
            {
                logger.LogError("API返回为空");
                Poke("API 返回为空");
                return;
            }

            var urls = result.Data
                .Where(d => !string.IsNullOrEmpty(d.Url))
                .Select(d => d.Url!)
                .ToList();

            if (urls.Count == 0)
            {
                logger.LogError("图片URL为空");
                Poke("图片 URL 为空");
                return;
            }

            var saved = new List<string>();
            foreach (var url in urls)
            {
                try
                {
                    var img = await http.GetByteArrayAsync(url);
                    var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                        "Data", "ImageGen");
                    Directory.CreateDirectory(dir);
                    var file = Path.Combine(dir,
                        $"img_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N}.png");
                    await File.WriteAllBytesAsync(file, img);
                    saved.Add(file);
                    logger.LogInformation("图片已保存: {Path}", file);
                }
                catch (Exception ex)
                {
                    logger.LogWarning("下载失败: {Msg}", ex.Message);
                }
            }

            if (saved.Count > 0)
            {
                var revised = result.Data.FirstOrDefault()?.RevisedPrompt;
                var msg = $"已生成 {saved.Count} 张图片";
                if (!string.IsNullOrEmpty(revised))
                    msg += $"\n> 优化描述: {revised}";
                msg += $"\n{string.Join("\n", saved)}";
                Poke(msg);
            }
            else
            {
                logger.LogWarning("图片生成成功但下载失败");
                Poke("图片生成成功但下载失败");
            }
        }
        catch (TaskCanceledException)
        {
            logger.LogError("请求超时");
            Poke("请求超时，请检查接口地址或稍后重试");
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "网络请求失败");
            Poke($"网络请求失败: {ex.Message}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "生成异常");
            Poke($"生成失败: {ex.Message}");
        }
    }
}
