# ChipsetAutoUpdater

[![ChipsetAutoUpdater Screenshot](https://raw.githubusercontent.com/realies/ChipsetAutoUpdater/master/app.png)](https://raw.githubusercontent.com/realies/ChipsetAutoUpdater/master/app.png)

A minimalistic utility for automatically updating AMD Ryzen chipset drivers.

## Features

- **Chipset Identification**: Determines the AMD chipset model the system is likely running on based on system information.

- **Automatic Version Checks**: Checks for the latest driver version every 6 hours, on application start, and when the app window is restored from minimised state.

- **Streamlined Installation**: One-click driver installation that downloads the latest version, initiates the installation process with appropriate permissions, and cleans up afterwards.

- **Configurable Startup**: Option to automatically start the application on system boot, running minimised in the system tray for unobtrusive operation.

- **Automated Updates**: When enabled, automatically installs new driver versions as they become available, ensuring your system always has the latest chipset drivers without manual intervention.

## Usage

1. Run the application
2. View detected chipset and versions
3. Click "Install Drivers" if an update is available

## Requirements

- Windows operating system
- AMD Ryzen chipset

## Notes

- Requires administrative privileges for installation
- Internet connection needed for version checks and downloads

## Uninstallation

To fully remove ChipsetAutoUpdater from your system, deselect the 'Start on boot' checkbox if it is selected, close the app and delete the ChipsetAutoUpdater.exe binary.
