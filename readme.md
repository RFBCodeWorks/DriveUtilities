## RFBCodeWorks.RFBCodeWorks.DriveUtilities

This library was created out of a need to format and eject USB drives from a WPF Application. This uses the CSWin32 source generator to generate the required windows APIs.

Notes : 
- CSWin32 will not source generate required APIs for 'anyCPU', so only x86 and x64 platforms are targeted currently.

This library provides the following objects:

- **RFBCodeWorks.DriveUtilities.UsbDrive**  
	- **Static methods:**
		- bool Eject(char driveLetter)
		- bool Eject(string driveRoot)
		- bool Eject(DriveInfo drive)
		- bool DismountVolume(char driveLetter)
		- bool IsDriveLetterValid(char driveLetter)
		- Task FormatDrive
			- Several overloads
	- **Instance**
		- IDisposable Instance that can be used to open a SafeHandle for a specified volume to allow Locking/unlocking, safe removal prevention, etc. 
		- Wraps  'System.IO.DriveInfo' with additional functionality.
			- Lock()
			- CloseHandle()
			- Dismount()
			- Eject()
			- IsRemovable()
			- Format() -- several overloads
			- SetExclusiveMode(bool)
			- PreventRemoval(bool)
			- Unlock()
- **FileSystemFormat**
	- enum for formats supported by format.com windows utility
- **IProcessLogger**
	- Basic logger implementations that can be passed to FormatDrive to get the output of the format process. This avoids a dependency on the ILogger nuget package.
	- **Implementations :** 
		- **ProcessLogger** (raises events and logs to a stringbuilder)
		- **ProcessProxyLogger** (use this to wrap ILogger)


