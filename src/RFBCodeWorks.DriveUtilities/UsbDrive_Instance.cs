/*
 * Robert Brenckman (c)2025
 * USB Drive Utiltiies
 * - Class allows locking/unlocking, ejection, and formatting the drive to various Windows-Supported formats
 * - Static methods provided to introduce the functionality without creating an instance of the class
 * - Instances of the class store a SafeHandle object, which can be used with other PInvoke calls on derived classes
 */

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Windows.Win32;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable CA1416 // available on non-windows platforms

namespace RFBCodeWorks.DriveUtilities
{
    /// <summary>
    /// This class wraps a <see cref="System.IO.DriveInfo"/> object to provide additional functionality for USB drives.
    /// <br/>Static methods are also available for use for dismounting and formatting drives without creating an instance of this class.
    /// <br/>Note that instances of this class should not be used concurrently across multiple threads.
    /// </summary>
    public partial class UsbDrive : IDisposable
    {
        [SupportedOSPlatform("windows")]
        public UsbDrive(DriveInfo driveInfo)
        {
            DriveInfo = driveInfo ?? throw new ArgumentNullException(nameof(driveInfo));
            Root = DriveInfo.Name;
            ThrowIfInvalidDriveChar(driveInfo.Name[0]); // validates drive letter
        }

        [SupportedOSPlatform("windows")]
        public UsbDrive(char driveLetter)
        {
            ThrowIfInvalidDriveChar(driveLetter);
            DriveInfo = new DriveInfo($"{Char.ToUpperInvariant(driveLetter)}:\\");
            Root = DriveInfo.Name;
        }

        [SupportedOSPlatform("windows")]
        public UsbDrive(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Input path must not be empty", nameof(path));
            path = Path.GetPathRoot(path)!;
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Input path must contain a valid root", nameof(path));

            if (Regex.IsMatch(path, "^[a-zA-Z]:\\\\$") is false)
                throw new ArgumentException("Input path must be in the format 'X:\\'", nameof(path));

            DriveInfo = new DriveInfo(path);
            Root = DriveInfo.Name;
        }

        /// <summary>
        /// An event that emits diagnostic messages, such as reporting when a lock fails due to an invalid handle.
        /// </summary>
        public EventHandler<string>? DiagnosticMessage;

        private void SendDiagnostic(string message) => DiagnosticMessage?.Invoke(this, message);

        /// <summary>
        /// Safe Handle used to hold onto the lock.
        /// When this is disposed, the lock is released.
        /// </summary>
        private SafeHandle? _handle;
        private string? _volumePath;
        private bool? _isMounted;
        private bool _isLocked;
        private bool _isExclusiveMode;
        private bool disposedValue;

        /// <summary>
        /// The SafeHandle used to access the drive
        /// </summary>
        protected SafeHandle Handle => _handle ??= GetHandle(false);
        private string VolumePath => _volumePath ??= DriveLetterToVolumePath(DriveLetter);

        /// <summary>
        /// The <see cref="DriveInfo"/> instance for the drive that this class wraps.
        /// </summary>
        public DriveInfo DriveInfo { get; }

        /// <summary>
        /// The first character of the drive letter (e.g. 'E' for "E:\")
        /// </summary>
        public char DriveLetter => Root[0];

        /// <summary>
        /// The root directory of the drive (e.g. "E:\") 
        /// </summary>
        /// <remarks><see cref="DriveInfo.Name"/></remarks>
        public string Root { get; }

        /// <summary>
        /// True if the <see cref="PreventRemoval(bool)"/> was set to true
        /// </summary>
        public bool IsRemovalPrevented { get; private set; }

        /// <summary>
        /// True if the drive root directory exists (i.e. the drive is mounted)
        /// </summary>
        /// <remarks>
        /// Cached result of <see cref="Directory.Exists(string?)"/>. Use <see cref="Refresh"/> to refresh the value.
        /// <br/>Note that when set to exclusive mode, <see cref="Directory.Exists(string?)"/> will return FALSE!
        /// </remarks>
        public bool IsMounted => _isMounted ??= Directory.Exists(Root);
        
        /// <summary>
        /// Value is set depending on the usage of <see cref="Lock(bool)"/> and <see cref="Unlock"/>
        /// </summary>
        public bool IsExclusiveMode => _isExclusiveMode;

        /// <summary>
        /// Returns <see langword="true"/> if the drive was locked using this class instance.
        /// </summary>
        public bool IsLocked => _isLocked;

        private static void ThrowIfDisposed(bool isDisposed)
        {
            if (isDisposed) ThrowDisposedException();
        }
        private static void ThrowDisposedException() => throw new ObjectDisposedException("UsbDrive instance has been disposed");

