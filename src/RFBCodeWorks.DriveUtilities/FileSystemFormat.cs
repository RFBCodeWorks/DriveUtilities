/*
 * Robert Brenckman (c)2025
 *
 */ 
  
 
 

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable CA1416 // available on non-windows platforms

using System.Runtime.InteropServices;

namespace RFBCodeWorks.DriveUtilities
{
    /// <summary>
    /// File System formats supported by Windows Format Command
    /// </summary>
    public enum FileSystemFormat
    {
        /// <summary>
        /// The default format for most modern removable drives
        /// </summary>
        FAT32,

        /// <summary>
        /// "FAT" or "FAT16" - A legacy format for older removable drives, or drives smaller than 2GB
        /// <br/> "FAT" is not supported on drives larger than 2GB
        /// <br/> See <see href="https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/format"/> for more information.
        /// </summary>
        FAT,

        /// <summary>
        /// The default format for Windows system drives
        /// </summary>
        NTFS,

        /// <summary>
        /// <see cref="https://learn.microsoft.com/en-us/windows/win32/fileio/exfat-specification"/>
        /// </summary>
        exFAT,

        /// <summary>
        /// <see cref="https://learn.microsoft.com/en-us/windows-server/storage/refs/refs-overview"/>
        /// </summary>
        ReFS,

        /// <summary>
        /// Typically used for optical media
        /// </summary>
        UDF
    }
}
#pragma warning restore CA1416 // available on non-windows platforms
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member