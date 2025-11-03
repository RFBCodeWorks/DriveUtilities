/*
 * Robert Brenckman (c)2025
 * USB Drive Utiltiies
 * - Class allows locking/unlocking, ejection, and formatting the drive to various Windows-Supported formats
 * - Static methods provided to introduce the functionality without creating an instance of the class
 * - Instances of the class store a SafeHandle object, which can be used with other PInvoke calls on derived classes
 * - Utilizes CSWin32 for Source Code generation of Windows APIs
 */

#define SAFE_EJECT

//#define CSWIN32_0_3_238
//#define CSWIN32_0_3_236
#define CSWIN32_0_3_235

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.System.Ioctl;
using Marshal = System.Runtime.InteropServices.Marshal;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable CA1416 // available on non-windows platforms

namespace RFBCodeWorks.DriveUtilities
{
    /// <remarks>
    /// Relies on CSWin32 for library generation of PInvoke calls
    /// <br/>Adapted from the following : 
    /// <br/> - <see href="https://stackoverflow.com/questions/7704599/eject-usb-device-via-c-sharp"/>
    /// <br/> - <see href="https://www.codeproject.com/articles/How-to-Prepare-a-USB-Drive-for-Safe-Removal"/>
    /// <br/> - <see href="https://www.codeproject.com/articles/How-to-Prepare-a-USB-Drive-for-Safe-Removal-2"/>
    /// <para/>Note that the 'C' drive is not considered a valid drive letter for this class.
    /// </remarks>
    public partial class UsbDrive
    {
        public static UsbDrive[] GetUsbDrives() => System.IO.DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Removable).Select(d => new UsbDrive(d)).ToArray();

        /// <summary>
        /// Method to normalize DeviceIoControl calls without input or output buffers
        /// </summary>
        private static unsafe bool DeviceIoControl(SafeHandle handle, uint controlCode)
        {
#if CSWIN32_0_3_236 || CSWIN32_0_3_238
            return PInvoke.DeviceIoControl(handle, controlCode, null, null);
#elif CSWIN32_0_3_235
            return PInvoke.DeviceIoControl(handle, controlCode, null, 0u, null, 0u, null, null);
#endif
        }

        /// <inheritdoc cref="DismountVolume(string)"/>
        [SupportedOSPlatform("windows")]
        public static unsafe bool DismountVolume(char driveLetter) => DismountVolume(DriveLetterToVolumePath(driveLetter));

        /// <inheritdoc cref="DismountVolume(SafeHandle)"/>
        /// <param name="volumePath">Path in the following format : "\\.\X:"</param>
        private static unsafe bool DismountVolume(string volumePath)
        {
            using var handle = OpenVolumeHandle(volumePath, true); // open volume handle in exclusive mode
            try
            {
                DismountVolume(handle);
            }
            finally
            {
                handle.Close();
                handle.Dispose();
            }
            return false;
        }

        /// <summary>
        /// Performs the following operations:
        /// <br/>  - Locks the volume <see langword="FSCTL_LOCK_VOLUME"/>
        /// <br/>  - Dismounts the volume <see langword="FSCTL_DISMOUNT_VOLUME"/>
        /// <br/>  - Sets the 'Prevent Removal' flag to false <see langword="IOCTL_STORAGE_MEDIA_REMOVAL"/>
        /// <br/>  - Ejects media <see langword="IOCTL_STORAGE_EJECT_MEDIA"/>
        /// <br/>  - Unlocks the volume <see langword="FSCTL_UNLOCK_VOLUME"/>
        /// </summary>
        /// <param name="handle">The handle for the device. Handle should be in exclusive mode.</param>
        /// <returns><see langword="true"/> if all steps complete successfully, otherwise <see langword="false"/></returns>
        private static unsafe bool DismountVolume(SafeHandle handle, bool unlockOnFailure = true)
        {
            if (handle is null || handle.IsInvalid) return false;
            if (DeviceIoControl(handle, PInvoke.FSCTL_LOCK_VOLUME))
            {
                try
                {
                    if (DeviceIoControl(handle, PInvoke.FSCTL_DISMOUNT_VOLUME))
                    {
                        unlockOnFailure = true; // ensure we unlock the volume since it was successfully dismounted
                        if (PreventRemoval(handle, false))
                        {
                            if (DeviceIoControl(handle, PInvoke.IOCTL_STORAGE_EJECT_MEDIA))
                            {
                                return true;
                            }
                        }
                    }
                }
                finally
                {
                    // unlock the volume
                    if (unlockOnFailure)
                    {
                        DeviceIoControl(handle, PInvoke.FSCTL_UNLOCK_VOLUME);
                    }
                }
            }
            return false;
        }

