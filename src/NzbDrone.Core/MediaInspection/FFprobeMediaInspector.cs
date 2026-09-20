using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.MediaInspection;

public class ProcessExecutionResult
{
    public int ExitCode { get; set; }

    public string StandardOutput { get; set; } = string.Empty;

    public string StandardError { get; set; } = string.Empty;

    public bool TimedOut { get; set; }
}

public class FFprobeMediaInspector : IFFprobeMediaInspector
{
    private readonly string _ffprobePath;
    private readonly TimeSpan _timeout;
    private readonly Func<string, IReadOnlyList<string>, TimeSpan, CancellationToken, Task<ProcessExecutionResult>> _processExecutor;
    private readonly Logger _logger;
    private readonly object _lock = new();
    private bool? _isAvailableCached;

    public FFprobeMediaInspector()
        : this("ffprobe", TimeSpan.FromSeconds(10), null, null)
    {
    }

    public FFprobeMediaInspector(
        string ffprobePath = "ffprobe",
        TimeSpan? timeout = null,
        Func<string, IReadOnlyList<string>, TimeSpan, CancellationToken, Task<ProcessExecutionResult>> processExecutor = null,
        Logger logger = null)
    {
        _ffprobePath = string.IsNullOrWhiteSpace(ffprobePath) ? "ffprobe" : ffprobePath;
        _timeout = timeout ?? TimeSpan.FromSeconds(10);
        _logger = logger ?? LogManager.GetCurrentClassLogger();
        _processExecutor = processExecutor ?? ((bin, args, to, ct) => DefaultExecuteProcessAsync(bin, args, to, ct, _logger));
    }

