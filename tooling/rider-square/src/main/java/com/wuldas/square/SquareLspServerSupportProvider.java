package com.wuldas.square;

import com.intellij.execution.configurations.GeneralCommandLine;
import com.intellij.openapi.project.Project;
import com.intellij.openapi.vfs.VirtualFile;
import com.intellij.platform.lsp.api.LspServerSupportProvider;
import com.intellij.platform.lsp.api.ProjectWideLspServerDescriptor;

import java.net.URISyntaxException;
import java.nio.file.Path;

public final class SquareLspServerSupportProvider implements LspServerSupportProvider {
    @Override
    public void fileOpened(
            Project project,
            VirtualFile file,
            LspServerStarter serverStarter) {
        if (isSquareFile(file)) {
            serverStarter.ensureServerStarted(new SquareLspServerDescriptor(project));
        }
    }

    private static boolean isSquareFile(VirtualFile file) {
        var extension = file.getExtension();
        return "sqx".equalsIgnoreCase(extension) || "sqv".equalsIgnoreCase(extension);
    }

    private static final class SquareLspServerDescriptor extends ProjectWideLspServerDescriptor {
        private SquareLspServerDescriptor(Project project) {
            super(project, "Square Language Server");
        }

        @Override
        public boolean isSupportedFile(VirtualFile file) {
            return isSquareFile(file);
        }

        @Override
        public GeneralCommandLine createCommandLine() {
            return new GeneralCommandLine("dotnet", bundledServerPath().toString());
        }

        private static Path bundledServerPath() {
            try {
                var location = Path.of(SquareLspServerSupportProvider.class
                        .getProtectionDomain()
                        .getCodeSource()
                        .getLocation()
                        .toURI());
                var pluginRoot = location.toFile().isDirectory()
                        ? location
                        : location.getParent().getParent();
                return pluginRoot.resolve("server").resolve("Square.LanguageServer.dll");
            } catch (URISyntaxException exception) {
                throw new IllegalStateException("Cannot locate bundled Square Language Server", exception);
            }
        }
    }
}
