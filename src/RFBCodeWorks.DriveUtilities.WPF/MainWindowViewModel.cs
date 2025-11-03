using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using RFBCodeWorks.DriveUtilities;

namespace RFBCodeWorks.DriveUtilities.WPF
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private FileSystemFormat _selectedFormat;

        [ObservableProperty]
        private UsbDriveViewModel[]? drives;

        [NotifyCanExecuteChangedFor(nameof(OpenWindowsExplorerCommand))]
        [ObservableProperty]
        private UsbDriveViewModel? selectedDrive;

        public RFBCodeWorks.DriveUtilities.FileSystemFormat[] FileSystemFormats { get; } = [ RFBCodeWorks.DriveUtilities.FileSystemFormat.FAT32, FileSystemFormat.FAT, FileSystemFormat.NTFS, FileSystemFormat.exFAT, FileSystemFormat.ReFS, FileSystemFormat.UDF ];

        public FileSystemFormat SelectedFormat
        {
            get => _selectedFormat;
            set
            {
                _selectedFormat = value;
                OnPropertyChanged(nameof(SelectedFormat));
                SelectedDrive?.Notify();
            }
        }

        /// <summary>
        /// Refresh the Drives array
        /// </summary>
        [RelayCommand]
        public void RefreshDrives()
        {
            Drives = UsbDriveViewModel.GetUsbDriveViewModels();
            SelectedDrive = Drives.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedDrive));
        }

        partial void OnSelectedDriveChanged(UsbDriveViewModel? oldValue, UsbDriveViewModel? newValue)
        {
            if (oldValue is not null) oldValue.Ejected -= OnEjected;
            if (newValue is not null) newValue.Ejected += OnEjected;
        }

        private void OnEjected(object sender, EventArgs e) => RefreshDrives();


        [RelayCommand(CanExecute =nameof(CanOpenWindowsExplorer))]
        private void OpenWindowsExplorer() => Process.Start(new ProcessStartInfo() { FileName = SelectedDrive!.UsbDrive.Root });
        private bool CanOpenWindowsExplorer() => SelectedDrive != null;
    }
}
