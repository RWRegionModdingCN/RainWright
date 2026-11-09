# RainWright 名称混淆构建

这是独立、可选的发布步骤，仅处理 RainWright.dll。使用固定版本 Obfuscar 2.2.50 修改 .NET 名称，使用 Mono.Cecil 0.11.6 整理调试符号及对应表。普通构建和游戏安装流程保持原样。

名称混淆会让反编译结果更难阅读，并不是把程序变成无法还原的密文。本配置不加密字符串或资源，不改变业务逻辑，不添加运行时解密依赖。

## 文件怎么用

- `release/RainWright.dll`：独立的混淆候选版本；本流程不会安装进游戏。验证游戏运行后再正式发布。
- `debug-private/RainWright.pdb`：和该 DLL 严格配套的 Portable PDB。源文件路径改为 `src/s00000.cs` 形式，局部变量改为 `v0` 等名称，保留原来的行号、列号及 IL 对应关系。
- `debug-private/symbol-map.json`、`name-map.csv`：混淆前后名称、最终元数据编号、参数及局部变量所在方法、源文件路径对应表。CSV 可以用表格软件查看。
- `debug-private/obfuscar-map.xml`：混淆工具自身导出的映射及跳过原因。
- `debug-private/compatibility-exclusions.json`：为兼容性保留名称的规则结果。
- `debug-private/validation.json`、`build-record.json`：实际覆盖数量、哈希、行号核对及验证范围。
- 两个 `example-*-stacktrace.txt`：实际执行混淆后的纯代码方法得到的异常，以及通过对应表还原的结果。

**发布包只取 release 文件夹。debug-private 整体自己保存，不随 Mod 发布。每次构建单独归档，不能拿别的版本的映射或 PDB 来调试。** DLL 内的普通业务字符串和资源仍然可读；专用 PDB 也可能包含命名空间导入等辅助信息。

## 哪些会保留名称

公共 API、已扫描的未混淆程序集对 RainWright 的引用、Unity/插件约定回调、枚举/序列化合同、被字符串引用的反射名称以及必要的基类/接口实现会保留。Cartography XML 记录通过字段名保存作者数据，这些字段名也必须保留。为保持完整的属性/事件元数据，事件名称保留。

参数名通常改为 `p0`、`p1`；兼容性方法的参数、带属性的参数保留原名。PDB 中原本存在的局部变量/常量名会替换并记录；Release 优化后已被编译器去掉的变量无法凭空恢复。类型、方法、字段和属性的实际改名数量请看 validation.json；并非承诺所有名字都能改。

PDB 的方法名与 DLL 的方法元数据对应，不存在另保留一份原函数名的要求。源文件用代号映射回原路径；原源码文件不会被重命名。原始名称集中留在私有对应表里。

## 构建与报错还原

在仓库根目录用 PowerShell 运行（需要 .NET 10 SDK；首次运行会下载固定版本构建工具）：

```powershell
./scripts/Protect-RainWright.ps1 -OutputDirectory 'E:/Application/RainWright-protected-build-001'
```

这是导出目录示例，并非安装位置。目录须为空，也不能位于游戏目录内。可通过 `-InputDll` 使用已有的 DLL 和同名 Portable PDB，避免重新编译。外部程序集如不在默认 Ancient Site 路径，通过 `-AdditionalConsumers` 指定。

收到异常日志后，用**相同版本**的 debug-private：

```powershell
./debug-private/Restore-StackTrace.ps1 -StackTrace './error.txt' -Output './restored-error.txt'
```

解码器需要 .NET 10 Runtime。它结合方法名、参数数量及日志已有的源文件代号/行号定位原方法，并在确定后还原参数名。若日志本身没有行号，对应表不能凭空推断行号；开发者调试时把私有 PDB 与对应 DLL 放在一起。普通玩家不携带 PDB，日志可能只有混淆方法名。仍有多个可能的原方法时，解码器列出候选项，结合签名、行号或元数据编号判断。

## 已验证和未验证的范围

工具核对程序集及依赖标识、嵌入资源、类型/成员签名、方法 IL 指令和偏移、所有 PDB 序列点以及未改动消费者的引用。还会在独立的 .NET 进程里调用真实的纯代码方法，验证异常行号及名称还原；不会启动 Unity 或 Rain World。

四类 Cartography 记录还会调用混淆后的实际 XML 读写方法，验证原字段名和往返结果。这些检查不能替代 Rain World/Mono 内的运行验收。此候选版本尚需验证启用 Mod、打开编辑器、加载已有 Cartography 文档、连接管道、撤销和保存，特别关注未被静态发现的动态反射调用。

实现依据：[Obfuscar 稳定版源码](https://github.com/obfuscar/obfuscar/tree/2.2.50)、[Mono.Cecil](https://github.com/jbevain/cecil)。
