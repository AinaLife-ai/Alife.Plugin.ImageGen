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
    "AI \u56fe\u7247\u751f\u6210",
    "AI \u56fe\u7247\u751f\u6210\u529f\u80fd\uff0c\u652f\u6301\u81ea\u5b9a\u4e49\u63a5\u53e3\u5730\u5740\u3001API Key\u3001\u6a21\u578b\u548c\u5c3a\u5bf8\u3002\u517c\u5bb9 OpenAI \u683c\u5f0f\u63a5\u53e3\u53ca Stable Diffusion API\u3002"
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
    [Description("\u914d\u7f6e\u56fe\u7247\u751f\u6210\u53c2\u6570 - \u8bbe\u7f6e API \u63a5\u53e3\u5730\u5740\u3001Key\u3001\u6a21\u578b\u548c\u9ed8\u8ba4\u5c3a\u5bf8")]
    public Task SetConfig(
        [Description("API \u63a5\u53e3\u5730\u5740\uff0c\u5982 https://api.openai.com/v1/images/generations")] string? endpoint = null,
        [Description("API Key")] string? apiKey = null,
        [Description("\u6a21\u578b\u540d\u79f0\uff0c\u5982 dall-e-3\u3001stable-diffusion-xl-1024-v1-0")] string? model = null,
        [Description("\u9ed8\u8ba4\u5bbd\u5ea6\uff0c\u5982 1024")] int? width = null,
        [Description("\u9ed8\u8ba4\u9ad8\u5ea6\uff0c\u5982 1024")] int? height = null
    )
    {
        Configuration ??= new ImageGenConfig();
        if (!string.IsNullOrEmpty(endpoint)) Configuration.ApiEndpoint = endpoint;
        if (!string.IsNullOrEmpty(apiKey)) Configuration.ApiKey = apiKey;
        if (!string.IsNullOrEmpty(model)) Configuration.Model = model;
        if (width.HasValue) Configuration.DefaultWidth = width.Value;
        if (height.HasValue) Configuration.DefaultHeight = height.Value;

        var keyLabel = string.IsNullOrEmpty(Configuration.ApiKey) ? "\u672a\u8bbe\u7f6e" : "\u5df2\u8bbe\u7f6e";
        Poke($"\u914d\u7f6e\u5df2\u66f4\u65b0\n  \u63a5\u53e3\uff1a{Configuration.ApiEndpoint}\n  Key\uff1a{keyLabel}\n  \u6a21\u578b\uff1a{Configuration.Model}\n  \u5c3a\u5bf8\uff1a{Configuration.DefaultWidth}x{Configuration.DefaultHeight}");
        return Task.CompletedTask;
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("\u751f\u6210\u56fe\u7247 - \u6839\u636e\u63d0\u793a\u8bcd\u751f\u6210 AI \u56fe\u7247\uff0c\u9700\u8981\u5148\u901a\u8fc7 SetConfig \u914d\u7f6e\u63a5\u53e3\u53c2\u6570")]
    public async Task GenerateImage(
        [Description("\u56fe\u7247\u63cf\u8ff0\u63d0\u793a\u8bcd\uff0c\u82f1\u6587\u66f4\u4f73")] string prompt,
        [Description("\u56fe\u7247\u5bbd\u5ea6\uff0c\u9ed8\u8ba4\u4f7f\u7528\u914d\u7f6e\u4e2d\u7684\u5c3a\u5bf8")] int? width = null,
        [Description("\u56fe\u7247\u9ad8\u5ea6\uff0c\u9ed8\u8ba4\u4f7f\u7528\u914d\u7f6e\u4e2d\u7684\u5c3a\u5bf8")] int? height = null,
        [Description("\u751f\u6210\u6570\u91cf\uff0c\u9ed8\u8ba4 1\uff0c\u6700\u5927 4")] int? n = null
    )
    {
        if (Configuration == null || string.IsNullOrWhiteSpace(Configuration.ApiKey))
        {
            Poke("\u8bf7\u5148\u901a\u8fc7 SetConfig \u914d\u7f6e API \u53c2\u6570");
            return;
        }

        if (string.IsNullOrWhiteSpace(prompt))
        {
            Poke("\u63d0\u793a\u8bcd\u4e0d\u80fd\u4e3a\u7a7a");
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
            logger.LogInformation("\u8bf7\u6c42\u56fe\u7247\u751f\u6210: {Endpoint} | {Prompt} | {W}x{H}",
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
                Poke($"API \u8bf7\u6c42\u5931\u8d25 ({response.StatusCode}): {body}");
                return;
            }

            var result = JsonSerializer.Deserialize<ImageGenResult>(body, JsonOptions);
            if (result?.Data == null || result.Data.Count == 0)
            {
                Poke("API \u8fd4\u56de\u4e86\u7a7a\u7684\u56fe\u7247\u6570\u636e");
                return;
            }

            var urls = result.Data
                .Where(d => !string.IsNullOrEmpty(d.Url))
                .Select(d => d.Url!)
                .ToList();

            if (urls.Count == 0)
            {
                Poke("API \u8fd4\u56de\u7684\u56fe\u7247 URL \u4e3a\u7a7a");
                return;
            }

            var msg = $"\u2705 \u5df2\u751f\u6210 {urls.Count} \u5f20\u56fe\u7247";
            var revised = result.Data.FirstOrDefault()?.RevisedPrompt;
            if (!string.IsNullOrEmpty(revised))
                msg += $"\n> \u4f18\u5316\u63d0\u793a\u8bcd: {revised}";
            var linkList = string.Join("\n", urls.Select((u, i) => $"\u56fe\u7247{i + 1}: {u}"));
            msg += $"\n{linkList}";

            Poke(msg);
        }
        catch (TaskCanceledException)
        {
            Poke("\u8bf7\u6c42\u8d85\u65f6\uff0c\u63a5\u53e3\u54cd\u5e94\u8f83\u6162\uff0c\u8bf7\u7a0d\u540e\u91cd\u8bd5\u6216\u68c0\u67e5\u63a5\u53e3\u5730\u5740");
        }
        catch (HttpRequestException ex)
        {
            Poke($"\u7f51\u7edc\u8bf7\u6c42\u5931\u8d25: {ex.Message}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "\u56fe\u7247\u751f\u6210\u5f02\u5e38");
            Poke($"\u751f\u6210\u5931\u8d25: {ex.Message}");
        }
    }

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);

        var xmlHandler = new XmlHandler(this);
        functionService.RegisterHandlerWithoutDocument(xmlHandler);

        var doc = xmlHandler.FunctionDocument();
        Prompt(
            "\u6b64\u670d\u52a1\u652f\u6301 AI \u56fe\u7247\u751f\u6210\u529f\u80fd\u3002\n" +
            "\u4f60\u53ef\u4ee5\u8ba9\u6211\u6839\u636e\u63cf\u8ff0\u751f\u6210\u56fe\u7247\uff0c\u4e5f\u53ef\u4ee5\u8ba9\u6211\u5e2e\u4f60\u914d\u7f6e API \u53c2\u6570\u3002\n\n" +
            "## \u63d0\u4f9b\u5de5\u5177\n" + doc
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
