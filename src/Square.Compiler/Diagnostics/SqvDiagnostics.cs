using Microsoft.CodeAnalysis;

namespace Square.Compiler.Diagnostics;

public static class SqvDiagnostics
{
    public const string Category = "Square.SQV";

    public static readonly DiagnosticDescriptor SQV0001_SyntaxError = new(
        "SQV0001", "Vue 模板语法错误", "{0}", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor SQV0002_UnsupportedDirective = new(
        "SQV0002", "不支持的 Vue 指令", "{0}", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor SQV0003_InvalidVFor = new(
        "SQV0003", "无效的 v-for 表达式", "{0}", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor SQV0004_OrphanedElse = new(
        "SQV0004", "无效的条件分支", "{0}", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor SQV0005_DuplicateBinding = new(
        "SQV0005", "重复的属性或事件绑定", "{0}", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor SQV0006_DynamicArgument = new(
        "SQV0006", "无效的动态参数", "{0}", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor SQV0007_UnsupportedBuiltIn = new(
        "SQV0007", "不支持的 Vue 内置组件", "{0}", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor SQV0008_ScopedSlot = new(
        "SQV0008", "无效或不支持的作用域插槽属性形式", "{0}", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor SQV0009_InvalidExpression = new(
        "SQV0009", "模板表达式必须是 C# 表达式", "{0}", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor SQV0010_SlotContractMissing = new(
        "SQV0010", "作用域插槽缺少类型契约", "{0}", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor SQV0011_SlotPropertyMissing = new(
        "SQV0011", "作用域插槽属性不存在", "{0}", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor SQV0012_DynamicSlotDestructuring = new(
        "SQV0012", "动态插槽不能使用类型化解构", "{0}", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor SQV0013_SemanticError = new(
        "SQV0013", "模板 C# 语义错误", "{0}", Category, DiagnosticSeverity.Error, true);

    public static DiagnosticDescriptor Get(string id) => id switch
    {
        "SQV0002" => SQV0002_UnsupportedDirective,
        "SQV0003" => SQV0003_InvalidVFor,
        "SQV0004" => SQV0004_OrphanedElse,
        "SQV0005" => SQV0005_DuplicateBinding,
        "SQV0006" => SQV0006_DynamicArgument,
        "SQV0007" => SQV0007_UnsupportedBuiltIn,
        "SQV0008" => SQV0008_ScopedSlot,
        "SQV0009" => SQV0009_InvalidExpression,
        "SQV0010" => SQV0010_SlotContractMissing,
        "SQV0011" => SQV0011_SlotPropertyMissing,
        "SQV0012" => SQV0012_DynamicSlotDestructuring,
        "SQV0013" => SQV0013_SemanticError,
        _ => SQV0001_SyntaxError
    };
}
