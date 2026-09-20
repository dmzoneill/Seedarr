using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.MediaInspection;

namespace NzbDrone.Core.Test.MediaInspection;

[TestFixture]
public class FFprobeMediaInspectorTest
{
    private string _tempDirectory;
    private string _sampleFilePath;

    [SetUp]
    public void SetUp()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "seedarr_ffprobe_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _sampleFilePath = Path.Combine(_tempDirectory, "CleanMovieTitle.mkv");
        File.WriteAllText(_sampleFilePath, "dummy media content");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, true);
            }
            catch
            {
                // Ignored in teardown
            }
        }
    }

    [Test]
    public void ParseFfprobeJson_With4kHdr10PlusAndAtmos_ParsesAllStructuredMetrics()
    {
        var json = """
        {
            "streams": [
                {
                    "index": 0,
                    "codec_name": "hevc",
                    "codec_type": "video",
                    "width": 3840,
                    "height": 2160,
                    "avg_frame_rate": "24000/1001",
                    "duration": "7200.500000",
                    "side_data_list": [
                        {
                            "side_data_type": "HDR Dynamic Metadata / SMPTE 2094-40"
                        }
                    ]
                },
                {
                    "index": 1,
                    "codec_name": "truehd",
                    "codec_type": "audio",
                    "profile": "TrueHD+Atmos",
                    "channels": 8,
                    "channel_layout": "7.1",
                    "sample_rate": "48000",
                    "bits_per_raw_sample": "24",
                    "bit_rate": "5400000",
                    "tags": {
                        "language": "eng"
                    }
                },
                {
                    "index": 2,
                    "codec_name": "subrip",
                    "codec_type": "subtitle",
                    "tags": {
                        "language": "eng",
                        "title": "English SDH"
                    }
                },
                {
                    "index": 3,
                    "codec_name": "subrip",
                    "codec_type": "subtitle",
                    "tags": {
                        "language": "fre"
                    }
                }
            ],
            "format": {
                "format_name": "matroska,webm",
                "duration": "7200.500000"
            }
        }
        """;

        var inspector = new FFprobeMediaInspector();
        var result = inspector.ParseFfprobeJson(json, "Movie.Title.mkv");

        Assert.That(result.ContainerFormat, Is.EqualTo("Matroska"));
        Assert.That(result.Resolution, Is.EqualTo("2160p"));
        Assert.That(result.Width, Is.EqualTo(3840));
        Assert.That(result.Height, Is.EqualTo(2160));
        Assert.That(result.VideoCodec, Is.EqualTo("HEVC"));
        Assert.That(result.FrameRate, Is.EqualTo(23.976));
        Assert.That(result.HdrFormat, Is.EqualTo("HDR10+"));
        Assert.That(result.AudioCodec, Is.EqualTo("Dolby Atmos"));
        Assert.That(result.AudioChannels, Is.EqualTo("7.1"));
        Assert.That(result.AudioSampleRate, Is.EqualTo(48000));
        Assert.That(result.AudioBitDepth, Is.EqualTo(24));
        Assert.That(result.AudioBitrate, Is.EqualTo(5400000));
        Assert.That(result.AudioLanguage, Is.EqualTo("eng"));
        Assert.That(result.DurationSeconds, Is.EqualTo(7200.5));
        Assert.That(result.SubtitleTracks, Is.EquivalentTo(new[] { "eng", "fre" }));
    }

    [Test]
    public void ParseFfprobeJson_WithDolbyVisionSideData_DetectsDolbyVision()
    {
        var json = """
        {
            "streams": [
                {
                    "index": 0,
                    "codec_name": "hevc",
                    "codec_type": "video",
                    "width": 3840,
                    "height": 2160,
                    "avg_frame_rate": "24/1",
                    "side_data_list": [
                        {
                            "side_data_type": "DOVI configuration record",
                            "dv_version_major": 1
                        }
                    ]
                }
            ],
            "format": {
                "format_name": "mov,mp4,m4a,3gp,3g2,mj2"
            }
        }
        """;

        var inspector = new FFprobeMediaInspector();
        var result = inspector.ParseFfprobeJson(json, "movie.mp4");

        Assert.That(result.HdrFormat, Is.EqualTo("Dolby Vision"));
        Assert.That(result.ContainerFormat, Is.EqualTo("MPEG-4"));
        Assert.That(result.Resolution, Is.EqualTo("2160p"));
    }

    [Test]
    public void ParseFfprobeJson_WithHdr10ColorTransfer_DetectsHdr10()
    {
        var json = """
        {
            "streams": [
                {
                    "index": 0,
                    "codec_name": "hevc",
                    "codec_type": "video",
                    "width": 1920,
                    "height": 1080,
                    "color_transfer": "smpte2084"
                }
            ],
            "format": {
                "format_name": "matroska"
            }
        }
        """;

        var inspector = new FFprobeMediaInspector();
        var result = inspector.ParseFfprobeJson(json, "movie.mkv");

        Assert.That(result.HdrFormat, Is.EqualTo("HDR10"));
        Assert.That(result.Resolution, Is.EqualTo("1080p"));
    }

    [Test]
    public void ParseFfprobeJson_With1080pAvcAndStereoAac_ParsesMetricsCorrectly()
    {
        var json = """
        {
            "streams": [
                {
                    "index": 0,
                    "codec_name": "h264",
                    "codec_type": "video",
                    "width": 1920,
                    "height": 1080,
                    "r_frame_rate": "24/1",
                    "duration": "120.000000"
                },
                {
                    "index": 1,
                    "codec_name": "aac",
                    "codec_type": "audio",
                    "channels": 2,
                    "channel_layout": "stereo",
                    "sample_rate": "44100",
                    "bits_per_sample": 16,
                    "bit_rate": "128000"
                }
            ],
            "format": {
                "format_name": "mov,mp4,m4a,3gp,3g2,mj2",
                "duration": "120.000000"
            }
        }
        """;

        var inspector = new FFprobeMediaInspector();
        var result = inspector.ParseFfprobeJson(json, "sample.mp4");

        Assert.That(result.ContainerFormat, Is.EqualTo("MPEG-4"));
        Assert.That(result.Resolution, Is.EqualTo("1080p"));
        Assert.That(result.VideoCodec, Is.EqualTo("AVC"));
        Assert.That(result.FrameRate, Is.EqualTo(24.0));
        Assert.That(result.AudioCodec, Is.EqualTo("AAC"));
        Assert.That(result.AudioChannels, Is.EqualTo("2.0"));
        Assert.That(result.AudioSampleRate, Is.EqualTo(44100));
        Assert.That(result.AudioBitDepth, Is.EqualTo(16));
        Assert.That(result.AudioBitrate, Is.EqualTo(128000));
        Assert.That(result.DurationSeconds, Is.EqualTo(120.0));
    }

    [Test]
    public void ParseFfprobeJson_WithAudioOnlyFlac_SkipsVideoAndParsesAudio()
    {
        var json = """
        {
            "streams": [
                {
                    "index": 0,
                    "codec_name": "flac",
                    "codec_type": "audio",
                    "channels": 2,
                    "channel_layout": "stereo",
                    "sample_rate": "96000",
                    "bits_per_raw_sample": "24",
                    "bit_rate": "2500000",
                    "tags": {
                        "language": "jpn"
                    }
                }
            ],
            "format": {
                "format_name": "flac",
                "duration": "300.250000"
            }
        }
        """;

        var inspector = new FFprobeMediaInspector();
        var result = inspector.ParseFfprobeJson(json, "audio.flac");

        Assert.That(result.ContainerFormat, Is.EqualTo("FLAC"));
        Assert.That(result.VideoCodec, Is.Null);
        Assert.That(result.Resolution, Is.Null);
        Assert.That(result.AudioCodec, Is.EqualTo("FLAC"));
        Assert.That(result.AudioChannels, Is.EqualTo("2.0"));
        Assert.That(result.AudioSampleRate, Is.EqualTo(96000));
        Assert.That(result.AudioBitDepth, Is.EqualTo(24));
        Assert.That(result.AudioBitrate, Is.EqualTo(2500000));
        Assert.That(result.AudioLanguage, Is.EqualTo("jpn"));
        Assert.That(result.DurationSeconds, Is.EqualTo(300.25));
    }

    [Test]
    public void ParseFfprobeJson_WithAttachedPicVideoStream_SkipsCoverImage()
    {
        var json = """
        {
            "streams": [
                {
                    "index": 0,
                    "codec_name": "mjpeg",
                    "codec_type": "video",
                    "width": 600,
                    "height": 600,
                    "disposition": {
                        "attached_pic": 1
                    }
                },
                {
                    "index": 1,
                    "codec_name": "mp3",
                    "codec_type": "audio",
                    "channels": 2,
                    "channel_layout": "stereo",
                    "sample_rate": "44100",
                    "bit_rate": "320000"
                }
            ],
            "format": {
                "format_name": "mp3",
                "duration": "210.000000"
            }
        }
        """;

        var inspector = new FFprobeMediaInspector();
        var result = inspector.ParseFfprobeJson(json, "song.mp3");

        Assert.That(result.ContainerFormat, Is.EqualTo("MP3"));
        Assert.That(result.VideoCodec, Is.Null);
        Assert.That(result.AudioCodec, Is.EqualTo("MP3"));
        Assert.That(result.AudioChannels, Is.EqualTo("2.0"));
        Assert.That(result.AudioSampleRate, Is.EqualTo(44100));
        Assert.That(result.AudioBitrate, Is.EqualTo(320000));
    }

    [Test]
    public void ParseFfprobeJson_WithMalformedJson_FallsBackToFilenameHeuristics()
    {
        var inspector = new FFprobeMediaInspector();
        var result = inspector.ParseFfprobeJson("{ invalid json ", "Show.Title.1080p.mkv");

        Assert.That(result.ContainerFormat, Is.EqualTo("Matroska"));
        Assert.That(result.Resolution, Is.EqualTo("1080p"));
    }

    [Test]
    public void ParseFfprobeJson_WithEmptyOrNullJson_FallsBackToFilenameHeuristics()
    {
        var inspector = new FFprobeMediaInspector();
        var result = inspector.ParseFfprobeJson(string.Empty, "Movie.720p.mp4");

        Assert.That(result.ContainerFormat, Is.EqualTo("MPEG-4"));
        Assert.That(result.Resolution, Is.EqualTo("720p"));
    }

    [Test]
    public void Inspect_WhenFileDoesNotExist_FallsBackToFilenameHeuristics()
    {
        var inspector = new FFprobeMediaInspector();
        var nonExistentPath = Path.Combine(_tempDirectory, "NonExistent.1080p.mkv");

        var result = inspector.Inspect(nonExistentPath);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Resolution, Is.EqualTo("1080p"));
        Assert.That(result.ContainerFormat, Is.EqualTo("Matroska"));
    }

    [Test]
    public void Inspect_WhenFfprobeTimesOut_FallsBackToFilenameHeuristics()
    {
        var inspector = new FFprobeMediaInspector(
            processExecutor: (bin, args, to, ct) =>
            {
                if (args.Count > 0 && args[0] == "-version")
                {
                    return Task.FromResult(new ProcessExecutionResult { ExitCode = 0 });
                }

                return Task.FromResult(new ProcessExecutionResult { ExitCode = -1, TimedOut = true });
            });

        var result = inspector.Inspect(_sampleFilePath);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.ContainerFormat, Is.EqualTo("Matroska"));
    }

    [Test]
    public void Inspect_WhenFfprobeFailsWithNonZeroExitCode_FallsBackToFilenameHeuristics()
    {
        var inspector = new FFprobeMediaInspector(
            processExecutor: (bin, args, to, ct) =>
            {
                if (args.Count > 0 && args[0] == "-version")
                {
                    return Task.FromResult(new ProcessExecutionResult { ExitCode = 0 });
                }

                return Task.FromResult(new ProcessExecutionResult { ExitCode = 1, StandardError = "corrupt input" });
            });

        var result = inspector.Inspect(_sampleFilePath);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.ContainerFormat, Is.EqualTo("Matroska"));
    }

    [Test]
    public void Inspect_WhenProcessExecutorThrows_CatchesAndFallsBackToFilenameHeuristics()
    {
        var inspector = new FFprobeMediaInspector(
            processExecutor: (bin, args, to, ct) =>
            {
                if (args.Count > 0 && args[0] == "-version")
                {
                    return Task.FromResult(new ProcessExecutionResult { ExitCode = 0 });
                }

                throw new InvalidOperationException("Process failed to spawn");
            });

        var result = inspector.Inspect(_sampleFilePath);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.ContainerFormat, Is.EqualTo("Matroska"));
    }

    [Test]
    public void Inspect_WhenFfprobeSucceeds_ExecutesWithExpectedArgumentsAndReturnsParsedResult()
    {
        IReadOnlyList<string> capturedArgs = null;

        var sampleOutput = """
        {
            "streams": [
                {
                    "codec_name": "hevc",
                    "codec_type": "video",
                    "width": 1920,
                    "height": 1080,
                    "avg_frame_rate": "24/1"
                },
                {
                    "codec_name": "ac3",
                    "codec_type": "audio",
                    "channels": 6,
                    "channel_layout": "5.1",
                    "sample_rate": "48000"
                }
            ],
            "format": {
                "format_name": "matroska",
                "duration": "100.0"
            }
        }
        """;

        var inspector = new FFprobeMediaInspector(
            processExecutor: (bin, args, to, ct) =>
            {
                if (args.Count > 0 && args[0] == "-version")
                {
                    return Task.FromResult(new ProcessExecutionResult { ExitCode = 0 });
                }

                capturedArgs = args;
                return Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardOutput = sampleOutput });
            });

        var result = inspector.Inspect(_sampleFilePath);

        Assert.That(capturedArgs, Is.Not.Null);
        Assert.That(capturedArgs, Does.Contain("-v"));
        Assert.That(capturedArgs, Does.Contain("quiet"));
        Assert.That(capturedArgs, Does.Contain("-print_format"));
        Assert.That(capturedArgs, Does.Contain("json"));
        Assert.That(capturedArgs, Does.Contain("-show_format"));
        Assert.That(capturedArgs, Does.Contain("-show_streams"));
        Assert.That(capturedArgs, Does.Contain(_sampleFilePath));

        Assert.That(result.Resolution, Is.EqualTo("1080p"));
        Assert.That(result.Width, Is.EqualTo(1920));
        Assert.That(result.Height, Is.EqualTo(1080));
        Assert.That(result.VideoCodec, Is.EqualTo("HEVC"));
        Assert.That(result.AudioCodec, Is.EqualTo("AC3"));
        Assert.That(result.AudioChannels, Is.EqualTo("5.1"));
    }

    [Test]
    public void MediaContainerInspector_InspectFile_DelegatesToFFprobeInspectorWhenFileExists()
    {
        var ffprobeInspector = Substitute.For<IFFprobeMediaInspector>();
        ffprobeInspector.IsAvailable().Returns(true);

        var expectedInfo = new MediaContainerInfo
        {
            ContainerFormat = "Matroska",
            Resolution = "2160p",
            Width = 3840,
            Height = 2160,
            VideoCodec = "HEVC",
        };

        ffprobeInspector.Inspect(_sampleFilePath).Returns(expectedInfo);

        var inspector = new MediaContainerInspector(ffprobeInspector);
        var result = inspector.InspectFile(_sampleFilePath);

        Assert.That(result, Is.SameAs(expectedInfo));
        ffprobeInspector.Received(1).Inspect(_sampleFilePath);
    }

    [Test]
    public void MediaContainerInspector_InspectFile_FallsBackToFilenameWhenFFprobeReturnsNull()
    {
        var ffprobeInspector = Substitute.For<IFFprobeMediaInspector>();
        ffprobeInspector.IsAvailable().Returns(true);
        ffprobeInspector.Inspect(Arg.Any<string>()).Returns((MediaContainerInfo)null);

        var testFile = Path.Combine(_tempDirectory, "Show.1080p.mkv");
        File.WriteAllText(testFile, "dummy");

        var inspector = new MediaContainerInspector(ffprobeInspector);
        var result = inspector.InspectFile(testFile);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Resolution, Is.EqualTo("1080p"));
        Assert.That(result.ContainerFormat, Is.EqualTo("Matroska"));
    }

    [Test]
    public void MediaEnrichmentService_Constructor_IntegratesFFprobeInspector()
    {
        var ffprobe = Substitute.For<IFFprobeMediaInspector>();
        var service = new NzbDrone.Core.MediaEnrichment.MediaEnrichmentService(
            repository: null,
            inspector: null,
            ffprobeInspector: ffprobe);

        Assert.That(service.FFprobeInspector, Is.SameAs(ffprobe));
    }

    [Test]
    public void Inspect_WithRealFfprobeIfAvailable_ExtractsAccurateMetrics()
    {
        var inspector = new FFprobeMediaInspector();
        if (!inspector.IsAvailable())
        {
            Assert.Ignore("ffprobe is not available on host system");
        }

        // Test against a clean-named file that contains no resolution or codec tags
        var testFilePath = Path.Combine(_tempDirectory, "test_clean_video.mp4");

        try
        {
            using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = "-f lavfi -i testsrc=duration=1:size=1280x720:rate=24 -f lavfi -i sine=frequency=1000:duration=1 -c:v libx264 -c:a aac -y " + testFilePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            if (proc == null || !proc.WaitForExit(10000) || proc.ExitCode != 0)
            {
                Assert.Ignore("ffmpeg could not generate test fixture");
            }
        }
        catch
        {
            Assert.Ignore("ffmpeg execution not supported in this environment");
        }

        var result = inspector.Inspect(testFilePath);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Resolution, Is.EqualTo("720p"));
        Assert.That(result.Width, Is.EqualTo(1280));
        Assert.That(result.Height, Is.EqualTo(720));
        Assert.That(result.VideoCodec, Is.EqualTo("AVC"));
        Assert.That(result.AudioCodec, Is.EqualTo("AAC"));
        Assert.That(result.DurationSeconds, Is.GreaterThan(0.5));
    }
}