        private static string DriveLetterToVolumePath(char driveLetter)
        {
            ThrowIfInvalidDriveChar(driveLetter);
            return $@"\\.\{Char.ToUpperInvariant(driveLetter)}:";
        }

        public static bool IsDriveLetterValid(char driveLetter)
        {
            return (driveLetter == 'c' || driveLetter == 'C' || driveLetter >= 'A' && driveLetter <= 'Z' || driveLetter >= 'a' && driveLetter <= 'z');
        }

        /// <summary>
        /// Check if a drive letter directory is located on a  removable drive.
        /// </summary>
        /// <param name="rootDir">The directory root.  F:\</param>
        /// <returns>True if the drive is flagged as removable, otherwise false.</returns>
        [SupportedOSPlatform("windows")]
        public static bool IsRemovableDrive(char driveLetter)
        {
            ThrowIfInvalidDriveChar(driveLetter);
            return PInvoke.GetDriveType($"{Char.ToUpperInvariant(driveLetter)}:\\") == PInvoke.DRIVE_REMOVABLE;
        }

        /// <summary>
        /// Check if a specified root directory is located on a  removable drive.
        /// </summary>
        /// <param name="rootDir">The directory root.  F:\</param>
        /// <returns>True if the drive is flagged as removable, otherwise false.</returns>
        [SupportedOSPlatform("windows")]
        public static bool IsRemovableDrive(string rootDir)
        {
            ThrowIfPathIsNotValid(rootDir, nameof(rootDir));
            return PInvoke.GetDriveType(rootDir) == PInvoke.DRIVE_REMOVABLE;
        }

        /// <param name="removableDrive">The drive to remove</param>
        /// <inheritdoc cref="Eject(char)"/>
        [SupportedOSPlatform("windows")]
        public static bool Eject(DriveInfo removableDrive)
        {
            if (removableDrive.DriveType == DriveType.Removable)
            {
                return Eject(removableDrive.RootDirectory.Name[0]);
            }
            return false;
        }

        /// <param name="driveRoot">
        /// The root of a drive. example "F:\\".
        /// <br/> Only the first character is used. (Accepts "D" , "D:\", "D:\\" etc.)
        /// </param>
        /// <inheritdoc cref="Eject(char)"/>
        [SupportedOSPlatform("windows")]
        public static bool Eject(string driveRoot)
        {
            ThrowIfPathIsNotValid(driveRoot, nameof(driveRoot));
            return Eject(driveRoot.TrimStart()[0]);
        }

        /// <summary>
        /// Attempts to eject a drive specified by the drive letter
        /// </summary>
        /// <param name="driveLetter">a-Z</param>
        /// <returns><see langword="true"/> if successfully ejected, otherwise <see langword="false"/></returns>
        /// <exception cref="ArgumentException">Thrown if the <paramref name="driveLetter"/> is invalid or the drive is not removable.</exception>"
        /// https://www.codeproject.com/articles/How-to-Prepare-a-USB-Drive-for-Safe-Removal
        /// https://www.codeproject.com/articles/How-to-Prepare-a-USB-Drive-for-Safe-Removal-2
        [SupportedOSPlatform("windows")]
        public static unsafe bool Eject(char driveLetter)
        {
            driveLetter = Char.ToUpperInvariant(driveLetter);
            ThrowIfDriveIsNotRemovable(driveLetter);
            return Eject(driveLetter, null, out _, null);
        }

