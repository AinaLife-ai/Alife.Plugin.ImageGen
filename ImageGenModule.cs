using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;
using Microsoft.Extensions.Logging;

namespace Alife.Plugin.ImageGen;

[Module(
    "AI 图片生成",
    "AI 图片生成功能，支持自定义接口地址、API Key、模型和尺寸。兼容 OpenAI 格式接口及 Stable Diffusion API。",
    EditorUI = typeof(ImageGenModuleUI)
)]
public class ImageGenModule(
    XmlFunctionCaller functionService,
    ILogger<ImageGenModule> logger,
    IHttpClientFactory httpClientFactory
) : InteractiveModule, IConfigurable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public ImageGenConfig? Configuration { get; set; }

    [XmlFunction(FunctionMode.Pair)]
    [Description("生成图片 - 根据提示词生成一张AI图片")]
    public async Task GenerateImage(
        [Description("图片描述提示词（英文更佳）")] string prompt,
        [Description("图片宽度（可选，默认使用管理界面设置）")] int? width = null,
        [Description("图片高度（可选，默认使用管理界面设置）")] int? height = null,
        [Description("生成数量，默认1，最大4")] int? n = null
    )
    {
        if (Configuration == null)
        {
            Error("插件未配置，请先在管理界面设置 API 参数");
            return;
        }

        if (string.IsNullOrWhiteSpace(Configuration.ApiKey))
        {
            Error("API Key 未配置，请先在管理界面填写");
            return;
        }

        if (string.IsNullOrWhiteSpace(prompt))
        {
            Error("提示词不能为空");
            return;
        }

        var httpClient = httpClientFactory.CreateClient("ImageGen");
        httpClient.Timeout = TimeSpan.FromSeconds(120);
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Configuration.ApiKey);

        var w = width ?? Configuration.DefaultWidth;
        var h = height ?? Configuration.DefaultHeight;
        var count = Math.Clamp(n ?? 1, 1, 4);

        // 构建请求体 — 兼容 OpenAI Image API / SD API
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
            logger.LogInformation("正在请求图片生成 API: {Endpoint}, prompt={Prompt}, size={W}x{H}",
                Configuration.ApiEndpoint, prompt, w, h);

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(requestBody, JsonOptions),
                Encoding.UTF8,
                "application/json"
            );

            var response = await httpClient.PostAsync(Configuration.ApiEndpoint, jsonContent);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Error($"API 请求失败 ({response.StatusCode}): {responseBody}");
                return;
            }

            var result = JsonSerializer.Deserialize<ImageGenResult>(responseBody, JsonOptions);
            if (result?.Data == null || result.Data.Count == 0)
            {
                Error("API 返回了空的图片数据");
                return;
            }

            var urls = result.Data
                .Where(d => !string.IsNullOrEmpty(d.Url))
                .Select(d => d.Url!)
                .ToList();

            if (urls.Count == 0)
            {
                Error("API 返回的图片 URL 为空");
                return;
            }

            // 下载图片并发送给 AI 上下文
            var imageUrls = new List<string>();
            foreach (var url in urls)
            {
                try
                {
                    var imgBytes = await httpClient.GetByteArrayAsync(url);
                    // 保存到临时目录供显示
                    var fileName = $"imagegen_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N}.png";
                    var savePath = Path.Combine(Path.GetTempPath(), "Alife_ImageGen", fileName);
                    Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
                    await File.WriteAllBytesAsync(savePath, imgBytes);
                    imageUrls.Add(url);

                    logger.LogInformation("图片已保存: {Path}", savePath);
                }
                catch (Exception ex)
                {
                    logger.LogWarning("下载图片失败 {Url}: {Msg}", url, ex.Message);
                }
            }

            if (imageUrls.Count > 0)
            {
                var revised = result.Data.FirstOrDefault()?.RevisedPrompt;
                var msg = $"\u2705 已生成 {imageUrls.Count} 张图片";
                if (!string.IsNullOrEmpty(revised))
                    msg += $"\n> {revised}";
                msg += $"\n{string.Join("\n", imageUrls.Select((u, i) => $"[图片{i + 1}]({u})"))}";

                Poke(msg);
            }
            else
            {
                Error("图片生成成功但下载失败，请检查网络或更换接口");
            }
        }
        catch (TaskCanceledException)
        {
            Error("图片生成请求超时，可能是接口响应较慢，请稍后重试");
        }
        catch (HttpRequestException ex)
        {
            Error($"网络请求失败: {ex.Message}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "图片生成失败");
            Error($"图片生成失败: {ex.Message}");
        }
    }

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);

        var xmlHandler = new XmlHandler(this);
        functionService.RegisterHandlerWithoutDocument(xmlHandler);

        Prompt($"""
        此服务支持 AI 图片生成功能。
        你可以让我根据描述生成图片。

        ## 提供工具
        {xmlHandler.FunctionDocument()}
        """);
    }
}