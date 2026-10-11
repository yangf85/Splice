开发者请将 Windows 版 ffmpeg.exe 放入这里：Tools\ffmpeg.exe。
Splice 启动和合并时只会查找应用所在目录下的 Tools\ffmpeg.exe。
项目将在 Visual Studio 构建/发布时自动复制此文件，发布包没有该文件会报错。
最终用户无需安装或配置 FFmpeg；无需 ffprobe.exe、ffplay.exe。
分发时请遵守你选用的 FFmpeg 构建对应许可证。
