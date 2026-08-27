using System.ComponentModel.Composition;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Utilities;

namespace Square.VisualStudio.LanguageServer
{
    internal static class SquareContentTypes
    {
        internal const string Sqx = "sqx";
        internal const string Sqv = "sqv";

        [Export]
        [Name(Sqx)]
        [BaseDefinition(CodeRemoteContentDefinition.CodeRemoteContentTypeName)]
        internal static ContentTypeDefinition SqxContentTypeDefinition = null!;

        [Export]
        [FileExtension(".sqx")]
        [ContentType(Sqx)]
        internal static FileExtensionToContentTypeDefinition SqxFileExtensionDefinition = null!;

        [Export]
        [Name(Sqv)]
        [BaseDefinition(CodeRemoteContentDefinition.CodeRemoteContentTypeName)]
        internal static ContentTypeDefinition SqvContentTypeDefinition = null!;

        [Export]
        [FileExtension(".sqv")]
        [ContentType(Sqv)]
        internal static FileExtensionToContentTypeDefinition SqvFileExtensionDefinition = null!;
    }
}