        /// <summary>
        /// Disposes of the internal handle used to access the volume.
        /// </summary>
        public void CloseHandle() => CloseHandle(false);
        private void CloseHandle(bool resetIsMounted)
        {
            _handle?.Close();
            _handle?.Dispose();
            _handle = null;
            _isLocked = false;
            _isExclusiveMode = false;
            if (resetIsMounted)
            {
                _isMounted = null;
            }
        }

        
        /// <summary>
        /// Resets the internal boolean states
        /// </summary>
        public void Refresh()
        {
            ThrowIfDisposed(disposedValue);
            _isMounted = null;
            if (_handle is null)
            {
                _isExclusiveMode = false;
                _isLocked = false;
            }
        }

        private bool IsHandleValid(SafeHandle handle, string messagePrefix)
        {
            if (handle.IsInvalid)
            {
                SendDiagnostic($"{messagePrefix}: Handle for {Root} is invalid.");
                return false;
            }
            if (handle.IsClosed)
            {
                SendDiagnostic($"{messagePrefix}: Handle for {Root} is closed.");
                return false;
            }
            return true;
        }


        /// <summary>
        /// Attempt to lock then dismount the volume.
        /// Note that this will not power off the device - use <see cref="Eject"/> for that.
        /// </summary>
        /// <remarks>
        /// If successful, closes the underlying handle.
        /// </remarks>
        /// <returns></returns>
        [SupportedOSPlatform("windows")]
        public bool Dismount()
        {
            ThrowIfDisposed(disposedValue);
            if (!IsMounted) return true;

            bool wasExclusive = IsExclusiveMode;
            bool wasLocked = IsLocked;
            if (SetExclusiveMode(true) && Lock())
            {
                if (DeviceIoControl(Handle, PInvoke.FSCTL_DISMOUNT_VOLUME))
                {
                    // success is expected after dismount
                    _isMounted = false;
                    SendDiagnostic($"Volume {Root} dismounted.");
                    if (PreventRemoval(false))
                    {
                        if (DeviceIoControl(Handle, PInvoke.IOCTL_STORAGE_EJECT_MEDIA))
                        {
                            SendDiagnostic($"Volume {Root} Ejected (IOCTL_STORAGE_EJECT_MEDIA).");
                            _isMounted = false;
                            return true;
                        }
                        else
                        {
                            SendDiagnostic($"Failed to eject volume {Root} (IOCTL_STORAGE_EJECT_MEDIA)");
                        }

                    }
                }
                else
                {
                    // dismount failed, so unlock and clear handle
                    SendDiagnostic($"Failed to dismount volume {Root} (FSCTL_DISMOUNT_VOLUME)");
                    _isMounted = null;
                    if (!wasLocked)
                    {
                        SetExclusiveMode(wasExclusive);
                        Unlock();
                    }
                }
            }
            return false;
        }

        /// <inheritdoc cref="Eject(char)"/>
        [SupportedOSPlatform("windows")]
        public bool Eject()
        {
            ThrowIfDisposed(disposedValue);
            bool wasDisposed = false;
            if (Dismount())
            {
                try
                {
                    if (Eject(DriveLetter, Handle, out wasDisposed, SendDiagnostic, true))
                    {
                        SendDiagnostic($"Volume {Root} Ejected (CM_Request_Device_Eject)");
                        return true;
                    }
                    else
                    {
                        SendDiagnostic($"Failed to eject volume {Root} (CM_Request_Device_Eject)");
                    }
                }
                finally
                {
                    if (wasDisposed) CloseHandle(true);
                }
            }
            return false;
        }

        /// <inheritdoc cref="FormatDrive"/>
        [SupportedOSPlatform("windows")]
        public Task Format(FileSystemFormat format, CancellationToken token)
        {
            return this.Format(format, null, null, token);
        }

        /// <inheritdoc cref="FormatDrive"/>
        [SupportedOSPlatform("windows")]
        public Task Format(FileSystemFormat format, string? volumeLabel, IProcessLogger? logger = null, CancellationToken token = default)
        {
            return this.Format(format, volumeLabel, true, 0, null, logger, token);
        }

        /// <inheritdoc cref="FormatDrive"/>
        [SupportedOSPlatform("windows")]
        public Task Format(FileSystemFormat format, string? volumeLabel = null, bool quickFormat = true, uint allocationUnitSize = 0, string? additionalArgs = null, IProcessLogger? logger = null, CancellationToken token = default)
        {
            CloseHandle(true);
            return FormatDrive(DriveLetter, format, quickFormat, volumeLabel, allocationUnitSize, additionalArgs, logger, token);
        }

        /// <summary>
        /// Updates the <see cref="_handle"/> field if necessary"/>
        /// </summary>
        /// <returns>the value of <see cref="_handle"/></returns>
        private SafeHandle GetHandle(bool exclusiveMode)
        {
            if (_handle is null || _handle.IsInvalid || exclusiveMode != _isExclusiveMode)
            {
                CloseHandle(false);
                _handle = OpenVolumeHandle(VolumePath, exclusiveMode);
                _isExclusiveMode = exclusiveMode;
                SendDiagnostic($"New handle for Volume {Root} opened in {(exclusiveMode ? "" : "non-")}exclusive mode");
            }
            return _handle;
        }