        private static unsafe bool Eject(char driveLetter, SafeHandle? safeHandle, out bool wasDisposed, Action<string>? diagnostic, bool dismountedAlready = false )
        {
            ThrowIfInvalidDriveChar(driveLetter);
            driveLetter = Char.ToUpperInvariant(driveLetter);
            wasDisposed = false;
            if (TryGetStorageDeviceNumber(DriveLetterToVolumePath(driveLetter), out var deviceNumber))
            {
                if (TryGetDeviceInstanceID(deviceNumber, out uint deviceInstanceID))
                {
                    bool dismounted = dismountedAlready || (safeHandle is null ? DismountVolume(driveLetter) : DismountVolume(safeHandle, false));
                    if (dismounted)
                    {
                        safeHandle?.Dispose();
                        wasDisposed = true;
                        // The first parent must be ejected - otherwise it attempt to dismount the volume only and subsequent attempts fail
                        var cr = PInvoke.CM_Get_Parent(out var parentDevId, deviceInstanceID, 0u);
                        if (cr == CONFIGRET.CR_SUCCESS)
                        {
#if DEBUG
                            Span<char> buffer = new char[PInvoke.MAX_PATH];
                            PNP_VETO_TYPE failureReason = default;
#if CSWIN32_0_3_235
                            cr = PInvoke.CM_Request_Device_Eject((uint)parentDevId, &failureReason, buffer, 0u);
#else
                            cr = PInvoke.CM_Request_Device_Eject((uint)parentDevId, out failureReason, buffer, 0u);
#endif
#else
#if CSWIN32_0_3_235
                            cr = PInvoke.CM_Request_Device_Eject((uint)parentDevId, null, null, 0u);
#else
                            cr = PInvoke.CM_Request_Device_Eject((uint)parentDevId, null, 0u);
#endif
#endif
                        }
                        if (cr == CONFIGRET.CR_SUCCESS)
                            return true;

                        var errorCode = (int)PInvoke.CM_MapCrToWin32Err(cr, default);
                        Marshal.ThrowExceptionForHR(errorCode);
                        Marshal.ThrowExceptionForHR(Marshal.GetLastWin32Error());
                    }
                    else
                    {
                        diagnostic?.Invoke("Dismount Failed");
                    }
                }
                else
                {
                    diagnostic?.Invoke("Failed to get Device Instance ID");
                }
            }
            else
            {
                diagnostic?.Invoke("Failed to get Storage Device Number");
            }
            return false;
        }

