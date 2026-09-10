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

    public double DurationSeconds { get; set; }
}

public interface IMediaContainerInspector
{
    MediaContainerInfo Inspect(System.IO.Stream stream, string fileName = "");

    MediaContainerInfo InspectFile(string filePath);
}

public class MediaContainerInspector : IMediaContainerInspector
{
    public MediaContainerInfo Inspect(System.IO.Stream stream, string fileName = "")
    {
        return InspectFileName(fileName);
    }

    public MediaContainerInfo InspectFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        var fileName = System.IO.Path.GetFileName(filePath);
        return InspectFileName(fileName);
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
                "flac" => "FLAC",
                "mp3" => "MP3",
                "aac" => "AAC",
                "ts" or "m2ts" => "MPEG-TS",
                _ => ext.ToUpperInvariant(),
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
