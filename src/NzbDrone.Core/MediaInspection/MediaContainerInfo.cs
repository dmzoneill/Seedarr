using System;
using System.Collections.Generic;

namespace NzbDrone.Core.MediaInspection;

public class MediaContainerInfo
{
    public string ContainerFormat { get; set; }

    public string VideoCodec { get; set; }

    public string Resolution { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public string HdrFormat { get; set; }

    public string AudioCodec { get; set; }

    public string AudioChannels { get; set; }

    public int AudioSampleRate { get; set; }

    public int AudioBitDepth { get; set; }

    public List<string> SubtitleTracks { get; set; } = new();

    public List<NzbDrone.Core.Subtitles.SubtitleTrackInfo> SubtitleTrackDetails { get; set; } = new();

    public double DurationSeconds { get; set; }

    public double FrameRate { get; set; }

    public int AudioBitrate { get; set; }

    public string AudioLanguage { get; set; }
}

public interface IMediaContainerInspector
{
    MediaContainerInfo Inspect(System.IO.Stream stream, string fileName = "");

    MediaContainerInfo InspectFile(string filePath);
}

public class MediaContainerInspector : IMediaContainerInspector
{
    private readonly IFFprobeMediaInspector _ffprobeInspector;
    private readonly NzbDrone.Core.Subtitles.ISubtitleDiscoveryService _subtitleDiscoveryService;

    public MediaContainerInspector(
        IFFprobeMediaInspector ffprobeInspector = null,
        NzbDrone.Core.Subtitles.ISubtitleDiscoveryService subtitleDiscoveryService = null)
    {
        _ffprobeInspector = ffprobeInspector ?? new FFprobeMediaInspector();
        _subtitleDiscoveryService = subtitleDiscoveryService ?? new NzbDrone.Core.Subtitles.SubtitleDiscoveryService();
    }

    public MediaContainerInfo Inspect(System.IO.Stream stream, string fileName = "")
    {
        if (stream == null || !stream.CanRead)
        {
            return InspectFileName(fileName);
        }

        var buffer = new byte[4096];
        var bytesRead = 0;
        long initialPos = 0;
        var canSeek = false;

        try
        {
            if (stream.CanSeek)
            {
                initialPos = stream.Position;
                canSeek = true;
            }

            while (bytesRead < buffer.Length)
            {
                var read = stream.Read(buffer, bytesRead, buffer.Length - bytesRead);
                if (read <= 0)
                {
                    break;
                }

                bytesRead += read;
            }
        }
        catch
        {
            return InspectFileName(fileName);
        }
        finally
        {
            if (canSeek)
            {
                try
                {
                    stream.Position = initialPos;
                }
                catch
                {
                }
            }
        }

        if (bytesRead < 4)
        {
            return InspectFileName(fileName);
        }

        var headerInfo = DetectHeader(buffer, bytesRead, fileName);
        if (headerInfo == null || string.IsNullOrEmpty(headerInfo.ContainerFormat))
        {
            return InspectFileName(fileName);
        }

        var result = InspectFileName(fileName);
        result.ContainerFormat = headerInfo.ContainerFormat;

        if (!string.IsNullOrEmpty(headerInfo.AudioCodec))
        {
            result.AudioCodec = headerInfo.AudioCodec;
        }

        if (!string.IsNullOrEmpty(headerInfo.AudioChannels))
        {
            result.AudioChannels = headerInfo.AudioChannels;
        }

        if (headerInfo.AudioSampleRate > 0)
        {
            result.AudioSampleRate = headerInfo.AudioSampleRate;
        }

        if (headerInfo.AudioBitDepth > 0)
        {
            result.AudioBitDepth = headerInfo.AudioBitDepth;
        }

        return result;
    }