        /// <summary>
        /// Sets the internal handle to exclusive or shared mode.
        /// </summary>
        /// <param name="exclusiveMode"></param>
        /// <returns></returns>
        [SupportedOSPlatform("windows")]
        public bool SetExclusiveMode(bool exclusiveMode)
        {
            ThrowIfDisposed(disposedValue);
            if (_handle is not null && IsExclusiveMode == exclusiveMode) return true;
            bool wasLocked = IsLocked;
            bool wasRemovePrevented = IsRemovalPrevented;
            _handle = GetHandle(exclusiveMode);
            if (IsHandleValid(_handle, "Unable to Set Exclusive Mode"))
            {
                if (wasLocked && wasRemovePrevented)
                {
                    // reuse the storage for the bools since already in this return block, and the will not be read again
                    wasLocked = Lock();
                    wasRemovePrevented = PreventRemoval(true);
                    return wasLocked && wasRemovePrevented;
                }
                else if (wasLocked)
                {
                    return Lock();
                }
                else if (wasRemovePrevented)
                {
                    return PreventRemoval(true);
                }
                return true;
            }
            return false; // invalid handle
        }

        /// <summary>
        /// Attempts to lock the volume.
        /// </summary>
        /// <param name="exclusiveMode">
        /// When true, opens the volume in exclusive mode, preventing other processes from accessing it.
        /// When false, opens the volume in shared mode, allowing other processes to access it.
        /// </param>
        /// <returns><see langword="true"/> if successful, otherwise <see langword="false"/></returns>
        [SupportedOSPlatform("windows")]
        public bool Lock()
        {
            ThrowIfDisposed(disposedValue);
            if (IsLocked) return true;
            if (!IsHandleValid(Handle, "Unable to Lock Volume")) return false;
            var result = DeviceIoControl(Handle, PInvoke.FSCTL_LOCK_VOLUME);
            if (result)
            {
                SendDiagnostic($"Volume {Root} locked");
                _isLocked = true;
            }
            else
            {
                SendDiagnostic($"Failed to lock volume {Root}");
            }
                return result;
        }

        /// <summary>
        /// Sets the 'Removal Prevention' flag true or false.
        /// <br/>If the drive was not locked prior to calling this method, the flag will be set in non-exclusive mode.
        /// <br/>If exclusive mode is required, call <see cref="Lock(bool)"/> first.
        /// </summary>
        /// <remarks>
        /// Note that <see cref="Eject"/> and <see cref="Dismount"/> will automatically set this flag to false as part of those processes.
        /// </remarks>
        /// <returns><see langword="true"/> if the operation was successful, otherwise <see langword="false"/></returns>
        [SupportedOSPlatform("windows")]
        public bool PreventRemoval(bool state)
        {
            ThrowIfDisposed(disposedValue);
            if ((_handle is null && state == false) || !IsHandleValid(Handle, "Unable to Prevent Removal"))
            {
                IsRemovalPrevented = false;
                return !(_handle?.IsInvalid ?? true);
            }
            bool result = PreventRemoval(Handle, state);
            if (result)
            {
                IsRemovalPrevented = state;
            }
            return result;
        }

        /// <summary>
        /// Attempts to unlock the volume.
        /// </summary>
        /// <remarks>
        /// This will also close and dispose of the internal handle if it was used to lock the volume.
        /// </remarks>
        /// <returns><see langword="true"/> is successful, otherwise <see langword="false"/></returns>
        [SupportedOSPlatform("windows")]
        public bool Unlock()
        {
            ThrowIfDisposed(disposedValue);
            if (_handle is null || !IsLocked) return true;  // was never locked
            if (!IsHandleValid(Handle, "Unable to Unlock Volume")) return false;             // invalid handle
            if (DeviceIoControl(Handle, PInvoke.FSCTL_UNLOCK_VOLUME))
            {
                SendDiagnostic($"Volume {Root} unlocked.");
                _isLocked = false;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Checks the status of the drive
        /// </summary>
        /// <returns>
        /// Returns <see langword="true"/> if the drive can be ejected, otherwise returns <see langword="false"/>
        /// </returns>
        [SupportedOSPlatform("windows")]
        public bool IsRemovable()
        {
            if (DriveInfo.DriveType != DriveType.Removable) return false;
            if (_handle is null) return DriveInfo.IsReady;
            if (Handle.IsInvalid) return false;
            return IsExclusiveMode || DriveInfo.IsReady || IsLocked;
        }

        private void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    CloseHandle(true);
                }
                disposedValue = true;
            }
        }

        /// <summary>
        /// Disposes of the internal handle used to access the volume.
        /// </summary>
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        //// Leave at bottom of file to indicate in solution explorer that methods below this are static
        //~UsbDrive()
        //{
        //    Dispose(false);
        //}


    }
}
#pragma warning restore CA1416 // available on non-windows platforms
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member