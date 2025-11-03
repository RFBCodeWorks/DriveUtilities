using CommunityToolkit.Mvvm.Input;
using Microsoft.SqlServer.Server;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace RFBCodeWorks.DriveUtilities
{
    /// <summary>
    /// A viewmodel wrapper for <see cref="UsbDrive"/>
    /// </summary>
    public partial class UsbDriveViewModel(UsbDrive value) : INotifyPropertyChanged
    {
        public static UsbDriveViewModel[] GetUsbDriveViewModels() => System.IO.DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Removable).Select(d => new UsbDriveViewModel(new UsbDrive(d))).ToArray();

        public UsbDrive UsbDrive { get; } = value;

        private readonly bool _isRemovaable = value.IsRemovable();

        private ProcessLogger? _diagnostics;
        private ProcessLogger? _pLogger;
        
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler? Ejected;
        public event EventHandler? Dismounted;

        public ProcessLogger Diagnostics
        {
            get
            {
                if (_diagnostics is null)
                {
                    _diagnostics = new ProcessLogger();
                    UsbDrive.DiagnosticMessage += (sender, e) =>
                    {
                        _diagnostics.LogInfo(e);
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DiagnosticLog)));
                    };
                }
                return _diagnostics;
            }
        }
        public string DiagnosticLog => Diagnostics.GetInfoLog() ?? string.Empty;
        public string FormatLog => _pLogger?.GetInfoLog() ?? string.Empty;


        [RelayCommand(CanExecute = nameof(CanFormatDrive))]
        private async Task FormatDrive(FileSystemFormat format)
        {
            var result = MessageBox.Show("This will erase all data on the drive. Are you sure?", "Format Drive?", MessageBoxButton.YesNoCancel);
            if (result == MessageBoxResult.Yes)
            {
                _pLogger = new ProcessLogger();
                _pLogger.InfoReceived += PLogger_InfoReceived;
                await UsbDrive.Format(format, "", _pLogger, default);
                _pLogger.InfoReceived -= PLogger_InfoReceived;
                Notify();
            }
        }
        private bool CanFormatDrive(FileSystemFormat format)
        {
            return UsbDrive.IsMounted || UsbDrive.IsLocked && format >= 0 && format <= FileSystemFormat.UDF;
        }

        private bool IsMounted() => UsbDrive.IsMounted;

        [RelayCommand(CanExecute = nameof(CanLock))]
        private void Lock() { UsbDrive.Lock(); Notify(); }
        private bool CanLock() => UsbDrive.IsMounted && !UsbDrive.IsLocked;

        [RelayCommand(CanExecute = nameof(CanUnlock))]
        private void Unlock() { UsbDrive.Unlock(); Notify(); }
        private bool CanUnlock() => UsbDrive.IsMounted && UsbDrive.IsLocked;

        [RelayCommand(CanExecute = nameof(IsMounted))] private void ToggleExclusiveMode() { UsbDrive.SetExclusiveMode(!UsbDrive.IsExclusiveMode); Notify(); }
        

        private void PLogger_InfoReceived(object? sender, ProcessDataReceivedEventArgs e)=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FormatLog)));

        public void Notify()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UsbDrive)));
            this.DismountCommand?.NotifyCanExecuteChanged();
            this.EjectCommand?.NotifyCanExecuteChanged();
            this.FormatDriveCommand?.NotifyCanExecuteChanged();
            this.LockCommand?.NotifyCanExecuteChanged();
            this.ToggleExclusiveModeCommand?.NotifyCanExecuteChanged();
            this.UnlockCommand?.NotifyCanExecuteChanged();
        }

        [RelayCommand(CanExecute =nameof(CanEject))]
        public void Eject()
        {
            if (UsbDrive.Dismount())
                Dismounted?.Invoke(this, EventArgs.Empty);
            
            if (UsbDrive.Eject())
                Ejected?.Invoke(this, EventArgs.Empty);

            Notify();
        }
        private bool CanEject() => UsbDrive.IsDriveLetterValid(UsbDrive.DriveLetter);

        [RelayCommand(CanExecute =nameof(IsMounted))]
        public void Dismount()
        {
            if (UsbDrive.Dismount())
                Dismounted?.Invoke(this, EventArgs.Empty);

            Notify();
        }
    }
}