    private static MediaContainerInfo DetectHeader(byte[] buffer, int bytesRead, string fileName)
    {
        // EBML: 0x1A, 0x45, 0xDF, 0xA3 -> Matroska or WebM
        if (bytesRead >= 4 && buffer[0] == 0x1A && buffer[1] == 0x45 && buffer[2] == 0xDF && buffer[3] == 0xA3)
        {
            var isWebm = !string.IsNullOrEmpty(fileName) && fileName.EndsWith(".webm", StringComparison.OrdinalIgnoreCase);
            if (!isWebm)
            {
                var maxCheck = Math.Min(bytesRead - 3, 512);
                for (var i = 4; i < maxCheck; i++)
                {
                    if ((buffer[i] == 'w' || buffer[i] == 'W') &&
                        (buffer[i + 1] == 'e' || buffer[i + 1] == 'E') &&
                        (buffer[i + 2] == 'b' || buffer[i + 2] == 'B') &&
                        (buffer[i + 3] == 'm' || buffer[i + 3] == 'M'))
                    {
                        isWebm = true;
                        break;
                    }
                }
            }

            return new MediaContainerInfo
            {
                ContainerFormat = isWebm ? "WebM" : "Matroska"
            };
        }

        // MP4 / ISO Base Media: 'ftyp' box at offset 4
        if (bytesRead >= 8 && buffer[4] == 0x66 && buffer[5] == 0x74 && buffer[6] == 0x79 && buffer[7] == 0x70)
        {
            var brand = bytesRead >= 12 ? System.Text.Encoding.ASCII.GetString(buffer, 8, 4) : string.Empty;
            return new MediaContainerInfo
            {
                ContainerFormat = brand == "qt  " ? "QuickTime" : "MPEG-4"
            };
        }

        // FLAC: magic 'fLaC' (0x66, 0x4C, 0x61, 0x43)
        if (bytesRead >= 4 && buffer[0] == 0x66 && buffer[1] == 0x4C && buffer[2] == 0x61 && buffer[3] == 0x43)
        {
            var flacInfo = new MediaContainerInfo
            {
                ContainerFormat = "FLAC",
                AudioCodec = "FLAC"
            };

            var siOffset = 4;
            if (bytesRead >= 42 && (buffer[4] == 0x00 || buffer[4] == 0x80) && buffer[5] == 0x00 && buffer[6] == 0x00 && buffer[7] == 0x22)
            {
                siOffset = 8;
            }

            if (bytesRead >= siOffset + 14)
            {
                var sampleRate = (buffer[siOffset + 10] << 12) | (buffer[siOffset + 11] << 4) | (buffer[siOffset + 12] >> 4);
                var channels = ((buffer[siOffset + 12] >> 1) & 0x07) + 1;
                var bitDepth = (((buffer[siOffset + 12] & 0x01) << 4) | (buffer[siOffset + 13] >> 4)) + 1;

                flacInfo.AudioSampleRate = sampleRate;
                flacInfo.AudioBitDepth = bitDepth;
                flacInfo.AudioChannels = channels switch
                {
                    8 => "7.1",
                    6 => "5.1",
                    2 => "2.0",
                    1 => "1.0",
                    > 0 => $"{channels}.0",
                    _ => null
                };
            }

            return flacInfo;
        }

        // ID3v2 / MP3: "ID3" (0x49, 0x44, 0x33) or sync bytes (0xFF, 0xFB / 0xF3 / 0xF2 / 0xFA)
        if ((bytesRead >= 3 && buffer[0] == 0x49 && buffer[1] == 0x44 && buffer[2] == 0x33) ||
            (bytesRead >= 2 && buffer[0] == 0xFF && (buffer[1] == 0xFB || buffer[1] == 0xF3 || buffer[1] == 0xF2 || (buffer[1] & 0xFE) == 0xFA || (buffer[1] & 0xFE) == 0xF2)))
        {
            return new MediaContainerInfo
            {
                ContainerFormat = "MP3",
                AudioCodec = "MP3"
            };
        }

        // RIFF: "RIFF" at offset 0, and format at offset 8: "AVI " or "WAVE"
        if (bytesRead >= 12 && buffer[0] == 0x52 && buffer[1] == 0x49 && buffer[2] == 0x46 && buffer[3] == 0x46)
        {
            var riffFormat = System.Text.Encoding.ASCII.GetString(buffer, 8, 4);
            if (riffFormat == "AVI ")
            {
                return new MediaContainerInfo
                {
                    ContainerFormat = "AVI"
                };
            }

            if (riffFormat == "WAVE")
            {
                return new MediaContainerInfo
                {
                    ContainerFormat = "WAV"
                };
            }
        }

        // Ogg: "OggS" (0x4F, 0x67, 0x67, 0x53)
        if (bytesRead >= 4 && buffer[0] == 0x4F && buffer[1] == 0x67 && buffer[2] == 0x67 && buffer[3] == 0x53)
        {
            return new MediaContainerInfo
            {
                ContainerFormat = "Ogg"
            };
        }

        return null;
    }