        /// <summary>
        /// Run a format operation on the specified drive letter.
        /// </summary>
        /// <param name="driveLetter">The drive letter to format</param>
        /// <param name="format">One of the compatible File System Formats</param>
        /// <param name="quickFormat">perform a Quick-Format (default true)</param>
        /// <param name="volumeLabel">The 11-character volume label to apply</param>
        /// <param name="allocationUnitSize">The allocation unit size to use. 0 for default.</param>
        /// <param name="additionalArgs">
        /// Additional arguments to apply to the format operation. Use at own risk.
        /// <br/> Refer to <see href="https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/format"/> for more information.
        /// </param>
        /// <param name="logger">An optional logger to capture the output of the <see cref="System.Diagnostics.Process"/> that runs the format</param>
        /// <param name="token">The cancellation token</param>
        /// <returns>
        /// A task that completes when the format operation has completed.
        /// </returns>
        /// <remarks>
        /// Note that this method requires elevated privileges to run successfully.
        /// </remarks>
        /// <exception cref="ArgumentException">Thrown when an invalid drive character is provided, invalid format, or invalid volume label.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the process fails to format the drive.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
        [SupportedOSPlatform("windows")]
        public static async Task FormatDrive(char driveLetter, FileSystemFormat format, bool quickFormat = true, string? volumeLabel = "", uint allocationUnitSize = 0, string? additionalArgs = "", IProcessLogger? logger = null, CancellationToken token = default)
        {
            ThrowIfInvalidDriveChar(driveLetter);
            driveLetter = Char.ToUpperInvariant(driveLetter);

            string formatString = format switch
            {
                FileSystemFormat.FAT32 => "FAT32",
                FileSystemFormat.FAT => "FAT",
                FileSystemFormat.NTFS => "NTFS",
                FileSystemFormat.ReFS => "ReFS",
                FileSystemFormat.exFAT => "exFAT",
                FileSystemFormat.UDF => "UDF",
                _ => throw new ArgumentException("Unsupported File System Format", nameof(format)),
            };

            if (string.IsNullOrEmpty(volumeLabel) is false)
            {
                if (volumeLabel!.Length > 11 && (format == FileSystemFormat.FAT || format == FileSystemFormat.FAT32))
                {
                    throw new ArgumentException("Volume label for FAT16 and FAT32 file systems must not exceed 11 characters.", nameof(volumeLabel));
                }
                volumeLabel = $"/V:{volumeLabel}";
            }
            else
            {
                volumeLabel = string.Empty;
            }
            string unitSizeArg = allocationUnitSize > 0 ? $"/A:{allocationUnitSize} " : string.Empty;

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "format.com",
                Arguments = $"/FS:{formatString} {(quickFormat ? "/Q" : string.Empty)} {unitSizeArg} {volumeLabel} /Y {driveLetter}:",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = new System.Diagnostics.Process() { StartInfo = psi };
            DataReceivedEventHandler? outputHandler = null, errorHandler = null;
            bool processStarted = false;
            try
            {
                if (logger is not null)
                {
                    // subscribe event handlers
                    process.EnableRaisingEvents = true;
                    outputHandler = (sender, e) =>
                    {
                        logger.LogInfo(e.Data ?? string.Empty);
                    };
                    errorHandler = (sender, e) =>
                    {
                        logger.LogError(e.Data ?? string.Empty);
                    };
                    process.OutputDataReceived += outputHandler;
                    process.ErrorDataReceived += errorHandler;
                    logger.LogInfo($"Starting format process: {psi.FileName} {psi.Arguments}");
                }

                // last chance to throw before it starts
                token.ThrowIfCancellationRequested();
                process.Start();
                processStarted = true;
                if (logger is not null)
                {
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                }

#if NETFRAMEWORK
                await Task.Run(process.WaitForExit, token).ConfigureAwait(false);
#else
                await process.WaitForExitAsync(token).ConfigureAwait(false);
#endif

                if (token.IsCancellationRequested && !process.HasExited)
                {
                    try { process.Kill(); } catch { }
                }
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"Format process exited with code {process.ExitCode}");
                }
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (processStarted && !process.HasExited)
                    {
                        process.Kill();
                    }
                }
                catch { }
                throw; // re-throw cancellation to caller
            }
            finally
            {
                // unsubscribe event handlers
                if (logger is not null)
                {
                    process.CancelErrorRead();
                    process.CancelOutputRead();
                    process.OutputDataReceived -= outputHandler;
                    process.ErrorDataReceived -= errorHandler;
                }
                process.Close();
                process.Dispose();
            }
        }

        internal const uint DefaultVolumeAccess = (uint)(GENERIC_ACCESS_RIGHTS.GENERIC_READ | GENERIC_ACCESS_RIGHTS.GENERIC_WRITE);

        /// <summary>
        /// Open a volume handle for the given volume path.
        /// </summary>
        /// <param name="volumePath">volume path in the following format <br/> \\.\X:</param>
        /// <param name="exclusiveMode">
        /// specifying the share mode for the volume handle.
        /// <br/><see cref="FILE_SHARE_MODE.FILE_SHARE_NONE"/> is used for exclusive access to the device
        /// </param>
        /// <param name="dwDesiredAccess">
        /// This parameter specifies the type of access to the volume. 
        /// <br/> Lock/Unlock and Dismount operations require both read and write access.
        /// <br/> Information Only can supply 0u for this parameter.
        /// </param>
        /// <returns>A <see cref="SafeHandle"/></returns>
        private static Microsoft.Win32.SafeHandles.SafeFileHandle OpenVolumeHandle(string volumePath, bool exclusiveMode = true, uint dwDesiredAccess = DefaultVolumeAccess)
        {
            return PInvoke.CreateFile(
                    lpFileName: volumePath,
                    dwDesiredAccess: dwDesiredAccess,
                    dwShareMode: (exclusiveMode ? FILE_SHARE_MODE.FILE_SHARE_NONE : FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE),
                    lpSecurityAttributes: null,
                    dwCreationDisposition: FILE_CREATION_DISPOSITION.OPEN_EXISTING,
                    dwFlagsAndAttributes: FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_DEVICE,
                    hTemplateFile: null);
        }

        /// <summary>
        /// Sets or Unsets the 'Prevent Removal' flag
        /// </summary>
        private static unsafe bool PreventRemoval(SafeHandle handle, bool prevent)
        {
            var pmr = new PREVENT_MEDIA_REMOVAL() { PreventMediaRemoval = prevent };
            bool success;
#if CSWIN32_0_3_236 || CSWIN32_0_3_238
            ReadOnlySpan<byte> inputBuffer = new(&pmr, Marshal.SizeOf<PREVENT_MEDIA_REMOVAL>());
            success = PInvoke.DeviceIoControl(handle, PInvoke.IOCTL_STORAGE_MEDIA_REMOVAL, inputBuffer, null);
#elif CSWIN32_0_3_235
            // !! Overload was removed in 0_3_3256
            success = PInvoke.DeviceIoControl(handle, PInvoke.IOCTL_STORAGE_MEDIA_REMOVAL, &pmr, (uint)Marshal.SizeOf<PREVENT_MEDIA_REMOVAL>(), null, 0u, null, null);
#endif
            return success;
        }

        private static void ThrowRootNotMounted(string root) => throw new DirectoryNotFoundException($"Volume {root} is not mounted.");
        private static void ThrowIfInvalidDriveChar(char driveLetter)
        {
            if (!IsDriveLetterValid(driveLetter))
            {
                Throw(driveLetter);
            }
            static void Throw(char letter) => throw new ArgumentException($"Invalid Drive Letter - Expected A-Z. Received '{letter}'", "driveLetter");
        }
        private static void ThrowIfDriveIsNotRemovable(char driveLetter)
        {
            if (IsRemovableDrive(driveLetter) is false)
            {
                Throw(driveLetter);
            }
            static void Throw(char letter) =>  throw new ArgumentException($"Drive '{letter}:\\' is not a Removable Drive.", "driveLetter");
        }
        private static void ThrowIfStringIsNullOrWhiteSpace(string input, string paramName)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                Throw(paramName);
            }
            static void Throw(string paramName) => throw new ArgumentException("Input must not be empty", paramName);
        }
        private static void ThrowIfPathIsNotValid(string input, string paramName)
        {
            ThrowIfStringIsNullOrWhiteSpace(input, paramName);
            if (Path.IsPathRooted(input) is false)
            {
                Throw(paramName);
            }
            static void Throw(string paramName) => throw new ArgumentException("Input path must contain a valid root", paramName);
        }

        /// <summary>
        /// Attempt to get the storage device number for a given volume handle.
        /// </summary>
        /// <returns><see langword="true"/> if successfull, otherwise <see langword="false"/></returns>
        private static unsafe bool TryGetStorageDeviceNumber(SafeHandle handle, out STORAGE_DEVICE_NUMBER result)
        {
            var sdn = new STORAGE_DEVICE_NUMBER();
#if CSWIN32_0_3_236 ||CSWIN32_0_3_238
            Span<byte> outBuffer = new(&sdn, Marshal.SizeOf<PREVENT_MEDIA_REMOVAL>());
            var success = PInvoke.DeviceIoControl(handle, PInvoke.IOCTL_STORAGE_GET_DEVICE_NUMBER, null, outBuffer);
#elif CSWIN32_0_3_235
            Span<byte> outBuffer = new(&sdn, Marshal.SizeOf<PREVENT_MEDIA_REMOVAL>());
            uint bytesReturned = 0;
            var success = PInvoke.DeviceIoControl(handle, PInvoke.IOCTL_STORAGE_GET_DEVICE_NUMBER, null, 0u, &sdn, (uint)Marshal.SizeOf<STORAGE_DEVICE_NUMBER>(), &bytesReturned, null);
#endif
            result = sdn;
            return success;
        }

        /// <inheritdoc cref="TryGetStorageDeviceNumber(SafeHandle, out STORAGE_DEVICE_NUMBER)">
        private static unsafe bool TryGetStorageDeviceNumber(string volumePath, out STORAGE_DEVICE_NUMBER result)
        {
            using var handle = OpenVolumeHandle(volumePath, false, 0u);
            bool success = TryGetStorageDeviceNumber(handle, out result);
            handle.Close();
            handle.Dispose();
            return success;
        }

        /// <summary>
        /// Iterate the DISK devices on the system to find the device instance ID that matches the provided <paramref name="storageDeviceNumber"/>
        /// </summary>
        /// <param name="storageDeviceNumber">The <see cref="STORAGE_DEVICE_NUMBER"/> returned from <see cref="TryGetStorageDeviceNumber(SafeHandle, out STORAGE_DEVICE_NUMBER)"/></param>
        /// <param name="deviceInstanceID">
        /// When this method returns <see langword="true"/>, this will contain the device instance ID of the device that matches the provided <paramref name="storageDeviceNumber"/>
        /// <br/> When this method returns <see langword="false"/>, this will be <see cref="uint.MaxValue"/>.
        /// </param>
        /// <returns><see langword="true"/> if a matching disk device was found, otherwise false.</returns>
        private static unsafe bool TryGetDeviceInstanceID(STORAGE_DEVICE_NUMBER storageDeviceNumber, out uint deviceInstanceID)
        {
            deviceInstanceID = uint.MaxValue;
            Guid diskGuid = PInvoke.GUID_DEVINTERFACE_DISK; // this class only cares about disk devices

            // Get device interface info set handle for all devices attached to system
            var hDevInfo = PInvoke.SetupDiGetClassDevs(
                ClassGuid: &diskGuid,
                Enumerator: null, hwndParent: HWND.Null,
                Flags: SETUP_DI_GET_CLASS_DEVS_FLAGS.DIGCF_PRESENT | SETUP_DI_GET_CLASS_DEVS_FLAGS.DIGCF_DEVICEINTERFACE);

            if (hDevInfo.IsNull)
            {
                PInvoke.SetupDiDestroyDeviceInfoList(hDevInfo);
                return false;
            }

            try
            {
                // Prepare interface data
                SP_DEVINFO_DATA devInfoData = new SP_DEVINFO_DATA();
                devInfoData.cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>();

                SP_DEVICE_INTERFACE_DATA devInterfaceData = new SP_DEVICE_INTERFACE_DATA();
                devInterfaceData.cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>();

                uint sizeOf_SDN = (uint)Marshal.SizeOf<STORAGE_DEVICE_NUMBER>();
                uint sizeOf_DeviceDetails = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DETAIL_DATA_W>();
                uint index = 0;

                while(true)
                {
                    // Get device interface data
                    if (!PInvoke.SetupDiEnumDeviceInterfaces(hDevInfo, null, &diskGuid, index++, &devInterfaceData))
                    {
                        // returns false if no more interfaces
                        int err = Marshal.GetLastWin32Error();
                        if (err == (int)WIN32_ERROR.ERROR_NO_MORE_ITEMS)
                            break;

                        Marshal.ThrowExceptionForHR(err);
                        break;
                    }

                    // Obtain required size for SP_DEVICE_INTERFACE_DETAIL_DATA_W
                    // expected to return false with ERROR_INSUFFICIENT_BUFFER
                    uint requiredSize = 0;
                    PInvoke.SetupDiGetDeviceInterfaceDetail(hDevInfo, &devInterfaceData, null, 0, &requiredSize, null);

                    // Call again to get the device interface details
                    SP_DEVICE_INTERFACE_DETAIL_DATA_W* detailData = (SP_DEVICE_INTERFACE_DETAIL_DATA_W*)Marshal.AllocHGlobal((int)requiredSize);
                    detailData->cbSize =sizeOf_DeviceDetails;
                    try
                    {
                        if (!PInvoke.SetupDiGetDeviceInterfaceDetail(hDevInfo, &devInterfaceData, detailData, requiredSize, &requiredSize, &devInfoData))
                        {
                            Marshal.ThrowExceptionForHR(Marshal.GetLastWin32Error());
                            continue;
                        }

                        if (detailData->cbSize == 0u)
                            continue;

                        // Get the device path by converting the fixed char array to string
                        string devicePath;
                        fixed(char* p = &detailData->DevicePath.e0)
                            devicePath = new string(p);
                        
                        if (string.IsNullOrWhiteSpace(devicePath)) 
                            continue;

                        // Open the physical device in non-exclusive mode
                        using SafeHandle hDevice =  OpenVolumeHandle(devicePath, false, 0u);
                        if (hDevice == null || hDevice.IsInvalid)
                        {
                            hDevice?.Close();
                            hDevice?.Dispose();
                            continue;
                        }

                        // Query device number from physical device
                        STORAGE_DEVICE_NUMBER sdn = new();
                        bool success;
#if CSWIN32_0_3_236 || CSWIN32_0_3_238
                        Span<byte> outBuffer = new(&sdn, (int)sizeOf_SDN);
                        success =  PInvoke.DeviceIoControl(hDevice, PInvoke.IOCTL_STORAGE_GET_DEVICE_NUMBER, null, outBuffer);
#elif CSWIN32_0_3_235
                        uint bytesReturned = 0;
                        success = PInvoke.DeviceIoControl(hDevice, PInvoke.IOCTL_STORAGE_GET_DEVICE_NUMBER, null, 0u, &sdn, sizeOf_SDN, &bytesReturned, null);
#endif

                        if (!success)
                        {
                            Marshal.ThrowExceptionForHR(Marshal.GetLastWin32Error());
                            continue;
                        }
                        if (sdn.DeviceNumber == storageDeviceNumber.DeviceNumber)
                        {
                            deviceInstanceID = devInfoData.DevInst;
                            return true;
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal((nint)detailData);
                    }
                }
            }
            finally
            {
                PInvoke.SetupDiDestroyDeviceInfoList(hDevInfo);
            }
            return false;
        }

        /// <summary>
        /// Get the DOS Device Name
        /// </summary>
        /// <param name="driveLetter"></param>
        /// <returns></returns>
        private static unsafe string GetDosDeviceName(char driveLetter)
        {
            // get the dos device name
            int bufferSize = (int)PInvoke.MAX_PATH;
            Span<char> buffer = new char[bufferSize];
            uint charCount = 0;
            charCount = PInvoke.QueryDosDevice($"{driveLetter}:", buffer);

            while (charCount > 0 && charCount < bufferSize && buffer[(int)charCount - 1] == '\0')
                charCount--;

            if (charCount == 0) return string.Empty;

#if NETFRAMEWORK
            return new string(buffer.Slice(0, (int)charCount).ToArray()).Trim();
#else
            return new string(buffer[..(int)charCount]);
#endif
        }


    }
}
#pragma warning restore CA1416 // available on non-windows platforms
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member