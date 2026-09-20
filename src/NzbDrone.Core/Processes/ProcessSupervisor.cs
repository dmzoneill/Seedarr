using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using NLog;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Processes;

public class ProcessSupervisor : ISidecarProcessSupervisor, IHandle<ApplicationShutdownRequested>
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    private readonly ConcurrentDictionary<int, Process> _processes = new();
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private volatile bool _isShuttingDown;
    private bool _disposed;
    private IntPtr _jobHandle = IntPtr.Zero;

    public ProcessSupervisor()
    {
        InitializeWindowsJobObject();
    }

    public IReadOnlyCollection<int> ActiveProcessIds => _processes.Keys.ToArray();

    public bool IsShuttingDown => _isShuttingDown;

    public void RegisterProcess(Process process)
    {
        if (process == null || _disposed || _isShuttingDown)
        {
            return;
        }

        try
        {
            if (process.HasExited)
            {
                return;
            }

            var pid = process.Id;
            if (_processes.TryAdd(pid, process))
            {
                _logger.Debug("Registered child process {0}", pid);

                try
                {
                    process.EnableRaisingEvents = true;
                    process.Exited += (sender, args) =>
                    {
                        UnregisterProcess(pid);
                    };
                }
                catch (Exception ex)
                {
                    _logger.Trace(ex, "Could not attach Exited event to process {0}", pid);
                }

                try
                {
                    if (process.HasExited)
                    {
                        UnregisterProcess(pid);
                    }
                }
                catch
                {
                }

                AssignToWindowsJob(process, pid);
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to register child process");
        }
    }

    public void UnregisterProcess(int pid)
    {
        if (_processes.TryRemove(pid, out _))
        {
            _logger.Debug("Unregistered child process {0}", pid);
        }
    }

    public void Handle(ApplicationShutdownRequested message)
    {
        _isShuttingDown = true;
        _logger.Info("Application shutdown requested: terminating all registered child processes");
        TerminateAll();
    }

    public void TerminateAll(TimeSpan? timeout = null)
    {
        var killTimeout = timeout ?? TimeSpan.FromSeconds(5);
        var pids = _processes.Keys.ToArray();
        if (pids.Length == 0)
        {
            return;
        }

        _logger.Info("Terminating {0} active sidecar child processes...", pids.Length);

        foreach (var pid in pids)
        {
            if (_processes.TryRemove(pid, out var process))
            {
                try
                {
                    if (!process.HasExited)
                    {
                        _logger.Debug("Terminating child process {0} (entire tree)", pid);
                        try
                        {
                            process.Kill(entireProcessTree: true);
                        }
                        catch (Exception ex)
                        {
                            _logger.Debug(ex, "Failed to kill process tree for PID {0}", pid);
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
                                killProc?.WaitForExit(500);
                            }
                            catch
                            {
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
                                taskkillProc?.WaitForExit(500);
                            }
                            catch
                            {
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Error while killing process {0}", pid);
                }
                finally
                {
                    try
                    {
                        var waitMs = (int)Math.Min(1000, killTimeout.TotalMilliseconds);
                        process.WaitForExit(waitMs);
                    }
                    catch
                    {
                    }
                }
            }
        }

        _processes.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _isShuttingDown = true;
        TerminateAll(TimeSpan.FromSeconds(2));

        if (OperatingSystem.IsWindows() && _jobHandle != IntPtr.Zero)
        {
            try
            {
                CloseHandle(_jobHandle);
                _jobHandle = IntPtr.Zero;
            }
            catch
            {
            }
        }
    }

    private void InitializeWindowsJobObject()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            _jobHandle = CreateJobObject(IntPtr.Zero, null);
            if (_jobHandle != IntPtr.Zero)
            {
                var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
                {
                    BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                    {
                        LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
                    },
                };

                var length = (uint)Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
                var pInfo = Marshal.AllocHGlobal((int)length);
                try
                {
                    Marshal.StructureToPtr(info, pInfo, false);
                    if (!SetInformationJobObject(_jobHandle, JobObjectExtendedLimitInformation, pInfo, length))
                    {
                        _logger.Debug("Failed to set JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE on Windows job object");
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(pInfo);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to initialize Windows Job Object for ProcessSupervisor");
        }
    }

    private void AssignToWindowsJob(Process process, int pid)
    {
        if (!OperatingSystem.IsWindows() || _jobHandle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                AssignProcessToJobObject(_jobHandle, process.Handle);
            }
        }
        catch (Exception ex)
        {
            _logger.Trace(ex, "Could not assign process {0} to Windows Job Object", pid);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryLimit;
        public UIntPtr PeakJobMemoryLimit;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IntPtr hJob, int jobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);
}