    public MediaContainerInfo InspectFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        MediaContainerInfo info = null;

        if (System.IO.File.Exists(filePath) && _ffprobeInspector != null)
        {
            try
            {
                info = _ffprobeInspector.Inspect(filePath);
            }
            catch
            {
                // Fallback to stream/filename inspection below
            }
        }

        if (info == null)
        {
            var fileName = System.IO.Path.GetFileName(filePath);
            if (System.IO.File.Exists(filePath))
            {
                try
                {
                    using var stream = new System.IO.FileStream(filePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
                    info = Inspect(stream, fileName);
                }
                catch
                {
                    info = InspectFileName(fileName);
                }
            }
            else
            {
                info = InspectFileName(fileName);
            }
        }

        if (info != null && System.IO.File.Exists(filePath) && _subtitleDiscoveryService != null)
        {
            try
            {
                var externalSubs = _subtitleDiscoveryService.DiscoverSubtitles(filePath);
                if (externalSubs != null && externalSubs.Count > 0)
                {
                    info.SubtitleTrackDetails = externalSubs;
                    foreach (var sub in externalSubs)
                    {
                        var display = !string.IsNullOrEmpty(sub.Language) && sub.Language != "und"
                            ? sub.Language
                            : sub.Title;

                        if (!info.SubtitleTracks.Exists(t => string.Equals(t, display, StringComparison.OrdinalIgnoreCase)))
                        {
                            info.SubtitleTracks.Add(display);
                        }
                    }
                }
            }
            catch
            {
                // Non-critical subtitle discovery failure
            }
        }

        return info;
    }

    public static MediaContainerInfo InspectFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return new MediaContainerInfo();
        }

        var info = new MediaContainerInfo();

        var ext = System.IO.Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        if (!string.IsNullOrEmpty(ext))
        {
            info.ContainerFormat = ext switch
            {
                "mkv" => "Matroska",
                "mp4" or "m4v" => "MPEG-4",
                "avi" => "AVI",
                "mov" => "QuickTime",
                "wmv" => "Windows Media",
                "flv" => "Flash Video",
                "webm" => "WebM",
                "ts" or "m2ts" => "MPEG-TS",
                "flac" => "FLAC",
                "mp3" => "MP3",
                "aac" => "AAC",
                "ogg" or "oga" => "Ogg",
                "wav" => "WAV",
                _ => null,
            };
        }

        // Resolution
        if (System.Text.RegularExpressions.Regex.IsMatch(fileName, @"(?i)\b(2160p|4k|uhd)\b"))
        {
            info.Resolution = "2160p";
            info.Width = 3840;
            info.Height = 2160;
        }
        else if (System.Text.RegularExpressions.Regex.IsMatch(fileName, @"(?i)\b(1080p|1080i)\b"))
        {
            info.Resolution = "1080p";
            info.Width = 1920;
            info.Height = 1080;
        }
        else if (System.Text.RegularExpressions.Regex.IsMatch(fileName, @"(?i)\b720p\b"))
        {
            info.Resolution = "720p";
            info.Width = 1280;
            info.Height = 720;
        }
        else if (System.Text.RegularExpressions.Regex.IsMatch(fileName, @"(?i)\b(576p|576i)\b"))
        {
            info.Resolution = "576p";
            info.Width = 720;
            info.Height = 576;
        }
        else if (System.Text.RegularExpressions.Regex.IsMatch(fileName, @"(?i)\b(480p|480i)\b"))
        {
            info.Resolution = "480p";
            info.Width = 720;
            info.Height = 480;
        }

