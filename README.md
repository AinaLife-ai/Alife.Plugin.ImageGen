# Alife.Plugin.ImageGen

AI 图片生成插件，支持自定义 API 接口地址、API Key、模型和尺寸。兼容 OpenAI 格式接口及 Stable Diffusion API。

## 功能

- 通过 `SetConfig` 配置 API 参数（接口地址、Key、模型、尺寸）
- 通过 `GenerateImage` 根据提示词生成图片，支持自定义尺寸和数量

## 配置说明

使用 `SetConfig` 配置以下参数：

| 参数 | 说明 | 示例 |
|------|------|------|
| `endpoint` | API 接口地址（支持 OpenAI 兼容格式） | `https://api.openai.com/v1/images/generations` |
| `apiKey` | API Key | `sk-xxx` |
| `model` | 模型名称 | `dall-e-3`, `stable-diffusion-xl-1024-v1-0` |
| `width` | 默认宽度 | `1024` |
| `height` | 默认高度 | `1024` |

## 使用方法

1. 将 `ImageGenModule.cs` 放入 Alife 的插件目录
2. 启动 Alife，调用 `SetConfig` 配置 API 参数
3. 调用 `GenerateImage` 生成图片

支持单图和多图生成（最多 4 张）。

## 依赖

- Alife Framework（>= 当前版本）
- 兼容 OpenAI / Stable Diffusion API 格式的接口服务

## 许可证

MIT