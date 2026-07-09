# WKC Communicator

[English](./README.md)

基于 BLE 通信，用于控制 WKC 系列设备，使用 .NET MAUI 编写。只支持安卓手机。

## 功能

* 使用方向键控制设备
* 修改设备部分基本设置

## 使用提醒

* 考虑到手机端操作的便捷性，通信协议未使用 BLE 原生配对方法。

## 开发计划

- [ ] 新增对设备固件自定义的快捷操作与设置条目的支持。
- [ ] 下一版协议将取消直接从广播数据中读取设置条目，改为从特征值中读取设置列表。

## 许可证

本项目采用 **GNU GPL v3.0** 许可。第三方组件保留原始许可。
* [Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json)，采用 **MIT** 许可。
* [Plugin.BLE](https://github.com/dotnet-bluetooth-le/dotnet-bluetooth-le)，采用 **Apache License Version 2.0** 许可。