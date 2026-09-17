using System;
using System.IO;

namespace NzbDrone.Core.FileSystem;

public class FileSystemValidationService : IFileSystemValidationService
{
    private readonly Action<string, byte[]> _fileWriter;
    private readonly Action<string> _fileDeleter;
    private readonly Action<string> _directoryCreator;

    public FileSystemValidationService(
        Action<string, byte[]> fileWriter = null,
        Action<string> fileDeleter = null,
        Action<string> directoryCreator = null)
    {
        _fileWriter = fileWriter ?? File.WriteAllBytes;
        _fileDeleter = fileDeleter ?? File.Delete;
        _directoryCreator = directoryCreator ?? (p => Directory.CreateDirectory(p));
    }

    public FileSystemValidationResult ValidateDirectory(string path, bool testWrite = true)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new FileSystemValidationResult
            {
                IsValid = false,
                ErrorMessage = "Path cannot be empty or whitespace.",
            };
        }

        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return new FileSystemValidationResult
            {
                IsValid = false,
                ErrorMessage = "Path contains invalid characters: " + path,
            };
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            return new FileSystemValidationResult
            {
                IsValid = false,
                ErrorMessage = "Invalid directory path syntax: " + ex.Message,
            };
        }

        if (File.Exists(fullPath))
        {
            return new FileSystemValidationResult
            {
                IsValid = false,
                ErrorMessage = "Specified path is an existing file, not a directory: " + fullPath,
            };
        }

        try
        {
            if (!Directory.Exists(fullPath))
            {
                _directoryCreator(fullPath);
            }
        }
        catch (UnauthorizedAccessException)
        {
            return new FileSystemValidationResult
            {
                IsValid = false,
                ErrorMessage = "Permission denied: process lacks write permissions to " + path,
            };
        }
        catch (IOException)
        {
            return new FileSystemValidationResult
            {
                IsValid = false,
                ErrorMessage = "Permission denied: process lacks write permissions to " + path,
            };
        }
        catch (Exception ex)
        {
            return new FileSystemValidationResult
            {
                IsValid = false,
                ErrorMessage = "Failed to create directory: " + ex.Message,
            };
        }

        if (testWrite)
        {
            var testFileName = $".seedarr_write_test_{Guid.NewGuid():N}.tmp";
            var testFilePath = Path.Combine(fullPath, testFileName);

            try
            {
                _fileWriter(testFilePath, new byte[] { 0x53, 0x45, 0x45, 0x44 });
                _fileDeleter(testFilePath);
            }
            catch (UnauthorizedAccessException)
            {
                TryDeleteFile(testFilePath);
                return new FileSystemValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Permission denied: process lacks write permissions to " + path,
                };
            }
            catch (IOException)
            {
                TryDeleteFile(testFilePath);
                return new FileSystemValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Permission denied: process lacks write permissions to " + path,
                };
            }
            catch (Exception ex)
            {
                TryDeleteFile(testFilePath);
                return new FileSystemValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Failed to write to directory: " + ex.Message,
                };
            }
        }

        return new FileSystemValidationResult
        {
            IsValid = true,
            ResolvedPath = fullPath,
        };
    }

    private void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                _fileDeleter(filePath);
            }
        }
        catch
        {
            // Ignore failure to cleanup probe file
        }
    }
}
