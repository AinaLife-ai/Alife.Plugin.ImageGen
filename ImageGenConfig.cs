namespace Alife.Plugin.ImageGen;

public class ImageGenConfig
{
    public string ApiEndpoint { get; set; } = "https://api.openai.com/v1/images/generations";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "dall-e-3";
    public int DefaultWidth { get; set; } = 1024;
    public int DefaultHeight { get; set; } = 1024;
    public List<string> SupportedSizes { get; set; } = new()
    {
        "1024x1024",
        "1024x1792",
        "1792x1024",
        "512x512",
        "768x768",
    };
}