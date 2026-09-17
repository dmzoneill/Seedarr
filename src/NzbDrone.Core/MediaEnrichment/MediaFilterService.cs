using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.MediaEnrichment;

public class MediaFilterService : IMediaFilterService
{
    public const long MaxSampleSizeInBytes = 70 * 1024 * 1024; // 70 MB

    private static readonly Regex SampleWordRegex = new(
        @"(?i)(?:^|[._\-\s\[\(])sample(?:[._\-\s\]\)]|$)",
        RegexOptions.Compiled);

    private static readonly Regex SamplePathRegex = new(
        @"(?i)(?:^|[\\/])samples?(?:[\\/]|$)",
        RegexOptions.Compiled);

    private static readonly HashSet<string> ClutterExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".nfo",
        ".sfv",
        ".txt",
        ".jpg",
        ".jpeg",
        ".png",
        ".gif",
        ".url",
        ".idx",
        ".sub",
        ".srr",
        ".srs",
        ".nzb",
        ".torrent",
        ".exe",
        ".bat",
        ".cmd",
        ".sh",
        ".md5",
        ".sha1",
        ".sha256",
        ".par",
        ".par2",
    };

    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv",
        ".mp4",
        ".avi",
        ".ts",
        ".m2ts",
        ".wmv",
        ".mov",
        ".iso",
        ".webm",
        ".mpg",
        ".mpeg",
        ".flv",
        ".vob",
        ".m4v",
    };

    public bool IsSampleFile(string filePath, long fileSize = 0)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        if (fileSize <= 0)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    fileSize = new FileInfo(filePath).Length;
                }
            }
            catch
            {
                // Ignore file system errors
            }
        }

        if (fileSize >= MaxSampleSizeInBytes)
        {
            return false;
        }

        var fileName = Path.GetFileName(filePath);
        if (SampleWordRegex.IsMatch(fileName))
        {
            return true;
        }

        var dirName = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dirName) && SamplePathRegex.IsMatch(dirName))
        {
            return true;
        }

        return false;
    }

    public bool IsClutterFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var ext = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(ext))
        {
            return false;
        }

        return ClutterExtensions.Contains(ext);
    }

    public bool IsMediaFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var ext = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(ext))
        {
            return false;
        }

        return MediaExtensions.Contains(ext);
    }

    public string SelectPrimaryMediaFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (Directory.Exists(path))
        {
            try
            {
                var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories);
                return SelectFromFiles(files);
            }
            catch
            {
                return null;
            }
        }

        if (File.Exists(path))
        {
            if (IsSampleFile(path) || IsClutterFile(path))
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    try
                    {
                        var files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories);
                        var selected = SelectFromFiles(files);
                        if (!string.IsNullOrEmpty(selected) && !IsSampleFile(selected) && !IsClutterFile(selected))
                        {
                            return selected;
                        }
                    }
                    catch
                    {
                        // Fallback
                    }
                }
            }

            return path;
        }

        return null;
    }

    public IEnumerable<string> FilterMediaFiles(IEnumerable<string> filePaths)
    {
        if (filePaths == null)
        {
            return Enumerable.Empty<string>();
        }

        var list = new List<(string Path, long Size)>();
        foreach (var file in filePaths)
        {
            if (string.IsNullOrWhiteSpace(file) || IsClutterFile(file) || !IsMediaFile(file))
            {
                continue;
            }

            long size = 0;
            try
            {
                if (File.Exists(file))
                {
                    size = new FileInfo(file).Length;
                }
            }
            catch
            {
                // Ignore
            }

            list.Add((file, size));
        }

        var nonSamples = list.Where(m => !IsSampleFile(m.Path, m.Size)).ToList();
        var final = nonSamples.Count > 0 ? nonSamples : list;
        return final.OrderByDescending(m => m.Size).Select(m => m.Path).ToList();
    }

    private string SelectFromFiles(IEnumerable<string> files)
    {
        var mediaFiles = new List<(string Path, long Size)>();
        foreach (var file in files)
        {
            if (IsClutterFile(file) || !IsMediaFile(file))
            {
                continue;
            }

            long size = 0;
            try
            {
                size = new FileInfo(file).Length;
            }
            catch
            {
                // Ignore
            }

            mediaFiles.Add((file, size));
        }

        if (mediaFiles.Count == 0)
        {
            return null;
        }

        var nonSampleMedia = mediaFiles.Where(m => !IsSampleFile(m.Path, m.Size)).ToList();
        var candidates = nonSampleMedia.Count > 0 ? nonSampleMedia : mediaFiles;

        return candidates.OrderByDescending(m => m.Size).FirstOrDefault().Path;
    }
}