    public bool IsAvailable()
    {
        if (_isAvailableCached.HasValue)
        {
            return _isAvailableCached.Value;
        }

        lock (_lock)
        {
            if (_isAvailableCached.HasValue)
            {
                return _isAvailableCached.Value;
            }

            try
            {
                var result = _processExecutor(
                    _ffprobePath,
                    new[] { "-version" },
                    TimeSpan.FromSeconds(2),
                    CancellationToken.None).GetAwaiter().GetResult();

                _isAvailableCached = result != null && result.ExitCode == 0 && !result.TimedOut;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "ffprobe is not available at '{0}'", _ffprobePath);
                _isAvailableCached = false;
            }

            return _isAvailableCached.Value;
        }
    }

    public MediaContainerInfo Inspect(string filePath)
    {
        return InspectAsync(filePath).GetAwaiter().GetResult();
    }

    public async Task<MediaContainerInfo> InspectAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        var fileName = Path.GetFileName(filePath);

        if (!File.Exists(filePath) || !IsAvailable())
        {
            return MediaContainerInspector.InspectFileName(fileName);
        }

        try
        {
            var args = new[]
            {
                "-v", "quiet",
                "-print_format", "json",
                "-show_format",
                "-show_streams",
                filePath
            };

            var result = await _processExecutor(_ffprobePath, args, _timeout, cancellationToken).ConfigureAwait(false);

            if (result == null || result.TimedOut || result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                _logger.Warn(
                    "ffprobe execution failed (exit code {0}, timeout: {1}) for '{2}', falling back to filename heuristics",
                    result?.ExitCode ?? -1,
                    result?.TimedOut ?? false,
                    filePath);

                return MediaContainerInspector.InspectFileName(fileName);
            }

            var info = ParseFfprobeJson(result.StandardOutput, filePath);
            return info ?? MediaContainerInspector.InspectFileName(fileName);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Exception while inspecting file '{0}' with ffprobe, falling back to filename heuristics", filePath);
            return MediaContainerInspector.InspectFileName(fileName);
        }
    }

    public MediaContainerInfo ParseFfprobeJson(string json, string filePath = null)
    {
        var fileName = !string.IsNullOrWhiteSpace(filePath) ? Path.GetFileName(filePath) : string.Empty;

        if (string.IsNullOrWhiteSpace(json))
        {
            return string.IsNullOrEmpty(fileName) ? new MediaContainerInfo() : MediaContainerInspector.InspectFileName(fileName);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string formatName = null;
            var duration = 0.0;

            if (root.TryGetProperty("format", out var formatElem) && formatElem.ValueKind == JsonValueKind.Object)
            {
                if (formatElem.TryGetProperty("format_name", out var fnProp))
                {
                    formatName = fnProp.GetString();
                }

                if (formatElem.TryGetProperty("duration", out var durProp))
                {
                    if (double.TryParse(durProp.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    {
                        duration = d;
                    }
                }
            }

            JsonElement? videoStream = null;
            JsonElement? audioStream = null;
            var subtitleTracks = new List<string>();

            if (root.TryGetProperty("streams", out var streamsElem) && streamsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var stream in streamsElem.EnumerateArray())
                {
                    var codecType = stream.TryGetProperty("codec_type", out var ct) ? ct.GetString()?.ToLowerInvariant() : null;

                    if (codecType == "video" && videoStream == null)
                    {
                        var isAttachedPic = false;
                        if (stream.TryGetProperty("disposition", out var disp) && disp.ValueKind == JsonValueKind.Object)
                        {
                            if (disp.TryGetProperty("attached_pic", out var ap) && ap.TryGetInt32(out var apVal) && apVal == 1)
                            {
                                isAttachedPic = true;
                            }
                        }

                        if (!isAttachedPic)
                        {
                            videoStream = stream;
                        }
                    }
                    else if (codecType == "audio" && audioStream == null)
                    {
                        audioStream = stream;
                    }
                    else if (codecType == "subtitle")
                    {
                        string subLang = null;
                        if (stream.TryGetProperty("tags", out var subTags) && subTags.ValueKind == JsonValueKind.Object)
                        {
                            if (subTags.TryGetProperty("language", out var langProp))
                            {
                                subLang = langProp.GetString();
                            }

                            if (string.IsNullOrWhiteSpace(subLang) && subTags.TryGetProperty("title", out var titleProp))
                            {
                                subLang = titleProp.GetString();
                            }
                        }

                        if (string.IsNullOrWhiteSpace(subLang) && stream.TryGetProperty("codec_name", out var scn))
                        {
                            subLang = scn.GetString();
                        }

                        if (!string.IsNullOrWhiteSpace(subLang))
                        {
                            subtitleTracks.Add(subLang);
                        }
                    }
                }
            }

            var width = 0;
            var height = 0;
            string resolution = null;
            string videoCodec = null;
            var frameRate = 0.0;
            string hdrFormat = null;

            if (videoStream.HasValue)
            {
                var vs = videoStream.Value;

                if (vs.TryGetProperty("width", out var wProp) && wProp.TryGetInt32(out var w))
                {
                    width = w;
                }

                if (vs.TryGetProperty("height", out var hProp) && hProp.TryGetInt32(out var h))
                {
                    height = h;
                }

                if (width > 0 || height > 0)
                {
                    if (width >= 3800 || height >= 2100)
                    {
                        resolution = "2160p";
                    }
                    else if (width >= 1900 || height >= 1000)
                    {
                        resolution = "1080p";
                    }
                    else if (width >= 1200 || height >= 700)
                    {
                        resolution = "720p";
                    }
                    else if (width >= 700 || height >= 570)
                    {
                        resolution = "576p";
                    }
                    else if (width >= 640 || height >= 470)
                    {
                        resolution = "480p";
                    }
                    else if (height > 0)
                    {
                        resolution = $"{height}p";
                    }
                }

                if (vs.TryGetProperty("codec_name", out var cnProp))
                {
                    var rawCodec = cnProp.GetString()?.ToLowerInvariant();
                    videoCodec = MapVideoCodec(rawCodec);
                }

                string fpsStr = null;
                if (vs.TryGetProperty("avg_frame_rate", out var afrProp))
                {
                    fpsStr = afrProp.GetString();
                }

                if ((string.IsNullOrWhiteSpace(fpsStr) || fpsStr == "0/0") && vs.TryGetProperty("r_frame_rate", out var rfrProp))
                {
                    fpsStr = rfrProp.GetString();
                }

                if (!string.IsNullOrWhiteSpace(fpsStr) && fpsStr.Contains('/'))
                {
                    var parts = fpsStr.Split('/');
                    if (parts.Length == 2 &&
                        double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var num) &&
                        double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var den) &&
                        den > 0)
                    {
                        frameRate = Math.Round(num / den, 3);
                    }
                }
                else if (!string.IsNullOrWhiteSpace(fpsStr) && double.TryParse(fpsStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedFps))
                {
                    frameRate = Math.Round(parsedFps, 3);
                }

                hdrFormat = DetectHdrFormat(vs);

                if (duration <= 0 && vs.TryGetProperty("duration", out var vDurProp) &&
                    double.TryParse(vDurProp.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var vd))
                {
                    duration = vd;
                }
            }

            string audioCodec = null;
            string audioChannels = null;
            var audioSampleRate = 0;
            var audioBitDepth = 0;
            var audioBitrate = 0;
            string audioLanguage = null;

            if (audioStream.HasValue)
            {
                var @as = audioStream.Value;

                if (@as.TryGetProperty("codec_name", out var acnProp))
                {
                    var rawCodec = acnProp.GetString()?.ToLowerInvariant();
                    var profile = @as.TryGetProperty("profile", out var profProp) ? profProp.GetString() : null;
                    var codecLongName = @as.TryGetProperty("codec_long_name", out var clnProp) ? clnProp.GetString() : null;
                    var tags = @as.TryGetProperty("tags", out var tProp) && tProp.ValueKind == JsonValueKind.Object ? tProp : (JsonElement?)null;

                    audioCodec = MapAudioCodec(rawCodec, profile, codecLongName, tags);
                }

                var channels = 0;
                if (@as.TryGetProperty("channels", out var chProp) && chProp.TryGetInt32(out var ch))
                {
                    channels = ch;
                }

                var channelLayout = @as.TryGetProperty("channel_layout", out var clProp) ? clProp.GetString() : null;
                audioChannels = MapAudioChannels(channels, channelLayout);

                if (@as.TryGetProperty("sample_rate", out var srProp))
                {
                    if (srProp.ValueKind == JsonValueKind.Number && srProp.TryGetInt32(out var srNum))
                    {
                        audioSampleRate = srNum;
                    }
                    else if (srProp.ValueKind == JsonValueKind.String && int.TryParse(srProp.GetString(), out var srParsed))
                    {
                        audioSampleRate = srParsed;
                    }
                }

                if (@as.TryGetProperty("bits_per_raw_sample", out var bprsProp))
                {
                    if (bprsProp.ValueKind == JsonValueKind.Number && bprsProp.TryGetInt32(out var bdNum))
                    {
                        audioBitDepth = bdNum;
                    }
                    else if (bprsProp.ValueKind == JsonValueKind.String && int.TryParse(bprsProp.GetString(), out var bdParsed))
                    {
                        audioBitDepth = bdParsed;
                    }
                }

                if (audioBitDepth <= 0 && @as.TryGetProperty("bits_per_sample", out var bpsProp))
                {
                    if (bpsProp.ValueKind == JsonValueKind.Number && bpsProp.TryGetInt32(out var bpsNum))
                    {
                        audioBitDepth = bpsNum;
                    }
                    else if (bpsProp.ValueKind == JsonValueKind.String && int.TryParse(bpsProp.GetString(), out var bpsParsed))
                    {
                        audioBitDepth = bpsParsed;
                    }
                }

                if (@as.TryGetProperty("bit_rate", out var abrProp))
                {
                    if (abrProp.ValueKind == JsonValueKind.Number && abrProp.TryGetInt32(out var abrNum))
                    {
                        audioBitrate = abrNum;
                    }
                    else if (abrProp.ValueKind == JsonValueKind.String && int.TryParse(abrProp.GetString(), out var abrParsed))
                    {
                        audioBitrate = abrParsed;
                    }
                }

                if (@as.TryGetProperty("tags", out var aTags) && aTags.ValueKind == JsonValueKind.Object)
                {
                    if (aTags.TryGetProperty("language", out var aLangProp))
                    {
                        audioLanguage = aLangProp.GetString();
                    }
                }

                if (duration <= 0 && @as.TryGetProperty("duration", out var aDurProp) &&
                    double.TryParse(aDurProp.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var ad))
                {
                    duration = ad;
                }
            }

            var info = new MediaContainerInfo
            {
                ContainerFormat = MapContainerFormat(formatName, filePath),
                VideoCodec = videoCodec,
                Resolution = resolution,
                Width = width,
                Height = height,
                HdrFormat = hdrFormat,
                AudioCodec = audioCodec,
                AudioChannels = audioChannels,
                AudioSampleRate = audioSampleRate,
                AudioBitDepth = audioBitDepth,
                AudioBitrate = audioBitrate,
                AudioLanguage = audioLanguage,
                SubtitleTracks = subtitleTracks,
                DurationSeconds = duration,
                FrameRate = frameRate,
            };

            if (!string.IsNullOrWhiteSpace(fileName))
            {
                var regexGuess = MediaContainerInspector.InspectFileName(fileName);
                if (string.IsNullOrEmpty(info.ContainerFormat))
                {
                    info.ContainerFormat = regexGuess.ContainerFormat;
                }

                if (string.IsNullOrEmpty(info.Resolution))
                {
                    info.Resolution = regexGuess.Resolution;
                }

                if (string.IsNullOrEmpty(info.VideoCodec))
                {
                    info.VideoCodec = regexGuess.VideoCodec;
                }

                if (string.IsNullOrEmpty(info.HdrFormat))
                {
                    info.HdrFormat = regexGuess.HdrFormat;
                }

                if (string.IsNullOrEmpty(info.AudioCodec))
                {
                    info.AudioCodec = regexGuess.AudioCodec;
                }

                if (string.IsNullOrEmpty(info.AudioChannels))
                {
                    info.AudioChannels = regexGuess.AudioChannels;
                }

                if (info.Width == 0 && regexGuess.Width > 0)
                {
                    info.Width = regexGuess.Width;
                }

                if (info.Height == 0 && regexGuess.Height > 0)
                {
                    info.Height = regexGuess.Height;
                }
            }

            return info;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to parse ffprobe json output");
            return string.IsNullOrEmpty(fileName) ? new MediaContainerInfo() : MediaContainerInspector.InspectFileName(fileName);
        }
    }

    private static string MapVideoCodec(string rawCodec)
    {
        if (string.IsNullOrWhiteSpace(rawCodec))
        {
            return null;
        }

        return rawCodec switch
        {
            "hevc" or "h265" => "HEVC",
            "h264" or "avc" or "avc1" => "AVC",
            "av1" => "AV1",
            "vp9" => "VP9",
            "vp8" => "VP8",
            "mpeg2video" => "MPEG2",
            "mpeg4" => "MPEG4",
            "vc1" => "VC-1",
            "xvid" or "divx" or "msmpeg4v3" => "XviD",
            "prores" => "ProRes",
            _ => rawCodec.ToUpperInvariant(),
        };
    }

    private static string MapAudioCodec(string codec, string profile, string longName, JsonElement? tags)
    {
        if (string.IsNullOrWhiteSpace(codec))
        {
            return null;
        }

        var isAtmos = (profile != null && profile.Contains("Atmos", StringComparison.OrdinalIgnoreCase)) ||
                      (longName != null && longName.Contains("Atmos", StringComparison.OrdinalIgnoreCase));

        if (!isAtmos && tags.HasValue)
        {
            if (tags.Value.TryGetProperty("title", out var titleProp) &&
                titleProp.GetString()?.Contains("Atmos", StringComparison.OrdinalIgnoreCase) == true)
            {
                isAtmos = true;
            }
        }

        return codec switch
        {
            "truehd" => isAtmos ? "Dolby Atmos" : "TrueHD",
            "eac3" => isAtmos ? "Dolby Atmos" : "EAC3",
            "ac3" => "AC3",
            "dts" => (profile?.Contains("MA", StringComparison.OrdinalIgnoreCase) == true ||
                      profile?.Contains("Master Audio", StringComparison.OrdinalIgnoreCase) == true ||
                      longName?.Contains("Master Audio", StringComparison.OrdinalIgnoreCase) == true)
                     ? "DTS-HD MA"
                     : (profile?.Contains("HRA", StringComparison.OrdinalIgnoreCase) == true ? "DTS-HD HRA" : "DTS"),
            "dts_hd_ma" or "dtshd" or "dts-hd" => "DTS-HD MA",
            "flac" => "FLAC",
            "aac" => "AAC",
            "mp3" => "MP3",
            "opus" => "Opus",
            "vorbis" => "Vorbis",
            "alac" => "ALAC",
            "wmav2" or "wmapro" or "wmav1" => "WMA",
            _ when codec.StartsWith("pcm_", StringComparison.OrdinalIgnoreCase) => "PCM",
            _ => codec.ToUpperInvariant(),
        };
    }

    private static string MapAudioChannels(int channels, string channelLayout)
    {
        if (!string.IsNullOrWhiteSpace(channelLayout))
        {
            var layoutLower = channelLayout.ToLowerInvariant();
            if (layoutLower == "7.1" || layoutLower.Contains("7.1"))
            {
                return "7.1";
            }

            if (layoutLower == "5.1" || layoutLower.Contains("5.1"))
            {
                return "5.1";
            }

            if (layoutLower == "stereo" || layoutLower == "2.0")
            {
                return "2.0";
            }

            if (layoutLower == "mono" || layoutLower == "1.0")
            {
                return "1.0";
            }
        }

        return channels switch
        {
            8 => "7.1",
            6 => "5.1",
            2 => "2.0",
            1 => "1.0",
            > 0 => $"{channels}.0",
            _ => !string.IsNullOrWhiteSpace(channelLayout) ? channelLayout : null,
        };
    }

    private static string DetectHdrFormat(JsonElement vs)
    {
        if (vs.TryGetProperty("side_data_list", out var sdl) && sdl.ValueKind == JsonValueKind.Array)
        {
            foreach (var sd in sdl.EnumerateArray())
            {
                if (sd.TryGetProperty("side_data_type", out var sdt))
                {
                    var sdtStr = sdt.GetString();
                    if (!string.IsNullOrEmpty(sdtStr))
                    {
                        if (sdtStr.Contains("DOVI", StringComparison.OrdinalIgnoreCase) ||
                            sdtStr.Contains("Dolby Vision", StringComparison.OrdinalIgnoreCase))
                        {
                            return "Dolby Vision";
                        }

                        if (sdtStr.Contains("HDR Dynamic Metadata", StringComparison.OrdinalIgnoreCase) ||
                            sdtStr.Contains("HDR10+", StringComparison.OrdinalIgnoreCase) ||
                            sdtStr.Contains("SMPTE 2094-40", StringComparison.OrdinalIgnoreCase))
                        {
                            return "HDR10+";
                        }
                    }
                }

                if (sd.TryGetProperty("dv_version_major", out _))
                {
                    return "Dolby Vision";
                }
            }
        }

        if (vs.TryGetProperty("color_transfer", out var ctProp))
        {
            var ct = ctProp.GetString();
            if (string.Equals(ct, "smpte2084", StringComparison.OrdinalIgnoreCase))
            {
                return "HDR10";
            }

            if (string.Equals(ct, "arib-std-b67", StringComparison.OrdinalIgnoreCase))
            {
                return "HLG";
            }
        }

        return null;
    }

    private static string MapContainerFormat(string formatName, string filePath)
    {
        if (!string.IsNullOrWhiteSpace(formatName))
        {
            var fn = formatName.ToLowerInvariant();
            if (fn.Contains("matroska"))
            {
                return "Matroska";
            }

            if (fn.Contains("mp4") || fn.Contains("mov") || fn.Contains("m4a"))
            {
                return "MPEG-4";
            }

            if (fn.Contains("avi"))
            {
                return "AVI";
            }

            if (fn.Contains("flac"))
            {
                return "FLAC";
            }

            if (fn.Contains("mp3"))
            {
                return "MP3";
            }

            if (fn.Contains("ogg"))
            {
                return "Ogg";
            }

            if (fn.Contains("wav"))
            {
                return "WAV";
            }

            if (fn.Contains("webm"))
            {
                return "WebM";
            }

            if (fn.Contains("mpegts") || fn.Contains("m2ts"))
            {
                return "MPEG-TS";
            }

            if (fn.Contains("asf") || fn.Contains("wmv"))
            {
                return "Windows Media";
            }

            if (fn.Contains("flv"))
            {
                return "Flash Video";
            }
        }

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            var ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
            return ext switch
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

        return null;
    }

    private static async Task<ProcessExecutionResult> DefaultExecuteProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        Logger logger)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var arg in arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            logger?.Debug(ex, "Failed to start ffprobe process: {0}", fileName);
            return new ProcessExecutionResult { ExitCode = -1, TimedOut = false };
        }

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

        var stdoutTask = process.StandardOutput.ReadToEndAsync(linkedCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(linkedCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            var isTimeout = timeoutCts.IsCancellationRequested;
            logger?.Warn("Process {0} {1} after {2}s", fileName, isTimeout ? "timed out" : "was cancelled", timeout.TotalSeconds);
            await TerminateProcessTreeAsync(process, fileName, logger).ConfigureAwait(false);
            return new ProcessExecutionResult { ExitCode = -1, TimedOut = isTimeout };
        }

        var stdout = string.Empty;
        var stderr = string.Empty;

        try
        {
            using var drainCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(drainCts.Token).ConfigureAwait(false);
            stdout = await stdoutTask.ConfigureAwait(false);
            stderr = await stderrTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.Debug(ex, "Exception while draining ffprobe streams: {0}", fileName);
            if (stdoutTask.IsCompleted)
            {
                stdout = await stdoutTask.ConfigureAwait(false);
            }

            if (stderrTask.IsCompleted)
            {
                stderr = await stderrTask.ConfigureAwait(false);
            }
        }

        return new ProcessExecutionResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = stdout,
            StandardError = stderr,
            TimedOut = false,
        };
    }

    private static async Task TerminateProcessTreeAsync(Process process, string name, Logger logger)
    {
        if (process == null)
        {
            return;
        }

        try
        {
            var pid = -1;
            try
            {
                pid = process.Id;
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "Could not retrieve PID for process {0}", name);
            }

            if (pid > 0)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (Exception ex)
                {
                    logger?.Warn(ex, "Failed to kill process tree for {0} (PID {1})", name, pid);
                }

                if (!OperatingSystem.IsWindows())
                {
                    try
                    {
                        using var killProc = Process.Start(new ProcessStartInfo
                        {
                            FileName = "kill",
                            Arguments = $"-9 -{pid}",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                        });

                        if (killProc != null)
                        {
                            using var killCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                            await killProc.WaitForExitAsync(killCts.Token).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.Debug(ex, "Failed to send kill -9 to process group -{0}", pid);
                    }
                }
                else
                {
                    try
                    {
                        using var taskkillProc = Process.Start(new ProcessStartInfo
                        {
                            FileName = "taskkill",
                            Arguments = $"/F /T /PID {pid}",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                        });

                        if (taskkillProc != null)
                        {
                            using var taskkillCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                            await taskkillProc.WaitForExitAsync(taskkillCts.Token).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.Debug(ex, "Failed to taskkill /PID {0}", pid);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger?.Warn(ex, "Failed to terminate process {0}", name);
        }
    }
}
