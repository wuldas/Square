namespace Square.Compiler.Syntax;

internal enum ComponentSectionKind
{
    Template,
    Script,
    Style
}

internal enum ComponentSectionDiagnosticKind
{
    MissingTemplate,
    DuplicateSection,
    UnclosedOpeningTag,
    UnclosedSection,
    UnclosedClosingTag,
    UnknownSection,
    UnexpectedContent,
    InvalidSection,
    UnclosedComment
}
