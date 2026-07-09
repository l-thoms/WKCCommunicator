# WKC Communicator

[简体中文](./README_CN.md)

An Android-only and BLE-based application to control WKC devices. Written in .NET MAUI.

## Features

* Use the arrow keys to control the device
* Modify some basic device settings

## Notice

Communication protocol does not use BLE native pairing method for the convenience of mobile phone operation.

## TODO

- [ ] Add support for device firmware-specified shortcut and settings table.
- [ ] The next version of the protocol will stop reading settings entries directly from advertise data and will read the settings table from the characteristic instead.

## License

The project is under **GNU GPL v3.0**. Third-party components retain their respactive licenses.
* [Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json) → **MIT License**
* [Plugin.BLE](https://github.com/dotnet-bluetooth-le/dotnet-bluetooth-le) → **Apache License Version 2.0**