        // HDR
        if (System.Text.RegularExpressions.Regex.IsMatch(fileName, @"(?i)\b(dv|dovi|dolby[\s._-]*vision)\b"))
        {
            info.HdrFormat = "Dolby Vision";
        }
        else if (System.Text.RegularExpressions.Regex.IsMatch(fileName, @"(?i)\b(hdr10\+|hdr10plus)\b"))
        {
            info.HdrFormat = "HDR10+";
        }
        else if (System.Text.RegularExpressions.Regex.IsMatch(fileName, @"(?i)\b(hdr|hdr10)\b"))
        {
            info.HdrFormat = "HDR10";
        }

        // Video Codec
        if (System.Text.RegularExpressions.Regex.IsMatch(fileName, @"(?i)\b(hevc|x265|h265|h\.265)\b"))
        {
            info.VideoCodec = "HEVC";
        }
        else if (System.Text.RegularExpressions.Regex.IsMatch(fileName, @"(?i)\b(avc|x264|h264|h\.264)\b"))
        {
            info.VideoCodec = "AVC";
        }
        else if (System.Text.RegularExpressions.Regex.IsMatch(fileName, @"(?i)\bav1\b"))
        {
            info.VideoCodec = "AV1";
        }
        else if (System.Text.RegularExpressions.Regex.IsMatch(fileName, @"(?i)\b(xvid|divx)\b"))
        {
            info.VideoCodec = "XviD";
        }

        // Audio Codec
        if (System.Text.RegularExpressions.Regex.IsMatch(fileName, @"(?i)\b(atmos)\b"))
        {
            info.AudioCodec = "Dolby Atmos";
        }
        else if (System.Text.RegularExpressions.Regex.IsMatch(fileName, @"(?i)\b(truehd)\b"))
        {
            info.AudioCodec = "TrueHD";
        }
        else if (System.Text.RegularExpressions.Regex.IsMatch(fileName, @"(?i)\b(dts[\s._-]*hd[\s._-]*ma|dts[\s._-]*hd)\b"))
        {
            info.AudioCodec = "DTS-HD MA";
        }
        else if (System.Text.RegularExpressions.Regex.IsMatch(fileName, @"(?i)\bdts\b"))
        {
            info.AudioCodec = "DTS";
        }
        else if (System.Text.RegularExpressions.Regex.IsMatch(fileName, @"(?i)\b(eac3|dd\+|ddp|digital[\s._-]*plus)\b"))
        {
            info.AudioCodec = "EAC3";
        }
        else if (System.Text.RegularExpressions.Regex.IsMatch(fileName, @"(?i)\b(ac3|dd5\.1|dd2\.0)\b"))
        {
            info.AudioCodec = "AC3";
        }
        else if (System.Text.RegularExpressions.Regex.IsMatch(fileName, @"(?i)\bflac\b"))
        {
            info.AudioCodec = "FLAC";
        }
        else if (System.Text.RegularExpressions.Regex.IsMatch(fileName, @"(?i)\baac\b"))
        {
            info.AudioCodec = "AAC";
        }
        else if (System.Text.RegularExpressions.Regex.IsMatch(fileName, @"(?i)\bmp3\b"))
        {
            info.AudioCodec = "MP3";
        }

        // Channels
        if (System.Text.RegularExpressions.Regex.IsMatch(fileName, @"(?i)\b(7\.1|8ch)\b"))
        {
            info.AudioChannels = "7.1";
        }
        else if (System.Text.RegularExpressions.Regex.IsMatch(fileName, @"(?i)\b(5\.1|6ch)\b"))
        {
            info.AudioChannels = "5.1";
        }
        else if (System.Text.RegularExpressions.Regex.IsMatch(fileName, @"(?i)\b(2\.0|stereo)\b"))
        {
            info.AudioChannels = "2.0";
        }

        return info;
    }
}
