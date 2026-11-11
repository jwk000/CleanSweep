净界 CleanSweep 1.0.0
========================================

可解释、可追溯的 Windows 清理工具。

系统要求
--------
Windows 10 1903+ / Windows 11
系统自带 .NET Framework 4.8（无需安装运行时）

使用
----
1. 双击 CleanSweep.exe。
2. 点击“开始扫描”；扫描阶段只读，不会修改文件。
3. 进入分类，点“依据”查看完整路径、时间条件、哈希或注册表证据。
4. 勾选需要处理的项目，点击“清理选中项”。
5. 清理完成后查看报告；完整日志保留在本机。

需要清理 C:\Windows\Temp、Windows 更新缓存或系统升级残留时，
点击右上角“标准权限 · 提升”，以管理员身份重启。

删除方式
--------
- 临时文件 / 缓存 / 系统冗余：永久删除。
- 失效快捷方式 / 重复文件：移至回收站。
- 注册表残留：先用 reg.exe 导出 .reg 备份，再删除。
- 回收站：清空后不可恢复；默认不勾选，并需要高风险二次确认。

数据位置
--------
%LOCALAPPDATA%\CleanSweep\Logs     清理日志（JSON + TXT）
%LOCALAPPDATA%\CleanSweep\Backups  注册表备份
%LOCALAPPDATA%\CleanSweep\settings.json

便携模式：在 CleanSweep.exe 同目录放置空的 portable.flag，
数据会改存到 CleanSweepData\。

网络
----
完全离线，不发起网络请求，不上传文件名、路径或日志。

校验
----
见同目录 SHA256.txt。

规则与源码说明
--------------
见项目根目录 README.md。
