namespace Alife.Plugin.ImageGen;

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