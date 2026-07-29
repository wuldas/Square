using Microsoft.CodeAnalysis;

namespace Square.Compiler.Diagnostics;

public static class SqxDiagnostics
{
    public const string Category = "Square.SQX";

    public static readonly DiagnosticDescriptor SQX0001_SyntaxError = new(
        "SQX0001", "SQX 语法错误", "{0}", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor SQX0002_UndefinedControl = new(
        "SQX0002", "未定义的控件", "控件 '{0}' 未定义", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor SQX0003_RequiredPropMissing = new(
        "SQX0003", "必填 Prop 缺失", "组件 '{0}' 的必填 Prop '{1}' 未提供", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor SQX0004_BindingMemberNotFound = new(
        "SQX0004", "绑定成员未找到", "成员 '{0}' 未在组件中找到", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor SQX0005_EventSignatureMismatch = new(
        "SQX0005", "事件方法签名不匹配", "事件 '{0}' 的方法签名不匹配", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor SQX0006_RefNameConflict = new(
        "SQX0006", "ref 名称冲突", "ref 名称 '{0}' 冲突", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor SQX0007_PropTypeMismatch = new(
        "SQX0007", "Prop 类型不匹配", "Prop '{0}' 类型不匹配", Category, DiagnosticSeverity.Error, true);

    // ——— 结构指令（Directive SDK）———

    public static readonly DiagnosticDescriptor SQXD001_DuplicateDirective = new(
        "SQXD001", "重复的结构指令标签", "结构指令标签 '{0}' 重复注册", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor SQXD002_MissingRequiredAttribute = new(
        "SQXD002", "指令缺少必需属性", "结构指令 <{0}> 缺少必需属性 '{1}'", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor SQXD003_InvalidParent = new(
        "SQXD003", "指令父标签不匹配", "结构指令 <{0}> 必须位于 <{1}> 内", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor SQXD004_UnknownPattern = new(
        "SQXD004", "未知指令发射模式", "结构指令 <{0}> 的 Pattern '{1}' 无法解析", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor SQXD005_IllegalStandalone = new(
        "SQXD005", "指令出现在非法位置", "结构指令 <{0}> 不能作为独立节点发射（SkipStandalone）", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor SQXD006_InvalidChild = new(
        "SQXD006", "结构指令子标签不匹配", "结构指令 <{0}> 只允许直接子标签 {1}，实际为 <{2}>", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor SQXD007_UnsupportedControlFlowShape = new(
        "SQXD007", "不支持的结构指令形状",
        "结构指令 <{0}> 的 ControlFlowAttach 形状不受支持；第三方条件指令必须同时声明 RuntimeTypeName、FieldPrefix 和 PrimaryAttribute",
        Category, DiagnosticSeverity.Error, true);
}
